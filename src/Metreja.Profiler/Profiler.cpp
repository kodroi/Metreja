#include "Profiler.h"
#include "Guids.h"
#include "ProfilerContext.h"
#include "MethodCache.h"
#include "CallStack.h"
#include "NdjsonWriter.h"

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

    // Publish atomically — callbacks can now proceed
    g_ctx = ctx.release();

    // Set event mask: ENTERLEAVE + EXCEPTIONS + JIT_COMPILATION + FRAME_INFO
    // COR_PRF_ENABLE_FRAME_INFO is required for SetEnterLeaveFunctionHooks3WithInfo
    DWORD eventMask = COR_PRF_MONITOR_ENTERLEAVE | COR_PRF_MONITOR_EXCEPTIONS | COR_PRF_MONITOR_JIT_COMPILATION |
                      COR_PRF_ENABLE_FRAME_INFO;
    if (g_ctx->config.disableInlining)
        eventMask |= COR_PRF_DISABLE_INLINING;
    if (g_ctx->config.trackMemory)
        eventMask |= COR_PRF_MONITOR_GC;
    hr = m_profilerInfo->SetEventMask(eventMask);
    if (FAILED(hr))
        return hr;

    // Set ELT3 hooks via assembly naked stubs
    hr = m_profilerInfo->SetEnterLeaveFunctionHooks3WithInfo(
        reinterpret_cast<FunctionEnter3WithInfo*>(EnterNaked), reinterpret_cast<FunctionLeave3WithInfo*>(LeaveNaked),
        reinterpret_cast<FunctionTailcall3WithInfo*>(TailcallNaked));
    if (FAILED(hr))
        return hr;

    // Set FunctionIDMapper2 to filter excluded functions at JIT time
    hr = m_profilerInfo->SetFunctionIDMapper2(reinterpret_cast<FunctionIDMapper2*>(FunctionMapper), nullptr);
    if (FAILED(hr))
        return hr;

    return S_OK;
}

HRESULT STDMETHODCALLTYPE MetrejaProfiler::Shutdown()
{
    // Stop callbacks first
    ProfilerContext* ctx = g_ctx;
    g_ctx = nullptr;

    // Flush and clean up subsystems
    if (ctx != nullptr)
    {
        if (ctx->ndjsonWriter)
            ctx->ndjsonWriter->Flush();
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

    ctx->ndjsonWriter->WriteException(tsNs, tid, exInfo, exTypeName);

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
    if (ctx == nullptr || !ctx->config.trackMemory)
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
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionSearchCatcherFound(FunctionID functionId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionOSHandlerEnter(UINT_PTR __unused) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionOSHandlerLeave(UINT_PTR __unused) { return S_OK; }
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

    CallEntry entry = ctx->callStackManager->Pop();
    long long tsNs = CallStackManager::GetTimestampNs();
    long long deltaNs = (ctx->config.computeDeltas && entry.enterTsNs > 0) ? (tsNs - entry.enterTsNs) : 0;
    int depth = ctx->callStackManager->GetDepth();

    ctx->ndjsonWriter->WriteLeave(tsNs, GetCurrentThreadId(), depth, *info, deltaNs);
    return S_OK;
}
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionUnwindFunctionLeave() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionUnwindFinallyEnter(FunctionID functionId) { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionUnwindFinallyLeave() { return S_OK; }
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionCatcherEnter(FunctionID functionId, ObjectID objectId)
{
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
    if (ctx == nullptr || !ctx->config.trackMemory)
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
    if (ctx == nullptr || !ctx->config.trackMemory)
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
    long long deltaNs = (ctx->config.computeDeltas && entry.enterTsNs > 0) ? (tsNs - entry.enterTsNs) : 0;
    int depth = ctx->callStackManager->GetDepth();

    ctx->ndjsonWriter->WriteLeave(tsNs, tid, depth, *info, deltaNs);
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
    long long deltaNs = (ctx->config.computeDeltas && entry.enterTsNs > 0) ? (tsNs - entry.enterTsNs) : 0;
    int depth = ctx->callStackManager->GetDepth();

    ctx->ndjsonWriter->WriteLeave(tsNs, tid, depth, *info, deltaNs, true);
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
