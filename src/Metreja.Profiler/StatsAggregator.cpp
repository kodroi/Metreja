#include "StatsAggregator.h"

#include "NdjsonWriter.h"

#include <algorithm>

StatsAggregator::StatsAggregator()
    : m_tlsIndex(PalTlsAlloc())
{
}

StatsAggregator::~StatsAggregator()
{
    StopPeriodicFlush();

    // ThreadStats objects are leaked intentionally (same pattern as CallStack).
    // Process is shutting down anyway.
    if (m_tlsIndex != PAL_TLS_INVALID)
        PalTlsFree(m_tlsIndex);
}

void StatsAggregator::RecordMethod(FunctionID functionId, long long inclusiveNs, long long selfNs)
{
    auto* stats = GetOrCreateThreadStats();
    if (stats == nullptr)
        return;

    std::lock_guard<std::mutex> lock(stats->mutex);
    auto& accum = stats->methodStats[functionId];
    accum.callCount++;
    accum.totalSelfNs += selfNs;
    if (selfNs > accum.maxSelfNs)
        accum.maxSelfNs = selfNs;
    accum.totalInclusiveNs += inclusiveNs;
    if (inclusiveNs > accum.maxInclusiveNs)
        accum.maxInclusiveNs = inclusiveNs;
}

void StatsAggregator::RecordException(const MethodInfo& callerInfo, const std::string& exType)
{
    auto* stats = GetOrCreateThreadStats();
    if (stats == nullptr)
        return;

    // Key: exType + ":" + fully qualified method name (resolved for async)
    const std::string& methodName = callerInfo.isAsyncStateMachine && !callerInfo.originalMethodName.empty()
                                        ? callerInfo.originalMethodName
                                        : callerInfo.methodName;
    std::string key = exType + ":" + callerInfo.assemblyName + "." + callerInfo.namespaceName + "." +
                      callerInfo.className + "." + methodName;

    std::lock_guard<std::mutex> lock(stats->mutex);
    auto& accum = stats->exceptionStats[key];
    if (accum.count == 0)
    {
        accum.assemblyName = callerInfo.assemblyName;
        accum.namespaceName = callerInfo.namespaceName;
        accum.className = callerInfo.className;
        accum.methodName = methodName;
    }
    accum.count++;
}

void StatsAggregator::CollectDeltaStats(std::unordered_map<FunctionID, MethodStatsAccum>& outMethods,
                                        std::unordered_map<std::string, ExceptionStatsAccum>& outExceptions)
{
    std::lock_guard<std::mutex> registryLock(m_registryMutex);

    for (auto* threadStats : m_allThreadStats)
    {
        std::unordered_map<FunctionID, MethodStatsAccum> swappedMethods;
        std::unordered_map<std::string, ExceptionStatsAccum> swappedExceptions;

        {
            std::lock_guard<std::mutex> threadLock(threadStats->mutex);
            swappedMethods.swap(threadStats->methodStats);
            swappedExceptions.swap(threadStats->exceptionStats);
        }

        for (auto& [funcId, accum] : swappedMethods)
        {
            auto& merged = outMethods[funcId];
            merged.callCount += accum.callCount;
            merged.totalSelfNs += accum.totalSelfNs;
            merged.maxSelfNs = (std::max)(merged.maxSelfNs, accum.maxSelfNs);
            merged.totalInclusiveNs += accum.totalInclusiveNs;
            merged.maxInclusiveNs = (std::max)(merged.maxInclusiveNs, accum.maxInclusiveNs);
        }

        for (auto& [key, accum] : swappedExceptions)
        {
            auto& merged = outExceptions[key];
            if (merged.count == 0)
            {
                merged.assemblyName = accum.assemblyName;
                merged.namespaceName = accum.namespaceName;
                merged.className = accum.className;
                merged.methodName = accum.methodName;
            }
            merged.count += accum.count;
        }
    }
}

void StatsAggregator::WriteMergedStats(NdjsonWriter& writer, MethodCache& cache,
                                       const std::unordered_map<FunctionID, MethodStatsAccum>& methods,
                                       const std::unordered_map<std::string, ExceptionStatsAccum>& exceptions)
{
    for (auto& [funcId, accum] : methods)
    {
        const MethodInfo* info = cache.Lookup(funcId);
        if (info != nullptr)
            writer.WriteMethodStats(*info, accum);
    }

    for (auto& [key, accum] : exceptions)
    {
        size_t colonPos = key.find(':');
        std::string exType = (colonPos != std::string::npos) ? key.substr(0, colonPos) : key;
        writer.WriteExceptionStats(exType, accum);
    }
}

void StatsAggregator::Flush(NdjsonWriter& writer, MethodCache& cache)
{
    std::unordered_map<FunctionID, MethodStatsAccum> mergedMethods;
    std::unordered_map<std::string, ExceptionStatsAccum> mergedExceptions;
    CollectDeltaStats(mergedMethods, mergedExceptions);
    WriteMergedStats(writer, cache, mergedMethods, mergedExceptions);
}

