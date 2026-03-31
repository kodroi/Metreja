#include "Profiler.h"
#include "Guids.h"
#include "ProfilerContext.h"
#include "MethodCache.h"
#include "CallStack.h"
#include "NdjsonWriter.h"
#include "StatsAggregator.h"
#include "platform/pal_io.h"
#include "platform/pal_threading.h"

// Global context pointer
ProfilerContext* g_ctx = nullptr;

MetrejaProfiler::MetrejaProfiler()
    : m_refCount(1)
    , m_profilerInfo(nullptr)
    , m_profilerInfo5(nullptr)
    , m_profilerInfo12(nullptr)
{
}

MetrejaProfiler::~MetrejaProfiler()
{
    // m_profilerInfo is released in Shutdown()
}

// IUnknown

HRESULT STDMETHODCALLTYPE MetrejaProfiler::QueryInterface(REFIID riid, void** ppvObject)
{
    if (ppvObject == nullptr)
        return E_POINTER;

    if (riid == IID_IUnknown || riid == IID_ICorProfilerCallback || riid == IID_ICorProfilerCallback2 ||
        riid == IID_ICorProfilerCallback3 || riid == IID_ICorProfilerCallback4 || riid == IID_ICorProfilerCallback5 ||
        riid == IID_ICorProfilerCallback6 || riid == IID_ICorProfilerCallback7 || riid == IID_ICorProfilerCallback8 ||
        riid == IID_ICorProfilerCallback9 || riid == IID_ICorProfilerCallback10)
    {
        *ppvObject = static_cast<ICorProfilerCallback10*>(this);
        AddRef();
        return S_OK;
    }

    *ppvObject = nullptr;
    return E_NOINTERFACE;
}

ULONG STDMETHODCALLTYPE MetrejaProfiler::AddRef() { return m_refCount.fetch_add(1) + 1; }

ULONG STDMETHODCALLTYPE MetrejaProfiler::Release()
{
    LONG count = m_refCount.fetch_sub(1) - 1;
    if (count == 0)
        delete this;
    return count;
}

// ICorProfilerCallback - Active methods

HRESULT STDMETHODCALLTYPE MetrejaProfiler::Initialize(IUnknown* pICorProfilerInfoUnk)
{
    // QI for ICorProfilerInfo3
    HRESULT hr = pICorProfilerInfoUnk->QueryInterface(IID_ICorProfilerInfo3, reinterpret_cast<void**>(&m_profilerInfo));
    if (FAILED(hr))
        return E_FAIL;

    // Try QI for ICorProfilerInfo5 (optional — needed for SetEventMask2)
    pICorProfilerInfoUnk->QueryInterface(IID_ICorProfilerInfo5, reinterpret_cast<void**>(&m_profilerInfo5));
    // m_profilerInfo5 may be null on older runtimes — that's OK

    // Try QI for ICorProfilerInfo12 (optional — needed for EventPipe)
    pICorProfilerInfoUnk->QueryInterface(IID_ICorProfilerInfo12, reinterpret_cast<void**>(&m_profilerInfo12));

    // Build context
    auto ctx = std::make_unique<ProfilerContext>();
    ctx->config = ConfigReader::Load();
    ctx->sessionId = ctx->config.sessionId;

    // Init subsystems
    CallStackManager::InitFrequency();
    DWORD pid = PalGetCurrentProcessId();
    ctx->callStackManager = std::make_unique<CallStackManager>();
    ctx->methodCache = std::make_unique<MethodCache>(m_profilerInfo, ctx->config);
    ctx->ndjsonWriter =
        std::make_unique<NdjsonWriter>(ctx->config.outputPath, ctx->config.maxEvents, ctx->sessionId, pid);

    // Write session_metadata event
    long long tsNs = CallStackManager::GetTimestampNs();
    ctx->ndjsonWriter->WriteSessionMetadata(ctx->config.scenario, tsNs);

    // Allocate StatsAggregator if stats events are enabled
    EventType events = ctx->config.enabledEvents;
    if (HasEvent(events, EventType::MethodStats) || HasEvent(events, EventType::ExceptionStats))
        ctx->statsAggregator = std::make_unique<StatsAggregator>();

    // Publish atomically — callbacks can now proceed
    g_ctx = ctx.release();

    hr = SetupEventMonitoring(events);
    if (FAILED(hr))
        return hr;

    // Create named semaphore for manual flush
    // Placed after all fallible setup so resources aren't leaked on early return.
    if (g_ctx->statsAggregator)
    {
        char semName[64];
        snprintf(semName, sizeof(semName), "MetrejaFlush_%lu", static_cast<unsigned long>(pid));
        g_ctx->m_manualFlushEvent = PalCreateNamedSemaphore(semName);
    }

    // Start flush thread if stats are enabled (handles periodic and/or manual flush)
    if (g_ctx->statsAggregator)
    {
        g_ctx->statsAggregator->StartPeriodicFlush(g_ctx->config.statsFlushIntervalSeconds, g_ctx->ndjsonWriter.get(),
                                                   g_ctx->methodCache.get(), g_ctx->m_manualFlushEvent);
    }

    return S_OK;
}

