#pragma once

// Platform Abstraction Layer — Threading, TLS, Events, Synchronization

#ifdef _WIN32

#include <Windows.h>
#include <process.h>

// ─── TLS ──────────────────────────────────────────────────────────────────────
typedef DWORD PalTlsIndex;
#define PAL_TLS_INVALID TLS_OUT_OF_INDEXES

inline PalTlsIndex PalTlsAlloc() { return TlsAlloc(); }
inline void PalTlsFree(PalTlsIndex index) { TlsFree(index); }
inline void* PalTlsGetValue(PalTlsIndex index) { return TlsGetValue(index); }
inline void PalTlsSetValue(PalTlsIndex index, void* value) { TlsSetValue(index, value); }

// ─── Events ───────────────────────────────────────────────────────────────────
// Windows: thin wrappers around HANDLE-based events

typedef HANDLE PalEventHandle;
#define PAL_INVALID_EVENT nullptr

inline PalEventHandle PalCreateEvent(bool manualReset, bool initialState)
{
    return CreateEventW(nullptr, manualReset ? TRUE : FALSE, initialState ? TRUE : FALSE, nullptr);
}
inline void PalSetEvent(PalEventHandle h) { SetEvent(h); }
inline void PalCloseEvent(PalEventHandle h)
{
    if (h != PAL_INVALID_EVENT)
        CloseHandle(h);
}

// Wait on single event. Returns true if signaled, false on timeout.
inline bool PalWaitEvent(PalEventHandle h, DWORD timeoutMs)
{
    return WaitForSingleObject(h, timeoutMs) == WAIT_OBJECT_0;
}

// ─── Named semaphore ──────────────────────────────────────────────────────────
// Windows: Named event (existing behavior)

typedef HANDLE PalNamedSemaphore;
#define PAL_INVALID_SEMAPHORE nullptr

inline PalNamedSemaphore PalCreateNamedSemaphore(const char* name)
{
    // Convert narrow name to wide for CreateEventW
    wchar_t wideName[128];
    int i = 0;
    for (; name[i] && i < 127; i++)
        wideName[i] = static_cast<wchar_t>(name[i]);
    wideName[i] = L'\0';
    return CreateEventW(nullptr, FALSE, FALSE, wideName);
}
inline void PalSignalNamedSemaphore(PalNamedSemaphore h) { SetEvent(h); }
inline bool PalTryWaitNamedSemaphore(PalNamedSemaphore h)
{
    if (h == PAL_INVALID_SEMAPHORE)
        return false;
    return WaitForSingleObject(h, 0) == WAIT_OBJECT_0;
}
inline void PalCloseNamedSemaphore(PalNamedSemaphore h)
{
    if (h != PAL_INVALID_SEMAPHORE)
        CloseHandle(h);
}

// ─── Threads ──────────────────────────────────────────────────────────────────

typedef HANDLE PalThreadHandle;
#define PAL_INVALID_THREAD nullptr

typedef unsigned(__stdcall* PalThreadProc)(void*);

inline PalThreadHandle PalCreateThread(PalThreadProc proc, void* param)
{
    unsigned threadId = 0;
    return reinterpret_cast<HANDLE>(_beginthreadex(nullptr, 0, proc, param, 0, &threadId));
}

// Unified thread creation: accepts a void*(*)(void*) proc and adapts it for _beginthreadex.
typedef void* (*PalUnifiedThreadProc)(void*);

struct PalUnifiedThreadContext
{
    PalUnifiedThreadProc proc;
    void* param;
};

inline unsigned __stdcall PalUnifiedThreadAdapter(void* ctx)
{
    auto* c = static_cast<PalUnifiedThreadContext*>(ctx);
    auto proc = c->proc;
    auto param = c->param;
    delete c;
    proc(param);
    return 0;
}

inline PalThreadHandle PalCreateThreadUnified(PalUnifiedThreadProc proc, void* param)
{
    auto* ctx = new PalUnifiedThreadContext{proc, param};
    unsigned threadId = 0;
    auto h = reinterpret_cast<HANDLE>(_beginthreadex(nullptr, 0, PalUnifiedThreadAdapter, ctx, 0, &threadId));
    if (h == nullptr)
        delete ctx;
    return h;
}

inline void PalJoinThread(PalThreadHandle h)
{
    if (h != PAL_INVALID_THREAD)
    {
        WaitForSingleObject(h, INFINITE);
        CloseHandle(h);
    }
}

// ─── Multi-wait ───────────────────────────────────────────────────────────────
// FlushThreadProc needs to wait on shutdown + optional manual flush + timeout.
// On Windows, we use WaitForMultipleObjects directly in the caller.
// On macOS, we use condition variables. We abstract this with a "WaitSet".

