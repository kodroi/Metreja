#pragma once

#include <Windows.h>
#include <vector>

struct CallEntry
{
    UINT_PTR functionId;
    long long enterTsNs;
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

    void Push(UINT_PTR functionId, long long timestamp);
    CallEntry Pop();
    int GetDepth() const;
    ThreadCallStack* GetThreadStack();

    static long long GetTimestampNs();
    static void InitFrequency();

private:
    ThreadCallStack* GetOrCreateStack();

    DWORD m_tlsIndex;
    static long long s_frequency;
};
