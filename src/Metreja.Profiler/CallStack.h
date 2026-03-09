#pragma once

#include <Windows.h>
#include <atomic>
#include <mutex>
#include <vector>

struct CallEntry
{
    UINT_PTR functionId;
    long long enterTsNs;
    long long m_childrenTimeNs = 0;
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

    DWORD m_tlsIndex;
    static std::atomic<long long> s_frequency;
    static std::once_flag s_frequencyOnce;
};
