#include "StatsAggregator.h"

#include "NdjsonWriter.h"

#include <algorithm>
#include <process.h>

StatsAggregator::StatsAggregator()
    : m_tlsIndex(TlsAlloc())
{
}

StatsAggregator::~StatsAggregator()
{
    StopPeriodicFlush();

    // ThreadStats objects are leaked intentionally (same pattern as CallStack).
    // Process is shutting down anyway.
    if (m_tlsIndex != TLS_OUT_OF_INDEXES)
        TlsFree(m_tlsIndex);
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
                                         HANDLE manualFlushEvent)
{
    if (writer == nullptr || cache == nullptr)
        return;

    // Nothing to do if neither periodic nor manual flush is requested
    if (intervalSeconds <= 0 && manualFlushEvent == nullptr)
        return;

    // Prevent double-start: if already running, bail out
    if (m_flushThread != nullptr)
        return;

    m_flushIntervalSeconds = intervalSeconds;
    m_writer = writer;
    m_cache = cache;
    m_manualFlushEvent = manualFlushEvent;

    m_shutdownEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (m_shutdownEvent == nullptr)
        return;

    unsigned threadId = 0;
    m_flushThread = reinterpret_cast<HANDLE>(_beginthreadex(nullptr, 0, FlushThreadProc, this, 0, &threadId));
    if (m_flushThread == nullptr)
    {
        CloseHandle(m_shutdownEvent);
        m_shutdownEvent = nullptr;
    }
}

void StatsAggregator::StopPeriodicFlush()
{
    if (m_shutdownEvent != nullptr)
        SetEvent(m_shutdownEvent);

    if (m_flushThread != nullptr)
    {
        WaitForSingleObject(m_flushThread, INFINITE);
        CloseHandle(m_flushThread);
        m_flushThread = nullptr;
    }

    if (m_shutdownEvent != nullptr)
    {
        CloseHandle(m_shutdownEvent);
        m_shutdownEvent = nullptr;
    }
}

unsigned __stdcall StatsAggregator::FlushThreadProc(void* param)
{
    auto* self = static_cast<StatsAggregator*>(param);
    DWORD intervalMs =
        (self->m_flushIntervalSeconds > 0) ? static_cast<DWORD>(self->m_flushIntervalSeconds) * 1000 : INFINITE;

    // Build event array: [shutdown, manualFlush (optional)]
    HANDLE events[2] = {self->m_shutdownEvent, nullptr};
    DWORD eventCount = 1;
    if (self->m_manualFlushEvent != nullptr)
    {
        events[eventCount] = self->m_manualFlushEvent;
        eventCount++;
    }

    while (true)
    {
        DWORD result = WaitForMultipleObjects(eventCount, events, FALSE, intervalMs);
        if (result == WAIT_OBJECT_0)
            break; // Shutdown signaled

        // Only flush on timeout (periodic) or manual flush signal
        if (result != WAIT_TIMEOUT && !(result > WAIT_OBJECT_0 && result < WAIT_OBJECT_0 + eventCount))
            continue; // WAIT_FAILED or unexpected — skip

        // Timeout (periodic) or manual flush signal — flush delta stats
        std::unordered_map<FunctionID, MethodStatsAccum> methods;
        std::unordered_map<std::string, ExceptionStatsAccum> exceptions;
        self->CollectDeltaStats(methods, exceptions);
        self->WriteMergedStats(*self->m_writer, *self->m_cache, methods, exceptions);
        self->m_writer->Flush();
    }

    return 0;
}

ThreadStats* StatsAggregator::GetOrCreateThreadStats()
{
    if (m_tlsIndex == TLS_OUT_OF_INDEXES)
        return nullptr;

    auto* stats = static_cast<ThreadStats*>(TlsGetValue(m_tlsIndex));
    if (stats == nullptr)
    {
        stats = new (std::nothrow) ThreadStats();
        if (stats != nullptr)
        {
            TlsSetValue(m_tlsIndex, stats);
            std::lock_guard<std::mutex> lock(m_registryMutex);
            m_allThreadStats.push_back(stats);
        }
    }
    return stats;
}