struct PalWaitSet
{
    PalEventHandle shutdownEvent;
    PalEventHandle manualFlushEvent; // may be PAL_INVALID_EVENT
    DWORD intervalMs;
};

enum class PalWaitResult
{
    Shutdown,
    Timeout,
    ManualFlush,
    Error
};

inline PalWaitResult PalWaitMultiple(const PalWaitSet& ws)
{
    HANDLE events[2] = {ws.shutdownEvent, nullptr};
    DWORD count = 1;
    if (ws.manualFlushEvent != PAL_INVALID_EVENT)
    {
        events[count] = ws.manualFlushEvent;
        count++;
    }

    DWORD result = WaitForMultipleObjects(count, events, FALSE, ws.intervalMs);
    if (result == WAIT_OBJECT_0)
        return PalWaitResult::Shutdown;
    if (result == WAIT_TIMEOUT)
        return PalWaitResult::Timeout;
    if (result > WAIT_OBJECT_0 && result < WAIT_OBJECT_0 + count)
        return PalWaitResult::ManualFlush;
    return PalWaitResult::Error;
}

#else // !_WIN32 (macOS / POSIX)

#include <pthread.h>
#include <semaphore.h>
#include <cstdint>
#include <cstring>
#include <cerrno>
#include <ctime>
#include <fcntl.h>
#include <new>

#include "pal.h"

// ─── TLS ──────────────────────────────────────────────────────────────────────
typedef pthread_key_t PalTlsIndex;
#define PAL_TLS_INVALID ((pthread_key_t) - 1)

inline PalTlsIndex PalTlsAlloc()
{
    pthread_key_t key;
    if (pthread_key_create(&key, nullptr) != 0)
        return PAL_TLS_INVALID;
    return key;
}
inline void PalTlsFree(PalTlsIndex index)
{
    if (index != PAL_TLS_INVALID)
        pthread_key_delete(index);
}
inline void* PalTlsGetValue(PalTlsIndex index) { return pthread_getspecific(index); }
inline void PalTlsSetValue(PalTlsIndex index, void* value) { pthread_setspecific(index, value); }

// ─── Events ───────────────────────────────────────────────────────────────────
// macOS: condition variable + mutex + bool flag

struct PalEventImpl
{
    pthread_mutex_t mutex;
    pthread_cond_t cond;
    bool signaled;
    bool manualReset;
};

typedef PalEventImpl* PalEventHandle;
#define PAL_INVALID_EVENT nullptr

inline PalEventHandle PalCreateEvent(bool manualReset, bool initialState)
{
    auto* ev = new (std::nothrow) PalEventImpl();
    if (ev == nullptr)
        return PAL_INVALID_EVENT;

    pthread_mutex_init(&ev->mutex, nullptr);
    pthread_cond_init(&ev->cond, nullptr);
    ev->signaled = initialState;
    ev->manualReset = manualReset;
    return ev;
}

inline void PalSetEvent(PalEventHandle h)
{
    if (h == PAL_INVALID_EVENT)
        return;
    pthread_mutex_lock(&h->mutex);
    h->signaled = true;
    if (h->manualReset)
        pthread_cond_broadcast(&h->cond);
    else
        pthread_cond_signal(&h->cond);
    pthread_mutex_unlock(&h->mutex);
}

inline void PalCloseEvent(PalEventHandle h)
{
    if (h == PAL_INVALID_EVENT)
        return;
    pthread_mutex_destroy(&h->mutex);
    pthread_cond_destroy(&h->cond);
    delete h;
}

inline bool PalWaitEvent(PalEventHandle h, DWORD timeoutMs)
{
    if (h == PAL_INVALID_EVENT)
        return false;

    pthread_mutex_lock(&h->mutex);
    if (timeoutMs == INFINITE)
    {
        while (!h->signaled)
            pthread_cond_wait(&h->cond, &h->mutex);
    }
    else
    {
        struct timespec ts;
        clock_gettime(CLOCK_REALTIME, &ts);
        ts.tv_sec += timeoutMs / 1000;
        ts.tv_nsec += (timeoutMs % 1000) * 1000000L;
        if (ts.tv_nsec >= 1000000000L)
        {
            ts.tv_sec++;
            ts.tv_nsec -= 1000000000L;
        }
        while (!h->signaled)
        {
            if (pthread_cond_timedwait(&h->cond, &h->mutex, &ts) == ETIMEDOUT)
                break;
        }
    }
    bool was = h->signaled;
    if (!h->manualReset)
        h->signaled = false;
    pthread_mutex_unlock(&h->mutex);
    return was;
}

// ─── Named semaphore ──────────────────────────────────────────────────────────
// macOS: POSIX named semaphore (sem_open)

typedef sem_t* PalNamedSemaphore;
#define PAL_INVALID_SEMAPHORE SEM_FAILED

