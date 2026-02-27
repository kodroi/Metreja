#include "CallStack.h"

long long CallStackManager::s_frequency = 0;

CallStackManager::CallStackManager()
    : m_tlsIndex(TlsAlloc())
{
    InitFrequency();
}

CallStackManager::~CallStackManager()
{
    // Note: individual ThreadCallStack objects are leaked intentionally.
    // Cleaning them up would require tracking all threads, which adds
    // complexity for minimal benefit (process is shutting down anyway).
    if (m_tlsIndex != TLS_OUT_OF_INDEXES)
        TlsFree(m_tlsIndex);
}

void CallStackManager::Push(DWORD tid, UINT_PTR functionId, long long timestamp)
{
    auto* stack = GetOrCreateStack(tid);
    if (stack != nullptr)
    {
        stack->stack.push_back({ functionId, timestamp });
    }
}

CallEntry CallStackManager::Pop(DWORD tid)
{
    auto* stack = GetOrCreateStack(tid);
    if (stack == nullptr || stack->stack.empty())
        return { 0, 0 };

    CallEntry entry = stack->stack.back();
    stack->stack.pop_back();
    return entry;
}

int CallStackManager::GetDepth(DWORD tid) const
{
    if (m_tlsIndex == TLS_OUT_OF_INDEXES)
        return 0;

    auto* stack = static_cast<ThreadCallStack*>(TlsGetValue(m_tlsIndex));
    if (stack == nullptr)
        return 0;

    return static_cast<int>(stack->stack.size());
}

ThreadCallStack* CallStackManager::GetThreadStack(DWORD tid)
{
    return GetOrCreateStack(tid);
}

long long CallStackManager::GetTimestampNs()
{
    LARGE_INTEGER counter;
    QueryPerformanceCounter(&counter);
    long long ticks = counter.QuadPart;

    // Overflow-safe conversion: ns = (ticks / freq) * 1e9 + ((ticks % freq) * 1e9) / freq
    long long seconds = ticks / s_frequency;
    long long remainder = ticks % s_frequency;
    return seconds * 1000000000LL + (remainder * 1000000000LL) / s_frequency;
}

void CallStackManager::InitFrequency()
{
    if (s_frequency == 0)
    {
        LARGE_INTEGER freq;
        QueryPerformanceFrequency(&freq);
        s_frequency = freq.QuadPart;
    }
}

ThreadCallStack* CallStackManager::GetOrCreateStack(DWORD /*tid*/)
{
    if (m_tlsIndex == TLS_OUT_OF_INDEXES)
        return nullptr;

    auto* stack = static_cast<ThreadCallStack*>(TlsGetValue(m_tlsIndex));
    if (stack == nullptr)
    {
        stack = new (std::nothrow) ThreadCallStack();
        if (stack != nullptr)
            TlsSetValue(m_tlsIndex, stack);
    }
    return stack;
}