HRESULT STDMETHODCALLTYPE MetrejaProfiler::Shutdown()
{
    // Stop callbacks first
    ProfilerContext* ctx = g_ctx;
    g_ctx = nullptr;

    // Stop EventPipe session before tearing down context
    if (m_profilerInfo12 != nullptr && ctx != nullptr && ctx->eventPipeSession != 0)
    {
        m_profilerInfo12->EventPipeStopSession(ctx->eventPipeSession);
    }

    // Stop flush thread, then do final flush of remaining stats
    if (ctx != nullptr)
    {
        if (ctx->statsAggregator)
            ctx->statsAggregator->StopPeriodicFlush();
        if (ctx->statsAggregator && ctx->ndjsonWriter && ctx->methodCache)
            ctx->statsAggregator->Flush(*ctx->ndjsonWriter, *ctx->methodCache);
        if (ctx->ndjsonWriter)
            ctx->ndjsonWriter->Flush();
        if (ctx->m_manualFlushEvent != PAL_INVALID_SEMAPHORE)
            PalCloseNamedSemaphore(ctx->m_manualFlushEvent);
        delete ctx;
    }

    if (m_profilerInfo12 != nullptr)
    {
        m_profilerInfo12->Release();
        m_profilerInfo12 = nullptr;
    }

    if (m_profilerInfo5 != nullptr)
    {
        m_profilerInfo5->Release();
        m_profilerInfo5 = nullptr;
    }

    if (m_profilerInfo != nullptr)
    {
        m_profilerInfo->Release();
        m_profilerInfo = nullptr;
    }

    return S_OK;
}

HRESULT MetrejaProfiler::SetupEventMonitoring(EventType events)
{
    // Build event mask dynamically based on enabled event types
    DWORD eventMask = COR_PRF_MONITOR_JIT_COMPILATION;

    // ELT hooks needed whenever we require call-stack context
    bool needElt = HasEvent(events, EventType::Enter) || HasEvent(events, EventType::Leave) ||
                   HasEvent(events, EventType::MethodStats) || HasEvent(events, EventType::ExceptionStats) ||
                   HasEvent(events, EventType::Exception) || HasEvent(events, EventType::AllocByClass);
    if (needElt)
    {
        eventMask |= COR_PRF_MONITOR_ENTERLEAVE | COR_PRF_ENABLE_FRAME_INFO;
    }

    // Exception monitoring needed for exception/exception_stats
    if (HasEvent(events, EventType::Exception) || HasEvent(events, EventType::ExceptionStats))
        eventMask |= COR_PRF_MONITOR_EXCEPTIONS;

    // GC monitoring: use COR_PRF_HIGH_BASIC_GC to avoid disabling concurrent GC.
    // AllocByClass requires the full COR_PRF_MONITOR_GC (which does disable concurrent GC).
    DWORD highFlags = 0;
    bool needGcCallbacks = HasEvent(events, EventType::GcStart) || HasEvent(events, EventType::GcEnd);
    bool needGcHeapStats = HasEvent(events, EventType::GcHeapStats) && m_profilerInfo12 != nullptr;
    bool needAllocByClass = HasEvent(events, EventType::AllocByClass);

    if (needAllocByClass || (needGcCallbacks && m_profilerInfo5 == nullptr))
    {
        // COR_PRF_MONITOR_GC: required for AllocByClass, or as fallback on old runtimes.
        // Note: this disables concurrent GC.
        eventMask |= COR_PRF_MONITOR_GC;
    }
    else if (needGcCallbacks)
    {
        // COR_PRF_HIGH_BASIC_GC: GC callbacks without disabling concurrent GC
        highFlags |= COR_PRF_HIGH_BASIC_GC;
    }

    // Build EventPipe keyword mask for combined subscription
    UINT64 epKeywords = 0;
    bool needEventPipe = false;

    if (HasEvent(events, EventType::ContentionStart) || HasEvent(events, EventType::ContentionEnd))
    {
        epKeywords |= 0x4000; // ContentionKeyword
        needEventPipe = true;
    }
    if (needGcHeapStats)
    {
        epKeywords |= 0x1; // GCKeyword
        needEventPipe = true;
    }

    if (m_profilerInfo12 != nullptr && needEventPipe)
        highFlags |= COR_PRF_HIGH_MONITOR_EVENT_PIPE;

    if (g_ctx->config.disableInlining)
        eventMask |= COR_PRF_DISABLE_INLINING;

    // Single consolidated SetEventMask call
    HRESULT hr;
    if (m_profilerInfo5 != nullptr && highFlags != 0)
    {
        hr = m_profilerInfo5->SetEventMask2(eventMask, highFlags);
    }
    else
    {
        hr = m_profilerInfo->SetEventMask(eventMask);
    }
    if (FAILED(hr))
        return hr;

    // Set ELT3 hooks when enter/leave/stats events are needed
    if (needElt)
    {
        hr = m_profilerInfo->SetEnterLeaveFunctionHooks3WithInfo(
            reinterpret_cast<FunctionEnter3WithInfo*>(EnterNaked),
            reinterpret_cast<FunctionLeave3WithInfo*>(LeaveNaked),
            reinterpret_cast<FunctionTailcall3WithInfo*>(TailcallNaked));
        if (FAILED(hr))
            return hr;

        // FunctionIDMapper2 filters excluded functions at JIT time (only useful with ELT hooks)
        hr = m_profilerInfo->SetFunctionIDMapper2(reinterpret_cast<FunctionIDMapper2*>(FunctionMapper), nullptr);
        if (FAILED(hr))
            return hr;
    }

    // Start EventPipe session with combined keywords (contention + GC)
    if (m_profilerInfo12 != nullptr && needEventPipe)
    {
        COR_PRF_EVENTPIPE_PROVIDER_CONFIG providerConfig;
        providerConfig.providerName = reinterpret_cast<const WCHAR*>(u"Microsoft-Windows-DotNETRuntime");
        providerConfig.keywords = epKeywords;
        providerConfig.loggingLevel = 4; // Informational
        providerConfig.filterData = nullptr;

        EVENTPIPE_SESSION session = 0;
        HRESULT epHr = m_profilerInfo12->EventPipeStartSession(1, &providerConfig, false, &session);
        if (SUCCEEDED(epHr))
            g_ctx->eventPipeSession = session;
    }

    return S_OK;
}

