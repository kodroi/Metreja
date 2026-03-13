#pragma once

#include <atomic>
#include <memory>
#include <string>
#include "ConfigReader.h"
#include "platform/pal_threading.h"

class MethodCache;
class CallStackManager;
class NdjsonWriter;
class StatsAggregator;

struct ProfilerContext
{
    ProfilerConfig config;
    std::string sessionId;
    std::atomic<long long> gcStartNs{0};
    std::unique_ptr<MethodCache> methodCache;
    std::unique_ptr<CallStackManager> callStackManager;
    std::unique_ptr<NdjsonWriter> ndjsonWriter;
    std::unique_ptr<StatsAggregator> statsAggregator;
    PalNamedSemaphore m_manualFlushEvent = PAL_INVALID_SEMAPHORE;
};
