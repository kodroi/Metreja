#pragma once

#include <Windows.h>
#include <vector>

struct CallEntry
{
    UINT_PTR functionId;
    long long enterTimestamp;
};

struct ThreadCallStack
{
    std::vector<CallEntry> stack;
    ThreadCallStack() { stack.reserve(256); }
};

class CallStackManager
{
public:
    CallStackManager();
    ~CallStackManager();

    void Push(DWORD tid, UINT_PTR functionId, long long timestamp);
    CallEntry Pop(DWORD tid);
    int GetDepth(DWORD tid) const;
    ThreadCallStack* GetThreadStack(DWORD tid);

    static long long GetTimestampNs();
    static void InitFrequency();

private:
    ThreadCallStack* GetOrCreateStack(DWORD tid);

    DWORD m_tlsIndex;
    static long long s_frequency;
};