HRESULT STDMETHODCALLTYPE MetrejaProfiler::JITCompilationStarted(FunctionID functionId, BOOL fIsSafeToBlock)
{
    auto* ctx = g_ctx;
    if (ctx == nullptr)
        return S_OK;

    ctx->methodCache->ResolveAndCache(functionId);
    return S_OK;
}

HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionThrown(ObjectID thrownObjectId)
{
    auto* ctx = g_ctx;
    if (ctx == nullptr || m_profilerInfo == nullptr)
        return S_OK;

    bool wantException = HasEvent(ctx->config.enabledEvents, EventType::Exception);
    bool wantExceptionStats = HasEvent(ctx->config.enabledEvents, EventType::ExceptionStats);
    if (!wantException && !wantExceptionStats)
        return S_OK;

    // Get exception class type name
    ClassID classId = 0;
    HRESULT hr = m_profilerInfo->GetClassFromObject(thrownObjectId, &classId);
    if (FAILED(hr))
        return S_OK;

    std::string exTypeName = ctx->methodCache->ResolveClassName(classId);

    // Try to get method info from top of call stack
    long long tsNs = CallStackManager::GetTimestampNs();
    DWORD tid = PalGetCurrentThreadId();

    MethodInfo exInfo{};
    auto* threadStack = ctx->callStackManager->GetThreadStack();
    if (threadStack != nullptr && !threadStack->stack.empty())
    {
        FunctionID topFunc = static_cast<FunctionID>(threadStack->stack.back().functionId);
        const MethodInfo* topInfo = ctx->methodCache->Lookup(topFunc);
        if (topInfo != nullptr)
            exInfo = *topInfo;
    }

    if (wantException)
        ctx->ndjsonWriter->WriteException(tsNs, tid, exInfo, exTypeName);

    if (wantExceptionStats && ctx->statsAggregator)
        ctx->statsAggregator->RecordException(exInfo, exTypeName);

    return S_OK;
}

// ICorProfilerCallback - No-op stubs

