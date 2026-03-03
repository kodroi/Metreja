#include "StatsAggregator.h"

#include "NdjsonWriter.h"

#include <algorithm>

StatsAggregator::StatsAggregator()
    : m_tlsIndex(TlsAlloc())
{
}

StatsAggregator::~StatsAggregator()
{
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

    // Key: exType + ":" + fully qualified method name
    std::string key = exType + ":" + callerInfo.assemblyName + "." + callerInfo.namespaceName + "." +
                      callerInfo.className + "." + callerInfo.methodName;

    auto& accum = stats->exceptionStats[key];
    if (accum.count == 0)
    {
        accum.assemblyName = callerInfo.assemblyName;
        accum.namespaceName = callerInfo.namespaceName;
        accum.className = callerInfo.className;
        accum.methodName = callerInfo.methodName;
    }
    accum.count++;
}

void StatsAggregator::Flush(NdjsonWriter& writer, MethodCache& cache)
{
    // Merge all thread-local maps into global maps (single-threaded at shutdown)
    std::unordered_map<FunctionID, MethodStatsAccum> mergedMethods;
    std::unordered_map<std::string, ExceptionStatsAccum> mergedExceptions;

    for (auto* threadStats : m_allThreadStats)
    {
        for (auto& [funcId, accum] : threadStats->methodStats)
        {
            auto& merged = mergedMethods[funcId];
            merged.callCount += accum.callCount;
            merged.totalSelfNs += accum.totalSelfNs;
            merged.maxSelfNs = (std::max)(merged.maxSelfNs, accum.maxSelfNs);
            merged.totalInclusiveNs += accum.totalInclusiveNs;
            merged.maxInclusiveNs = (std::max)(merged.maxInclusiveNs, accum.maxInclusiveNs);
        }

        for (auto& [key, accum] : threadStats->exceptionStats)
        {
            auto& merged = mergedExceptions[key];
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

    // Write method_stats events
    for (auto& [funcId, accum] : mergedMethods)
    {
        const MethodInfo* info = cache.Lookup(funcId);
        if (info != nullptr)
            writer.WriteMethodStats(*info, accum);
    }

    // Write exception_stats events
    for (auto& [key, accum] : mergedExceptions)
    {
        // Extract exType from key (everything before first ':')
        size_t colonPos = key.find(':');
        std::string exType = (colonPos != std::string::npos) ? key.substr(0, colonPos) : key;
        writer.WriteExceptionStats(exType, accum);
    }
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
