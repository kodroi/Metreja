#include "CallStack.h"

std::atomic<long long> CallStackManager::s_frequency{0};
std::once_flag CallStackManager::s_frequencyOnce;

CallStackManager::CallStackManager()
    : m_tlsIndex(PalTlsAlloc())
{
    InitFrequency();
}

CallStackManager::~CallStackManager()
{
    // Note: individual ThreadCallStack objects are leaked intentionally.
    // Cleaning them up would require tracking all threads, which adds
    // complexity for minimal benefit (process is shutting down anyway).
    if (m_tlsIndex != PAL_TLS_INVALID)
        PalTlsFree(m_tlsIndex);
}

void CallStackManager::Push(UINT_PTR functionId, long long timestamp)
{
    auto* stack = GetOrCreateStack();
    if (stack != nullptr)
    {
        stack->stack.push_back({functionId, timestamp});
    }
}

CallEntry CallStackManager::Pop()
{
    auto* stack = GetOrCreateStack();
    if (stack == nullptr || stack->stack.empty())
        return {0, 0, 0};

    CallEntry entry = stack->stack.back();
    stack->stack.pop_back();
    return entry;
}

void CallStackManager::CreditParent(long long inclusiveNs)
{
    if (m_tlsIndex == PAL_TLS_INVALID)
        return;

    auto* stack = static_cast<ThreadCallStack*>(PalTlsGetValue(m_tlsIndex));
    if (stack != nullptr && !stack->stack.empty() && inclusiveNs > 0)
        stack->stack.back().childrenTimeNs += inclusiveNs;
}

int CallStackManager::GetDepth() const
{
    if (m_tlsIndex == PAL_TLS_INVALID)
        return 0;

    auto* stack = static_cast<ThreadCallStack*>(PalTlsGetValue(m_tlsIndex));
    if (stack == nullptr)
        return 0;

    return static_cast<int>(stack->stack.size());
}

ThreadCallStack* CallStackManager::GetThreadStack() { return GetOrCreateStack(); }

long long CallStackManager::GetTimestampNs()
{
    long long ticks;
    PalQueryPerformanceCounter(ticks);

    // Overflow-safe conversion: ns = (ticks / freq) * 1e9 + ((ticks % freq) * 1e9) / freq
    long long freq = s_frequency.load(std::memory_order_relaxed);
    long long seconds = ticks / freq;
    long long remainder = ticks % freq;
    return seconds * 1000000000LL + (remainder * 1000000000LL) / freq;
}

void CallStackManager::InitFrequency()
{
    std::call_once(s_frequencyOnce,
                   []()
                   {
                       long long freq;
                       PalQueryPerformanceFrequency(freq);
                       s_frequency.store(freq, std::memory_order_relaxed);
                   });
}

ThreadCallStack* CallStackManager::GetOrCreateStack()
{
    if (m_tlsIndex == PAL_TLS_INVALID)
        return nullptr;

    auto* stack = static_cast<ThreadCallStack*>(PalTlsGetValue(m_tlsIndex));
    if (stack == nullptr)
    {
        stack = new (std::nothrow) ThreadCallStack();
        if (stack != nullptr)
            PalTlsSetValue(m_tlsIndex, stack);
    }
    return stack;
}
