#include "Profiler.h"
#include "Guids.h"
#include "ProfilerContext.h"
#include "MethodCache.h"
#include "CallStack.h"
#include "NdjsonWriter.h"
#include "StatsAggregator.h"

// Global context pointer
ProfilerContext* g_ctx = nullptr;

MetrejaProfiler::MetrejaProfiler()
    : m_refCount(1)
    , m_profilerInfo(nullptr)
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
        riid == IID_ICorProfilerCallback3)
    {
        *ppvObject = static_cast<ICorProfilerCallback3*>(this);
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

    // Build context
    auto ctx = std::make_unique<ProfilerContext>();
    ctx->config = ConfigReader::Load();
    ctx->sessionId = ctx->config.sessionId;

    // Init subsystems
    CallStackManager::InitFrequency();
    DWORD pid = GetCurrentProcessId();
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

    // Create named event for manual flush (auto-reset, initially non-signaled)
    if (g_ctx->statsAggregator)
    {
        wchar_t eventName[64];
        swprintf_s(eventName, L"MetrejaFlush_%lu", pid);
        g_ctx->m_manualFlushEvent = CreateEventW(nullptr, FALSE, FALSE, eventName);
    }

    // Start flush thread if stats are enabled (handles periodic and/or manual flush)
    if (g_ctx->statsAggregator)
    {
        g_ctx->statsAggregator->StartPeriodicFlush(g_ctx->config.statsFlushIntervalSeconds, g_ctx->ndjsonWriter.get(),
                                                   g_ctx->methodCache.get(), g_ctx->m_manualFlushEvent);
    }

    // Build event mask dynamically based on enabled event types
    DWORD eventMask = COR_PRF_MONITOR_JIT_COMPILATION;

    // ELT hooks needed whenever we require call-stack context
    bool needElt = HasEvent(events, EventType::Enter) || HasEvent(events, EventType::Leave) ||
                   HasEvent(events, EventType::MethodStats) || HasEvent(events, EventType::ExceptionStats) ||
                   HasEvent(events, EventType::Exception);
    if (needElt)
    {
        eventMask |= COR_PRF_MONITOR_ENTERLEAVE | COR_PRF_ENABLE_FRAME_INFO;
    }

    // Exception monitoring needed for exception/exception_stats
    if (HasEvent(events, EventType::Exception) || HasEvent(events, EventType::ExceptionStats))
        eventMask |= COR_PRF_MONITOR_EXCEPTIONS;

    // GC monitoring
    if (HasEvent(events, EventType::GcStart) || HasEvent(events, EventType::GcEnd) ||
        HasEvent(events, EventType::AllocByClass))
    {
        eventMask |= COR_PRF_MONITOR_GC;
    }

    if (g_ctx->config.disableInlining)
        eventMask |= COR_PRF_DISABLE_INLINING;
    hr = m_profilerInfo->SetEventMask(eventMask);
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

    return S_OK;
}

