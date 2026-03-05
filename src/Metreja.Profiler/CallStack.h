#pragma once

#include <Windows.h>
#include <atomic>
#include <mutex>
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