inline PalNamedSemaphore PalCreateNamedSemaphore(const char* name)
{
    // macOS named semaphores must start with '/'
    char semName[128];
    if (name[0] != '/')
    {
        semName[0] = '/';
        strncpy(semName + 1, name, sizeof(semName) - 2);
        semName[sizeof(semName) - 1] = '\0';
    }
    else
    {
        strncpy(semName, name, sizeof(semName) - 1);
        semName[sizeof(semName) - 1] = '\0';
    }
    return sem_open(semName, O_CREAT, 0644, 0);
}

inline void PalSignalNamedSemaphore(PalNamedSemaphore h)
{
    if (h != PAL_INVALID_SEMAPHORE)
        sem_post(h);
}

inline bool PalTryWaitNamedSemaphore(PalNamedSemaphore h)
{
    if (h == PAL_INVALID_SEMAPHORE)
        return false;
    return sem_trywait(h) == 0;
}

inline void PalCloseNamedSemaphore(PalNamedSemaphore h)
{
    if (h != PAL_INVALID_SEMAPHORE)
        sem_close(h);
}

// ─── Threads ──────────────────────────────────────────────────────────────────

typedef pthread_t PalThreadHandle;
#define PAL_INVALID_THREAD ((pthread_t)0)

typedef void* (*PalThreadProcPosix)(void*);

inline PalThreadHandle PalCreateThreadPosix(PalThreadProcPosix proc, void* param)
{
    pthread_t thread;
    if (pthread_create(&thread, nullptr, proc, param) != 0)
        return PAL_INVALID_THREAD;
    return thread;
}

// Unified thread creation: same signature on both platforms — void*(*)(void*)
typedef void* (*PalUnifiedThreadProc)(void*);

inline PalThreadHandle PalCreateThreadUnified(PalUnifiedThreadProc proc, void* param)
{
    return PalCreateThreadPosix(proc, param);
}

inline void PalJoinThread(PalThreadHandle h)
{
    if (h != PAL_INVALID_THREAD)
        pthread_join(h, nullptr);
}

// ─── Multi-wait ───────────────────────────────────────────────────────────────

struct PalWaitSet
{
    PalEventHandle shutdownEvent;
    PalEventHandle manualFlushEvent; // may be PAL_INVALID_EVENT
    DWORD intervalMs;
};

enum class PalWaitResult
{
    Shutdown,
    Timeout,
    ManualFlush,
    Error
};

inline PalWaitResult PalWaitMultiple(const PalWaitSet& ws)
{
    // On macOS, we implement multi-wait by polling both events with a timeout.
    // This is a simplification — for the flush thread use case, a short poll is fine.

    // Use the shutdown event's condition variable with a timed wait
    PalEventHandle shutEv = ws.shutdownEvent;
    PalEventHandle manualEv = ws.manualFlushEvent;
    DWORD remaining = ws.intervalMs;
    constexpr DWORD pollMs = 100; // 100ms poll granularity

    while (remaining > 0 || ws.intervalMs == INFINITE)
    {
        // Check shutdown
        pthread_mutex_lock(&shutEv->mutex);
        bool shutdown = shutEv->signaled;
        pthread_mutex_unlock(&shutEv->mutex);
        if (shutdown)
            return PalWaitResult::Shutdown;

        // Check manual flush
        if (manualEv != PAL_INVALID_EVENT)
        {
            pthread_mutex_lock(&manualEv->mutex);
            bool manual = manualEv->signaled;
            if (manual)
                manualEv->signaled = false; // auto-reset
            pthread_mutex_unlock(&manualEv->mutex);
            if (manual)
                return PalWaitResult::ManualFlush;
        }

        // Wait on shutdown event with a short timeout
        DWORD waitTime = (ws.intervalMs == INFINITE) ? pollMs : (remaining < pollMs ? remaining : pollMs);

        pthread_mutex_lock(&shutEv->mutex);
        if (!shutEv->signaled)
        {
            struct timespec ts;
            clock_gettime(CLOCK_REALTIME, &ts);
            ts.tv_sec += waitTime / 1000;
            ts.tv_nsec += (waitTime % 1000) * 1000000L;
            if (ts.tv_nsec >= 1000000000L)
            {
                ts.tv_sec++;
                ts.tv_nsec -= 1000000000L;
            }
            pthread_cond_timedwait(&shutEv->cond, &shutEv->mutex, &ts);
        }
        bool shutdownAfterWait = shutEv->signaled;
        pthread_mutex_unlock(&shutEv->mutex);

        if (shutdownAfterWait)
            return PalWaitResult::Shutdown;

        if (ws.intervalMs != INFINITE)
        {
            if (remaining <= waitTime)
                return PalWaitResult::Timeout;
            remaining -= waitTime;
        }
    }

    return PalWaitResult::Timeout;
}

#endif // !_WIN32