HRESULT STDMETHODCALLTYPE MetrejaProfiler::Shutdown()
{
    // Stop callbacks first
    ProfilerContext* ctx = g_ctx;
    g_ctx = nullptr;

    // Stop flush thread, then do final flush of remaining stats
    if (ctx != nullptr)
    {
        if (ctx->statsAggregator)
            ctx->statsAggregator->StopPeriodicFlush();
        if (ctx->statsAggregator && ctx->ndjsonWriter && ctx->methodCache)
            ctx->statsAggregator->Flush(*ctx->ndjsonWriter, *ctx->methodCache);
        if (ctx->ndjsonWriter)
            ctx->ndjsonWriter->Flush();
        if (ctx->m_manualFlushEvent != nullptr)
            CloseHandle(ctx->m_manualFlushEvent);
        delete ctx;
    }

    if (m_profilerInfo != nullptr)
    {
        m_profilerInfo->Release();
        m_profilerInfo = nullptr;
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
    DWORD tid = GetCurrentThreadId();

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
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ModuleAttachedToAssembly(ModuleID moduleId, AssemblyID AssemblyId)
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

    for (ULONG i = 0; i < cClassCount; i++)
    {
        if (classIds[i] == 0 || cObjects[i] == 0)
            continue;

        std::string className = ctx->methodCache->ResolveClassName(classIds[i]);
        DWORD tid = GetCurrentThreadId();
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

// Finalize a deferred unwind entry that turned out NOT to be the catcher
// (an inner recursive activation with the same FunctionID as the catcher).
static void FinalizeDeferredUnwind(ProfilerContext* ctx, ThreadCallStack* ts)
{
    CallEntry& entry = ts->m_deferredUnwindEntry;
    long long inclusiveNs = (entry.enterTsNs > 0) ? (ts->m_deferredUnwindTsNs - entry.enterTsNs) : 0;
    long long selfNs = inclusiveNs - entry.m_childrenTimeNs;
    if (selfNs < 0)
        selfNs = 0;
    ctx->callStackManager->CreditParent(inclusiveNs);
    int depth = ctx->callStackManager->GetDepth();

    if (ctx->statsAggregator && HasEvent(ctx->config.enabledEvents, EventType::MethodStats))
        ctx->statsAggregator->RecordMethod(static_cast<FunctionID>(ts->m_deferredUnwindFunctionId), inclusiveNs,
                                           selfNs);

    if (HasEvent(ctx->config.enabledEvents, EventType::Leave))
    {
        const MethodInfo* info = ctx->methodCache->Lookup(static_cast<FunctionID>(ts->m_deferredUnwindFunctionId));
        if (info != nullptr)
        {
            long long deltaNs = ctx->config.computeDeltas ? inclusiveNs : 0;
            ctx->ndjsonWriter->WriteLeave(ts->m_deferredUnwindTsNs, GetCurrentThreadId(), depth, *info, deltaNs);
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
        threadStack->m_deferredUnwindEntry = entry;
        threadStack->m_deferredUnwindFunctionId = functionId;
        threadStack->m_deferredUnwindTsNs = tsNs;
        threadStack->m_hasDeferredUnwind = true;
        return S_OK;
    }

    // Not the catcher's FunctionID — pop and process normally
    CallEntry entry = ctx->callStackManager->Pop();
    long long tsNs = CallStackManager::GetTimestampNs();
    long long inclusiveNs = (entry.enterTsNs > 0) ? (tsNs - entry.enterTsNs) : 0;
    long long selfNs = inclusiveNs - entry.m_childrenTimeNs;
    if (selfNs < 0)
        selfNs = 0;
    ctx->callStackManager->CreditParent(inclusiveNs);
    int depth = ctx->callStackManager->GetDepth();

    if (ctx->statsAggregator && HasEvent(ctx->config.enabledEvents, EventType::MethodStats))
        ctx->statsAggregator->RecordMethod(static_cast<FunctionID>(entry.functionId), inclusiveNs, selfNs);

    if (HasEvent(ctx->config.enabledEvents, EventType::Leave))
    {
        long long deltaNs = ctx->config.computeDeltas ? inclusiveNs : 0;
        ctx->ndjsonWriter->WriteLeave(tsNs, GetCurrentThreadId(), depth, *info, deltaNs);
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
    if (ctx == nullptr || !HasEvent(ctx->config.enabledEvents, EventType::GcStart))
        return S_OK;

    long long startNs = CallStackManager::GetTimestampNs();
    ctx->gcStartNs.store(startNs, std::memory_order_relaxed);

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

    ctx->ndjsonWriter->WriteGcStarted(startNs, gen0, gen1, gen2, reasonStr);
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

    long long nowNs = CallStackManager::GetTimestampNs();
    long long startNs = ctx->gcStartNs.load(std::memory_order_relaxed);
    long long durationNs = (startNs > 0) ? (nowNs - startNs) : 0;

    ctx->ndjsonWriter->WriteGcFinished(nowNs, durationNs);
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
    tid = GetCurrentThreadId();
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

    if (HasEvent(ctx->config.enabledEvents, EventType::Enter))
        ctx->ndjsonWriter->WriteEnter(tsNs, tid, depth, *info);
}

extern "C" void STDMETHODCALLTYPE LeaveStub(FunctionIDOrClientID functionIDOrClientID, COR_PRF_ELT_INFO eltInfo)
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
    long long selfNs = inclusiveNs - entry.m_childrenTimeNs;
    if (selfNs < 0)
        selfNs = 0;
    ctx->callStackManager->CreditParent(inclusiveNs);
    int depth = ctx->callStackManager->GetDepth();

    if (ctx->statsAggregator && HasEvent(ctx->config.enabledEvents, EventType::MethodStats))
        ctx->statsAggregator->RecordMethod(funcId, inclusiveNs, selfNs);

    if (HasEvent(ctx->config.enabledEvents, EventType::Leave))
    {
        long long deltaNs = ctx->config.computeDeltas ? inclusiveNs : 0;
        ctx->ndjsonWriter->WriteLeave(tsNs, tid, depth, *info, deltaNs);
    }
}

extern "C" void STDMETHODCALLTYPE TailcallStub(FunctionIDOrClientID functionIDOrClientID, COR_PRF_ELT_INFO eltInfo)
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
    long long selfNs = inclusiveNs - entry.m_childrenTimeNs;
    if (selfNs < 0)
        selfNs = 0;
    ctx->callStackManager->CreditParent(inclusiveNs);
    int depth = ctx->callStackManager->GetDepth();

    if (ctx->statsAggregator && HasEvent(ctx->config.enabledEvents, EventType::MethodStats))
        ctx->statsAggregator->RecordMethod(funcId, inclusiveNs, selfNs);

    if (HasEvent(ctx->config.enabledEvents, EventType::Leave))
    {
        long long deltaNs = ctx->config.computeDeltas ? inclusiveNs : 0;
        ctx->ndjsonWriter->WriteLeave(tsNs, tid, depth, *info, deltaNs, true);
    }
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
