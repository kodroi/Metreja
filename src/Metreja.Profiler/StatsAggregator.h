#pragma once

#include <Windows.h>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>
#include "MethodCache.h"

class NdjsonWriter;

struct MethodStatsAccum
{
    long long callCount = 0;
    long long totalSelfNs = 0;
    long long maxSelfNs = 0;
    long long totalInclusiveNs = 0;
    long long maxInclusiveNs = 0;
};

struct ExceptionStatsAccum
{
    long long count = 0;
    std::string assemblyName;
    std::string namespaceName;
    std::string className;
    std::string methodName;
};

struct ThreadStats
{
    std::unordered_map<FunctionID, MethodStatsAccum> methodStats;
    std::unordered_map<std::string, ExceptionStatsAccum> exceptionStats;
};

class StatsAggregator
{
public:
    StatsAggregator();
    ~StatsAggregator();
    StatsAggregator(const StatsAggregator&) = delete;
    StatsAggregator& operator=(const StatsAggregator&) = delete;
    StatsAggregator(StatsAggregator&&) = delete;
    StatsAggregator& operator=(StatsAggregator&&) = delete;

    void RecordMethod(FunctionID functionId, long long inclusiveNs, long long selfNs);
    void RecordException(const MethodInfo& callerInfo, const std::string& exType);
    void Flush(NdjsonWriter& writer, MethodCache& cache);

private:
    ThreadStats* GetOrCreateThreadStats();

    DWORD m_tlsIndex;
    std::mutex m_registryMutex;
    std::vector<ThreadStats*> m_allThreadStats;
};