HRESULT STDMETHODCALLTYPE MetrejaProfiler::AppDomainCreationStarted(AppDomainID appDomainId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::AppDomainCreationFinished(AppDomainID appDomainId, HRESULT hrStatus)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::AppDomainShutdownStarted(AppDomainID appDomainId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::AppDomainShutdownFinished(AppDomainID appDomainId, HRESULT hrStatus)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::AssemblyLoadStarted(AssemblyID assemblyId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::AssemblyLoadFinished(AssemblyID assemblyId, HRESULT hrStatus)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::AssemblyUnloadStarted(AssemblyID assemblyId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::AssemblyUnloadFinished(AssemblyID assemblyId, HRESULT hrStatus)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ModuleLoadStarted(ModuleID moduleId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ModuleLoadFinished(ModuleID moduleId, HRESULT hrStatus) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ModuleUnloadStarted(ModuleID moduleId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ModuleUnloadFinished(ModuleID moduleId, HRESULT hrStatus) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ModuleAttachedToAssembly(ModuleID moduleId, AssemblyID assemblyId)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ClassLoadStarted(ClassID classId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ClassLoadFinished(ClassID classId, HRESULT hrStatus) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ClassUnloadStarted(ClassID classId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ClassUnloadFinished(ClassID classId, HRESULT hrStatus) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::FunctionUnloadStarted(FunctionID functionId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::JITCompilationFinished(FunctionID functionId, HRESULT hrStatus,
                                                                  BOOL fIsSafeToBlock)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::JITCachedFunctionSearchStarted(FunctionID functionId,
                                                                          BOOL* pbUseCachedFunction)
{
    auto* ctx = g_ctx;
    if (ctx != nullptr)
        ctx->methodCache->ResolveAndCache(functionId);
    if (pbUseCachedFunction != nullptr)
        *pbUseCachedFunction = TRUE;
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::JITCachedFunctionSearchFinished(FunctionID functionId,
                                                                           COR_PRF_JIT_CACHE result)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::JITFunctionPitched(FunctionID functionId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::JITInlining(FunctionID callerId, FunctionID calleeId, BOOL* pfShouldInline)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ThreadCreated(ThreadID threadId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ThreadDestroyed(ThreadID threadId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ThreadAssignedToOSThread(ThreadID managedThreadId, DWORD osThreadId)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RemotingClientInvocationStarted() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RemotingClientSendingMessage(GUID* pCookie, BOOL fIsAsync) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RemotingClientReceivingReply(GUID* pCookie, BOOL fIsAsync) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RemotingClientInvocationFinished() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RemotingServerReceivingMessage(GUID* pCookie, BOOL fIsAsync) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RemotingServerInvocationStarted() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RemotingServerInvocationReturned() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RemotingServerSendingReply(GUID* pCookie, BOOL fIsAsync) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::UnmanagedToManagedTransition(FunctionID functionId,
                                                                        COR_PRF_TRANSITION_REASON reason)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ManagedToUnmanagedTransition(FunctionID functionId,
                                                                        COR_PRF_TRANSITION_REASON reason)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RuntimeSuspendStarted(COR_PRF_SUSPEND_REASON suspendReason) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RuntimeSuspendFinished() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RuntimeSuspendAborted() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RuntimeResumeStarted() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RuntimeResumeFinished() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RuntimeThreadSuspended(ThreadID threadId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RuntimeThreadResumed(ThreadID threadId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::MovedReferences(ULONG cMovedObjectIDRanges, ObjectID oldObjectIDRangeStart[],
                                                           ObjectID newObjectIDRangeStart[],
                                                           ULONG cObjectIDRangeLength[])
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ObjectAllocated(ObjectID objectId, ClassID classId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ObjectsAllocatedByClass(ULONG cClassCount, ClassID classIds[],
                                                                   ULONG cObjects[])
{
    auto* ctx = g_ctx;
    if (ctx == nullptr || !HasEvent(ctx->config.enabledEvents, EventType::AllocByClass))
        return S_OK;

    long long tsNs = CallStackManager::GetTimestampNs();
    DWORD tid = PalGetCurrentThreadId();

    // Try to get current method from top of call stack for call-site attribution
    MethodInfo allocMethod{};
    bool hasCallSite = false;
    auto* threadStack = ctx->callStackManager->GetThreadStack();
    if (threadStack != nullptr && !threadStack->stack.empty())
    {
        FunctionID topFunc = static_cast<FunctionID>(threadStack->stack.back().functionId);
        const MethodInfo* topInfo = ctx->methodCache->Lookup(topFunc);
        if (topInfo != nullptr)
        {
            allocMethod = *topInfo;
            hasCallSite = true;
        }
    }

    for (ULONG i = 0; i < cClassCount; i++)
    {
        if (classIds[i] == 0 || cObjects[i] == 0)
            continue;

        std::string className = ctx->methodCache->ResolveClassName(classIds[i]);

        if (hasCallSite)
            ctx->ndjsonWriter->WriteAllocByClassDetailed(tsNs, tid, className, cObjects[i], allocMethod);
        else
            ctx->ndjsonWriter->WriteAllocByClass(tsNs, tid, className, cObjects[i]);
    }

    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ObjectReferences(ObjectID objectId, ClassID classId, ULONG cObjectRefs,
                                                            ObjectID objectRefIds[])
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RootReferences(ULONG cRootRefs, ObjectID rootRefIds[]) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionSearchFunctionEnter(FunctionID functionId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionSearchFunctionLeave() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionSearchFilterEnter(FunctionID functionId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionSearchFilterLeave() { return S_OK; }

// Decrement async nesting count for the given functionId. When the count
// reaches zero the entry is removed and wall-time (tsNs minus the first
// enter timestamp) is returned. Returns 0 when nesting is still active or
// when the functionId has no async tracking entry.
static inline long long DecrementAsyncNesting(ThreadCallStack* ts, UINT_PTR functionId, long long tsNs)
{
    long long wallTimeNs = 0;
    auto it = ts->m_asyncNestingCount.find(functionId);
    if (it != ts->m_asyncNestingCount.end())
    {
        it->second--;
        if (it->second <= 0)
        {
            auto firstIt = ts->m_asyncFirstEnterNs.find(functionId);
            if (firstIt != ts->m_asyncFirstEnterNs.end())
            {
                wallTimeNs = tsNs - firstIt->second;
                ts->m_asyncFirstEnterNs.erase(firstIt);
            }
            ts->m_asyncNestingCount.erase(it);
        }
    }
    return wallTimeNs;
}

// Finalize a deferred unwind entry that turned out NOT to be the catcher
// (an inner recursive activation with the same FunctionID as the catcher).
static void FinalizeDeferredUnwind(ProfilerContext* ctx, ThreadCallStack* ts)
{
    CallEntry& entry = ts->m_deferredUnwindEntry;
    long long inclusiveNs = (entry.enterTsNs > 0) ? (ts->m_deferredUnwindTsNs - entry.enterTsNs) : 0;
    long long selfNs = inclusiveNs - entry.childrenTimeNs;
    if (selfNs < 0)
        selfNs = 0;
    ctx->callStackManager->CreditParent(inclusiveNs);
    int depth = ctx->callStackManager->GetDepth();

    // Clean up async wall-time tracking for finalized deferred entries
    const MethodInfo* deferredInfo = ctx->methodCache->Lookup(static_cast<FunctionID>(ts->m_deferredUnwindFunctionId));
    if (deferredInfo != nullptr && deferredInfo->isAsyncStateMachine)
        DecrementAsyncNesting(ts, ts->m_deferredUnwindFunctionId, ts->m_deferredUnwindTsNs);

    if (ctx->statsAggregator && HasEvent(ctx->config.enabledEvents, EventType::MethodStats))
        ctx->statsAggregator->RecordMethod(static_cast<FunctionID>(ts->m_deferredUnwindFunctionId), inclusiveNs,
                                           selfNs);

    if (HasEvent(ctx->config.enabledEvents, EventType::Leave))
    {
        if (deferredInfo != nullptr)
        {
            long long deltaNs = ctx->config.computeDeltas ? inclusiveNs : 0;
            ctx->ndjsonWriter->WriteLeave(ts->m_deferredUnwindTsNs, PalGetCurrentThreadId(), depth, *deferredInfo,
                                          deltaNs);
        }
    }

    ts->m_hasDeferredUnwind = false;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionSearchCatcherFound(FunctionID functionId)
{
    auto* ctx = g_ctx;
    if (ctx == nullptr)
        return S_OK;

    auto* threadStack = ctx->callStackManager->GetThreadStack();
    if (threadStack != nullptr)
    {
        // If there's a stale deferred entry from a previous exception, finalize it
        if (threadStack->m_hasDeferredUnwind)
            FinalizeDeferredUnwind(ctx, threadStack);

        threadStack->exceptionCatcherFunctionId = functionId;
    }

    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionOSHandlerEnter(UINT_PTR) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionOSHandlerLeave(UINT_PTR) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionUnwindFunctionEnter(FunctionID functionId)
{
    auto* ctx = g_ctx;
    if (ctx == nullptr)
        return S_OK;

    const MethodInfo* info = ctx->methodCache->Lookup(functionId);
    if (info == nullptr || !info->isIncluded)
    {
        // Don't pop for excluded methods — they were never pushed in EnterStub
        return S_OK;
    }

    auto* threadStack = ctx->callStackManager->GetThreadStack();

    if (threadStack != nullptr && threadStack->exceptionCatcherFunctionId == static_cast<UINT_PTR>(functionId))
    {
        // This frame's FunctionID matches the catcher. It might be the catcher
        // itself, or an inner recursive activation of the same method.
        // Defer the pop: if another matching frame comes later, finalize this one
        // (it was an inner activation). The LAST deferred entry is the actual
        // catcher, restored in ExceptionCatcherEnter.

        if (threadStack->m_hasDeferredUnwind)
        {
            // Previous deferred entry was NOT the catcher — finalize it now
            FinalizeDeferredUnwind(ctx, threadStack);
        }

        // Pop and defer this entry
        CallEntry entry = ctx->callStackManager->Pop();
        long long tsNs = CallStackManager::GetTimestampNs();

        // Clean up async wall-time tracking for unwound methods
        if (info != nullptr && info->isAsyncStateMachine && threadStack != nullptr)
            DecrementAsyncNesting(threadStack, functionId, tsNs);

        threadStack->m_deferredUnwindEntry = entry;
        threadStack->m_deferredUnwindFunctionId = functionId;
        threadStack->m_deferredUnwindTsNs = tsNs;
        threadStack->m_hasDeferredUnwind = true;
        return S_OK;
    }

    // Not the catcher's FunctionID — pop and process normally
    CallEntry entry = ctx->callStackManager->Pop();
    long long tsNs = CallStackManager::GetTimestampNs();

    // Clean up async wall-time tracking for unwound methods
    if (info != nullptr && info->isAsyncStateMachine && threadStack != nullptr)
        DecrementAsyncNesting(threadStack, functionId, tsNs);

    long long inclusiveNs = (entry.enterTsNs > 0) ? (tsNs - entry.enterTsNs) : 0;
    long long selfNs = inclusiveNs - entry.childrenTimeNs;
    if (selfNs < 0)
        selfNs = 0;
    ctx->callStackManager->CreditParent(inclusiveNs);
    int depth = ctx->callStackManager->GetDepth();

    if (ctx->statsAggregator && HasEvent(ctx->config.enabledEvents, EventType::MethodStats))
        ctx->statsAggregator->RecordMethod(static_cast<FunctionID>(entry.functionId), inclusiveNs, selfNs);

    if (HasEvent(ctx->config.enabledEvents, EventType::Leave))
    {
        long long deltaNs = ctx->config.computeDeltas ? inclusiveNs : 0;
        ctx->ndjsonWriter->WriteLeave(tsNs, PalGetCurrentThreadId(), depth, *info, deltaNs);
    }
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionUnwindFunctionLeave() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionUnwindFinallyEnter(FunctionID functionId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionUnwindFinallyLeave() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionCatcherEnter(FunctionID functionId, ObjectID objectId)
{
    auto* ctx = g_ctx;
    if (ctx == nullptr)
        return S_OK;

    auto* threadStack = ctx->callStackManager->GetThreadStack();
    if (threadStack != nullptr)
    {
        if (threadStack->m_hasDeferredUnwind)
        {
            // The deferred entry IS the catcher — restore it to the stack.
            // It will exit normally via LeaveStub when the method returns.
            threadStack->stack.push_back(threadStack->m_deferredUnwindEntry);
            threadStack->m_hasDeferredUnwind = false;
        }
        threadStack->exceptionCatcherFunctionId = 0;
    }

    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionCatcherLeave() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::COMClassicVTableCreated(ClassID wrappedClassId, REFGUID implementedIID,
                                                                   void* pVTable, ULONG cSlots)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::COMClassicVTableDestroyed(ClassID wrappedClassId, REFGUID implementedIID,
                                                                     void* pVTable)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionCLRCatcherFound() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionCLRCatcherExecute() { return S_OK; }

// ICorProfilerCallback2 - No-op stubs
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ThreadNameChanged(ThreadID threadId, ULONG cchName, WCHAR name[])
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::GarbageCollectionStarted(int cGenerations, BOOL generationCollected[],
                                                                    COR_PRF_GC_REASON reason)
{
    auto* ctx = g_ctx;
    if (ctx == nullptr)
        return S_OK;

    // Always record start timestamp so GarbageCollectionFinished can compute durationNs
    // even when gc_start event is disabled but gc_end is enabled.
    long long startTsNs = CallStackManager::GetTimestampNs();
    ctx->gcStartNs.store(startTsNs, std::memory_order_relaxed);

    if (!HasEvent(ctx->config.enabledEvents, EventType::GcStart))
        return S_OK;

    bool gen0 = cGenerations > 0 && generationCollected[0];
    bool gen1 = cGenerations > 1 && generationCollected[1];
    bool gen2 = cGenerations > 2 && generationCollected[2];

    const char* reasonStr;
    switch (reason)
    {
    case COR_PRF_GC_INDUCED: reasonStr = "induced"; break;
    case COR_PRF_GC_OTHER: reasonStr = "other"; break;
    default: reasonStr = "unknown"; break;
    }

    ctx->ndjsonWriter->WriteGcStart(startTsNs, gen0, gen1, gen2, reasonStr);
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::SurvivingReferences(ULONG cSurvivingObjectIDRanges,
                                                               ObjectID objectIDRangeStart[],
                                                               ULONG cObjectIDRangeLength[])
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::GarbageCollectionFinished()
{
    auto* ctx = g_ctx;
    if (ctx == nullptr || !HasEvent(ctx->config.enabledEvents, EventType::GcEnd))
        return S_OK;

    long long nowTsNs = CallStackManager::GetTimestampNs();
    long long startTsNs = ctx->gcStartNs.load(std::memory_order_relaxed);
    long long durationNs = (startTsNs > 0) ? (nowTsNs - startTsNs) : 0;

    // Get heap size via GetGenerationBounds (available on ICorProfilerInfo2+)
    long long heapSizeBytes = 0;
    constexpr ULONG kMaxRanges = 16;
    COR_PRF_GC_GENERATION_RANGE ranges[kMaxRanges];
    ULONG rangeCount = 0;
    HRESULT hr = m_profilerInfo->GetGenerationBounds(kMaxRanges, &rangeCount, ranges);
    if (SUCCEEDED(hr))
    {
        // rangeCount is the total available; cap to buffer size to avoid overread
        ULONG count = rangeCount < kMaxRanges ? rangeCount : kMaxRanges;
        for (ULONG i = 0; i < count; i++)
            heapSizeBytes += static_cast<long long>(ranges[i].rangeLength);
    }

    ctx->ndjsonWriter->WriteGcEnd(nowTsNs, durationNs, heapSizeBytes);
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::FinalizeableObjectQueued(DWORD finalizerFlags, ObjectID objectID)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::RootReferences2(ULONG cRootRefs, ObjectID rootRefIds[],
                                                           COR_PRF_GC_ROOT_KIND rootKinds[],
                                                           COR_PRF_GC_ROOT_FLAGS rootFlags[], UINT_PTR rootIds[])
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::HandleCreated(GCHandleID handleId, ObjectID initialObjectId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::HandleDestroyed(GCHandleID handleId) { return S_OK; }

// ICorProfilerCallback3 - No-op stubs
HRESULT STDMETHODCALLTYPE MetrejaProfiler::InitializeForAttach(IUnknown* pCorProfilerInfoUnk, void* pvClientData,
                                                               UINT cbClientData)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ProfilerAttachComplete() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ProfilerDetachSucceeded() { return S_OK; }

// ICorProfilerCallback4 - No-op stubs
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ReJITCompilationStarted(FunctionID functionId, ReJITID rejitId,
                                                                   BOOL fIsSafeToBlock)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::GetReJITParameters(ModuleID moduleId, mdMethodDef methodId,
                                                              ICorProfilerFunctionControl* pFunctionControl)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ReJITCompilationFinished(FunctionID functionId, ReJITID rejitId,
                                                                    HRESULT hrStatus, BOOL fIsSafeToBlock)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ReJITError(ModuleID moduleId, mdMethodDef methodId, FunctionID functionId,
                                                      HRESULT hrStatus)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::MovedReferences2(ULONG cMovedObjectIDRanges,
                                                            ObjectID oldObjectIDRangeStart[],
                                                            ObjectID newObjectIDRangeStart[],
                                                            SIZE_T cObjectIDRangeLength[])
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::SurvivingReferences2(ULONG cSurvivingObjectIDRanges,
                                                                ObjectID objectIDRangeStart[],
                                                                SIZE_T cObjectIDRangeLength[])
{
    return S_OK;
}

// ICorProfilerCallback5 - No-op stub
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ConditionalWeakTableElementReferences(ULONG cRootRefs, ObjectID keyRefIds[],
                                                                                 ObjectID valueRefIds[],
                                                                                 GCHandleID rootIds[])
{
    return S_OK;
}

// ICorProfilerCallback6 - No-op stub
HRESULT STDMETHODCALLTYPE MetrejaProfiler::GetAssemblyReferences(const WCHAR* wszAssemblyPath,
                                                                 ICorProfilerAssemblyReferenceProvider* pAsmRefProvider)
{
    return S_OK;
}

// ICorProfilerCallback7 - No-op stub
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ModuleInMemorySymbolsUpdated(ModuleID moduleId) { return S_OK; }

// ICorProfilerCallback8 - No-op stubs
HRESULT STDMETHODCALLTYPE MetrejaProfiler::DynamicMethodJITCompilationStarted(FunctionID functionId,
                                                                              BOOL fIsSafeToBlock, LPCBYTE pILHeader,
                                                                              ULONG cbILHeader)
{
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::DynamicMethodJITCompilationFinished(FunctionID functionId, HRESULT hrStatus,
                                                                               BOOL fIsSafeToBlock)
{
    return S_OK;
}

// ICorProfilerCallback9 - No-op stub
HRESULT STDMETHODCALLTYPE MetrejaProfiler::DynamicMethodUnloaded(FunctionID functionId) { return S_OK; }

// ICorProfilerCallback10 - EventPipe delivery
HRESULT STDMETHODCALLTYPE MetrejaProfiler::EventPipeEventDelivered(EVENTPIPE_PROVIDER provider, DWORD eventId,
                                                                   DWORD eventVersion, ULONG cbMetadataBlob,
                                                                   LPCBYTE metadataBlob, ULONG cbEventData,
                                                                   LPCBYTE eventData, LPCGUID pActivityId,
                                                                   LPCGUID pRelatedActivityId, ThreadID eventThread,
                                                                   ULONG numStackFrames, UINT_PTR stackFrames[])
{
    auto* ctx = g_ctx;
    if (ctx == nullptr)
        return S_OK;

    long long tsNs = CallStackManager::GetTimestampNs();
    DWORD tid = PalGetCurrentThreadId();

    // Event ID 81 = ContentionStart, Event ID 91 = ContentionStop
    if (eventId == 81 && HasEvent(ctx->config.enabledEvents, EventType::ContentionStart))
    {
        ctx->ndjsonWriter->WriteContentionStart(tsNs, tid);
    }
    else if (eventId == 91 && HasEvent(ctx->config.enabledEvents, EventType::ContentionEnd))
    {
        // ContentionStop payload contains DurationNs as first 8 bytes
        long long durationNs = 0;
        if (cbEventData >= 8 && eventData != nullptr)
            memcpy(&durationNs, eventData, sizeof(long long));
        ctx->ndjsonWriter->WriteContentionEnd(tsNs, tid, durationNs);
    }
    // Event ID 4 = GCHeapStats (V1/V2)
    else if (eventId == 4 && HasEvent(ctx->config.enabledEvents, EventType::GcHeapStats))
    {
        // GCHeapStats payload: per-generation sizes and promoted bytes
        // Minimum V1 payload: 92 bytes (gen0-LOH + finalization + pinned + sink + handle counts)
        if (cbEventData >= 92 && eventData != nullptr)
        {
            uint64_t gen0Size = 0;
            uint64_t gen0Promoted = 0;
            uint64_t gen1Size = 0;
            uint64_t gen1Promoted = 0;
            uint64_t gen2Size = 0;
            uint64_t gen2Promoted = 0;
            uint64_t lohSize = 0;
            uint64_t lohPromoted = 0;
            uint64_t finalizationPromotedCount = 0;
            uint32_t pinnedObjectCount = 0;

            memcpy(&gen0Size, eventData + 0, 8);
            memcpy(&gen0Promoted, eventData + 8, 8);
            memcpy(&gen1Size, eventData + 16, 8);
            memcpy(&gen1Promoted, eventData + 24, 8);
            memcpy(&gen2Size, eventData + 32, 8);
            memcpy(&gen2Promoted, eventData + 40, 8);
            memcpy(&lohSize, eventData + 48, 8);
            memcpy(&lohPromoted, eventData + 56, 8);
            // Offset 64: FinalizationPromotedSize (8), Offset 72: FinalizationPromotedCount (8)
            memcpy(&finalizationPromotedCount, eventData + 72, 8);
            memcpy(&pinnedObjectCount, eventData + 80, 4);

            // V2 adds POH (Pinned Object Heap) after ClrInstanceID (uint16 at offset 92)
            uint64_t pohSize = 0;
            uint64_t pohPromoted = 0;
            if (cbEventData >= 110)
            {
                memcpy(&pohSize, eventData + 94, 8);
                memcpy(&pohPromoted, eventData + 102, 8);
            }

            ctx->ndjsonWriter->WriteGcHeapStats(tsNs, gen0Size, gen0Promoted, gen1Size, gen1Promoted, gen2Size,
                                                gen2Promoted, lohSize, lohPromoted, pohSize, pohPromoted,
                                                static_cast<long long>(finalizationPromotedCount),
                                                static_cast<int>(pinnedObjectCount));
        }
    }

    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::EventPipeProviderCreated(EVENTPIPE_PROVIDER provider) { return S_OK; }

// ELT3 C++ Stubs (called from MASM naked hooks)

// Common preamble: validates context, resolves method info, captures timing context.
// Returns false if the event should be skipped.
static inline bool PrepareStubContext(FunctionIDOrClientID functionIDOrClientID, ProfilerContext*& ctx,
                                      const MethodInfo*& info, FunctionID& funcId, long long& tsNs, DWORD& tid)
{
    ctx = g_ctx;
    if (ctx == nullptr)
        return false;

    funcId = functionIDOrClientID.functionID;
    info = ctx->methodCache->Lookup(funcId);
    if (info == nullptr || !info->isIncluded)
        return false;

    tsNs = CallStackManager::GetTimestampNs();
    tid = PalGetCurrentThreadId();
    return true;
}

extern "C" void STDMETHODCALLTYPE EnterStub(FunctionIDOrClientID functionIDOrClientID, COR_PRF_ELT_INFO eltInfo)
{
    ProfilerContext* ctx;
    const MethodInfo* info;
    FunctionID funcId;
    long long tsNs;
    DWORD tid;
    if (!PrepareStubContext(functionIDOrClientID, ctx, info, funcId, tsNs, tid))
        return;

    int depth = ctx->callStackManager->GetDepth();
    ctx->callStackManager->Push(funcId, tsNs);

    // Track async wall-time: record first enter timestamp per async method
    if (info->isAsyncStateMachine)
    {
        auto* threadStack = ctx->callStackManager->GetThreadStack();
        if (threadStack != nullptr)
        {
            auto& nestCount = threadStack->m_asyncNestingCount[funcId];
            nestCount++;
            if (nestCount == 1)
                threadStack->m_asyncFirstEnterNs[funcId] = tsNs;
        }
    }

    if (HasEvent(ctx->config.enabledEvents, EventType::Enter))
        ctx->ndjsonWriter->WriteEnter(tsNs, tid, depth, *info);
}

static void ProcessLeave(FunctionIDOrClientID functionIDOrClientID, bool isTailcall)
{
    ProfilerContext* ctx;
    const MethodInfo* info;
    FunctionID funcId;
    long long tsNs;
    DWORD tid;
    if (!PrepareStubContext(functionIDOrClientID, ctx, info, funcId, tsNs, tid))
        return;

    CallEntry entry = ctx->callStackManager->Pop();
    long long inclusiveNs = (entry.enterTsNs > 0) ? (tsNs - entry.enterTsNs) : 0;
    long long selfNs = inclusiveNs - entry.childrenTimeNs;
    if (selfNs < 0)
        selfNs = 0;
    ctx->callStackManager->CreditParent(inclusiveNs);
    int depth = ctx->callStackManager->GetDepth();

    // Compute async wall-time
    long long wallTimeNs = 0;
    if (info->isAsyncStateMachine)
    {
        auto* threadStack = ctx->callStackManager->GetThreadStack();
        if (threadStack != nullptr)
            wallTimeNs = DecrementAsyncNesting(threadStack, funcId, tsNs);
    }

    if (ctx->statsAggregator && HasEvent(ctx->config.enabledEvents, EventType::MethodStats))
        ctx->statsAggregator->RecordMethod(funcId, inclusiveNs, selfNs);

    if (HasEvent(ctx->config.enabledEvents, EventType::Leave))
    {
        long long deltaNs = ctx->config.computeDeltas ? inclusiveNs : 0;
        ctx->ndjsonWriter->WriteLeave(tsNs, tid, depth, *info, deltaNs, isTailcall, wallTimeNs);
    }
}

extern "C" void STDMETHODCALLTYPE LeaveStub(FunctionIDOrClientID functionIDOrClientID, COR_PRF_ELT_INFO eltInfo)
{
    ProcessLeave(functionIDOrClientID, false);
}

extern "C" void STDMETHODCALLTYPE TailcallStub(FunctionIDOrClientID functionIDOrClientID, COR_PRF_ELT_INFO eltInfo)
{
    ProcessLeave(functionIDOrClientID, true);
}

// FunctionIDMapper2 callback - filter excluded functions at JIT time
extern "C" UINT_PTR STDMETHODCALLTYPE FunctionMapper(FunctionID funcId, void* clientData, BOOL* pbHookFunction)
{
    auto* ctx = g_ctx;
    if (ctx == nullptr)
    {
        *pbHookFunction = FALSE;
        return funcId;
    }

    // ShouldHook resolves and caches the function, then checks filters
    *pbHookFunction = ctx->methodCache->ShouldHook(funcId) ? TRUE : FALSE;
    return funcId;
}
