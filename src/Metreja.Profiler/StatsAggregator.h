#pragma once

#include "include/profiling.h"
#include "platform/pal_threading.h"
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
    std::mutex mutex;
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
    void StartPeriodicFlush(int intervalSeconds, NdjsonWriter* writer, MethodCache* cache,
                            PalNamedSemaphore manualFlushEvent = PAL_INVALID_SEMAPHORE);
    void StopPeriodicFlush();

private:
    ThreadStats* GetOrCreateThreadStats();
    void CollectDeltaStats(std::unordered_map<FunctionID, MethodStatsAccum>& outMethods,
                           std::unordered_map<std::string, ExceptionStatsAccum>& outExceptions);
    static void WriteMergedStats(NdjsonWriter& writer, MethodCache& cache,
                                 const std::unordered_map<FunctionID, MethodStatsAccum>& methods,
                                 const std::unordered_map<std::string, ExceptionStatsAccum>& exceptions);

#ifdef _WIN32
    static unsigned __stdcall FlushThreadProc(void* param);
#else
    static void* FlushThreadProc(void* param);
#endif

    PalTlsIndex m_tlsIndex;
    std::mutex m_registryMutex;
    std::vector<ThreadStats*> m_allThreadStats;

    // Periodic flush state
    PalEventHandle m_shutdownEvent = PAL_INVALID_EVENT;
    PalThreadHandle m_flushThread = PAL_INVALID_THREAD;
    PalNamedSemaphore m_manualFlushEvent = PAL_INVALID_SEMAPHORE;
    NdjsonWriter* m_writer = nullptr;
    MethodCache* m_cache = nullptr;
    int m_flushIntervalSeconds = 0;
};
