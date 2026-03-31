#pragma once

#include "include/profiling.h"
#include "platform/pal_threading.h"
#include "platform/pal_time.h"
#include <atomic>
#include <mutex>
#include <unordered_map>
#include <vector>

struct CallEntry
{
    UINT_PTR functionId;
    long long enterTsNs;
    long long childrenTimeNs = 0;
};

struct ThreadCallStack
{
    std::vector<CallEntry> stack;
    UINT_PTR exceptionCatcherFunctionId = 0;

    // Deferred unwind entry: when ExceptionUnwindFunctionEnter sees a frame
    // matching the catcher's FunctionID, we pop and defer rather than finalize.
    // The LAST deferred entry is the catcher (restored in ExceptionCatcherEnter).
    // Earlier deferred entries (inner recursive activations) are finalized when
    // a subsequent matching frame is encountered.
    bool m_hasDeferredUnwind = false;
    CallEntry m_deferredUnwindEntry = {0, 0, 0};
    UINT_PTR m_deferredUnwindFunctionId = 0;
    long long m_deferredUnwindTsNs = 0;

    // Async wall-time tracking: track first enter and nesting depth per FunctionID
    std::unordered_map<UINT_PTR, long long> m_asyncFirstEnterNs;
    std::unordered_map<UINT_PTR, int> m_asyncNestingCount;

    ThreadCallStack() { stack.reserve(256); }
};

class CallStackManager
{
public:
    CallStackManager();
    ~CallStackManager();
    CallStackManager(const CallStackManager&) = delete;
    CallStackManager& operator=(const CallStackManager&) = delete;
    CallStackManager(CallStackManager&&) = delete;
    CallStackManager& operator=(CallStackManager&&) = delete;

    void Push(UINT_PTR functionId, long long timestamp);
    CallEntry Pop();
    void CreditParent(long long inclusiveNs);
    int GetDepth() const;
    ThreadCallStack* GetThreadStack();

    static long long GetTimestampNs();
    static void InitFrequency();

private:
    ThreadCallStack* GetOrCreateStack();

    PalTlsIndex m_tlsIndex;
    static std::atomic<long long> s_frequency;
    static std::once_flag s_frequencyOnce;
};