void StatsAggregator::StartPeriodicFlush(int intervalSeconds, NdjsonWriter* writer, MethodCache* cache,
                                         PalNamedSemaphore manualFlushEvent)
{
    if (writer == nullptr || cache == nullptr)
        return;

    // Nothing to do if neither periodic nor manual flush is requested
    if (intervalSeconds <= 0 && manualFlushEvent == PAL_INVALID_SEMAPHORE)
        return;

    // Prevent double-start: if already running, bail out
    if (m_flushThread != PAL_INVALID_THREAD)
        return;

    m_flushIntervalSeconds = intervalSeconds;
    m_writer = writer;
    m_cache = cache;
    m_manualFlushEvent = manualFlushEvent;

    m_shutdownEvent = PalCreateEvent(true, false); // manual-reset, initially non-signaled
    if (m_shutdownEvent == PAL_INVALID_EVENT)
        return;

    m_flushThread = PalCreateThreadUnified(FlushThreadProc, this);
    if (m_flushThread == PAL_INVALID_THREAD)
    {
        PalCloseEvent(m_shutdownEvent);
        m_shutdownEvent = PAL_INVALID_EVENT;
    }
}

void StatsAggregator::StopPeriodicFlush()
{
    if (m_shutdownEvent != PAL_INVALID_EVENT)
        PalSetEvent(m_shutdownEvent);

    if (m_flushThread != PAL_INVALID_THREAD)
    {
        PalJoinThread(m_flushThread);
        m_flushThread = PAL_INVALID_THREAD;
    }

    if (m_shutdownEvent != PAL_INVALID_EVENT)
    {
        PalCloseEvent(m_shutdownEvent);
        m_shutdownEvent = PAL_INVALID_EVENT;
    }
}

void* StatsAggregator::FlushThreadProc(void* param)
{
    auto* self = static_cast<StatsAggregator*>(param);
    DWORD intervalMs =
        (self->m_flushIntervalSeconds > 0) ? static_cast<DWORD>(self->m_flushIntervalSeconds) * 1000 : INFINITE;

    // Build wait set for PalWaitMultiple (handles shutdown + timeout)
    PalWaitSet ws;
    ws.shutdownEvent = self->m_shutdownEvent;
    ws.manualFlushEvent = PAL_INVALID_EVENT;
    ws.intervalMs = intervalMs;

    while (true)
    {
        // Check for manual flush signal via named semaphore (non-blocking)
        bool manualFlush = PalTryWaitNamedSemaphore(self->m_manualFlushEvent);
        if (manualFlush)
        {
            // Flush immediately without waiting
            std::unordered_map<FunctionID, MethodStatsAccum> methods;
            std::unordered_map<std::string, ExceptionStatsAccum> exceptions;
            self->CollectDeltaStats(methods, exceptions);
            self->WriteMergedStats(*self->m_writer, *self->m_cache, methods, exceptions);
            self->m_writer->Flush();
            continue;
        }

        // Wait for shutdown or periodic timeout (poll named semaphore via short timeout)
        PalWaitSet pollWs = ws;
        if (self->m_manualFlushEvent != PAL_INVALID_SEMAPHORE)
        {
            // Use short poll interval so we notice manual flush signals promptly
            DWORD pollMs = 250;
            pollWs.intervalMs = (intervalMs == INFINITE) ? pollMs : (intervalMs < pollMs ? intervalMs : pollMs);
        }

        PalWaitResult result = PalWaitMultiple(pollWs);
        if (result == PalWaitResult::Shutdown)
            break;
        if (result == PalWaitResult::Error)
            break;

        // On timeout: check if it's a real periodic timeout or just a poll cycle
        if (result == PalWaitResult::Timeout && self->m_manualFlushEvent != PAL_INVALID_SEMAPHORE &&
            pollWs.intervalMs != intervalMs)
        {
            // This was a short poll cycle — only flush if the full periodic interval has elapsed
            if (intervalMs == INFINITE)
                continue; // Periodic disabled; only manual flush triggers output
        }

        // Flush delta stats
        std::unordered_map<FunctionID, MethodStatsAccum> methods;
        std::unordered_map<std::string, ExceptionStatsAccum> exceptions;
        self->CollectDeltaStats(methods, exceptions);
        if (!methods.empty() || !exceptions.empty())
        {
            self->WriteMergedStats(*self->m_writer, *self->m_cache, methods, exceptions);
            self->m_writer->Flush();
        }
    }

    return nullptr;
}

ThreadStats* StatsAggregator::GetOrCreateThreadStats()
{
    if (m_tlsIndex == PAL_TLS_INVALID)
        return nullptr;

    auto* stats = static_cast<ThreadStats*>(PalTlsGetValue(m_tlsIndex));
    if (stats == nullptr)
    {
        stats = new (std::nothrow) ThreadStats();
        if (stats != nullptr)
        {
            PalTlsSetValue(m_tlsIndex, stats);
            std::lock_guard<std::mutex> lock(m_registryMutex);
            m_allThreadStats.push_back(stats);
        }
    }
    return stats;
}
