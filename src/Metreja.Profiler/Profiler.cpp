#include "Profiler.h"
#include "Guids.h"
#include "ConfigReader.h"
#include "MethodCache.h"
#include "CallStack.h"
#include "NdjsonWriter.h"

// Global pointers
MetrejaProfiler* g_profiler = nullptr;
MethodCache* g_methodCache = nullptr;
CallStackManager* g_callStackManager = nullptr;
NdjsonWriter* g_ndjsonWriter = nullptr;

// Global config (kept alive for the profiler lifetime)
static ProfilerConfig g_config;
static std::string g_sessionId;
static long long g_gcStartNs;

MetrejaProfiler::MetrejaProfiler()
    : m_refCount(1)
    , m_profilerInfo(nullptr)
{
}

MetrejaProfiler::~MetrejaProfiler()
{
    if (m_profilerInfo != nullptr)
    {
        m_profilerInfo->Release();
        m_profilerInfo = nullptr;
    }
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

    g_profiler = this;

    // Load config
    g_config = ConfigReader::Load();
    g_sessionId = g_config.sessionId;

    // Init subsystems
    CallStackManager::InitFrequency();
    g_callStackManager = new CallStackManager();
    g_methodCache = new MethodCache(m_profilerInfo, g_config);
    g_ndjsonWriter = new NdjsonWriter(g_config.outputPath, g_config.maxEvents);

    // Write session_metadata event
    DWORD pid = GetCurrentProcessId();
    long long tsNs = CallStackManager::GetTimestampNs();
    g_ndjsonWriter->WriteSessionMetadata(g_sessionId, g_config.scenario, pid, tsNs);

    // Set event mask: ENTERLEAVE + EXCEPTIONS + JIT_COMPILATION + FRAME_INFO
    // COR_PRF_ENABLE_FRAME_INFO is required for SetEnterLeaveFunctionHooks3WithInfo
    DWORD eventMask = COR_PRF_MONITOR_ENTERLEAVE | COR_PRF_MONITOR_EXCEPTIONS | COR_PRF_MONITOR_JIT_COMPILATION |
                      COR_PRF_ENABLE_FRAME_INFO;
    if (g_config.trackMemory)
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
    // Flush and clean up subsystems
    if (g_ndjsonWriter != nullptr)
    {
        g_ndjsonWriter->Flush();
        delete g_ndjsonWriter;
        g_ndjsonWriter = nullptr;
    }

    if (g_callStackManager != nullptr)
    {
        delete g_callStackManager;
        g_callStackManager = nullptr;
    }

    if (g_methodCache != nullptr)
    {
        delete g_methodCache;
        g_methodCache = nullptr;
    }

    g_profiler = nullptr;

    if (m_profilerInfo != nullptr)
    {
        m_profilerInfo->Release();
        m_profilerInfo = nullptr;
    }

    return S_OK;
}

HRESULT STDMETHODCALLTYPE MetrejaProfiler::JITCompilationStarted(FunctionID functionId, BOOL fIsSafeToBlock)
{
    if (g_methodCache == nullptr)
        return S_OK;

    g_methodCache->ResolveAndCache(functionId);
    return S_OK;
}

HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionThrown(ObjectID thrownObjectId)
{
    if (g_ndjsonWriter == nullptr || m_profilerInfo == nullptr)
        return S_OK;

    // Get exception class type name
    ClassID classId = 0;
    HRESULT hr = m_profilerInfo->GetClassFromObject(thrownObjectId, &classId);
    if (FAILED(hr))
        return S_OK;

    // Resolve class name via metadata
    ModuleID moduleId = 0;
    mdTypeDef typeDef = 0;
    hr = m_profilerInfo->GetClassIDInfo(classId, &moduleId, &typeDef);
    if (FAILED(hr))
        return S_OK;

    IUnknown* pUnk = nullptr;
    hr = m_profilerInfo->GetModuleMetaData(moduleId, ofRead, IID_IMetaDataImport, &pUnk);
    if (FAILED(hr) || pUnk == nullptr)
        return S_OK;

    IMetaDataImport* metaImport = nullptr;
    hr = pUnk->QueryInterface(IID_IMetaDataImport, reinterpret_cast<void**>(&metaImport));
    pUnk->Release();
    if (FAILED(hr) || metaImport == nullptr)
        return S_OK;

    WCHAR typeName[512];
    ULONG typeNameLen = 0;
    hr = metaImport->GetTypeDefProps(typeDef, typeName, 512, &typeNameLen, nullptr, nullptr);
    metaImport->Release();

    std::string exTypeName = "Unknown";
    if (SUCCEEDED(hr) && typeNameLen > 0)
    {
        exTypeName = MethodCache::WideToUtf8(typeName, static_cast<int>(typeNameLen - 1));
    }

    // Write exception event with a minimal MethodInfo
    long long tsNs = CallStackManager::GetTimestampNs();
    DWORD pid = GetCurrentProcessId();
    DWORD tid = GetCurrentThreadId();

    MethodInfo exInfo{};
    g_ndjsonWriter->WriteException(tsNs, pid, g_sessionId, tid, exInfo, exTypeName);

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
    if (g_ndjsonWriter == nullptr || m_profilerInfo == nullptr || !g_config.trackMemory)
        return S_OK;

    long long tsNs = CallStackManager::GetTimestampNs();
    DWORD pid = GetCurrentProcessId();

    for (ULONG i = 0; i < cClassCount; i++)
    {
        if (classIds[i] == 0 || cObjects[i] == 0)
            continue;

        // Resolve ClassID -> class name via metadata (same chain as ExceptionThrown)
        ModuleID moduleId = 0;
        mdTypeDef typeDef = 0;
        HRESULT hr = m_profilerInfo->GetClassIDInfo(classIds[i], &moduleId, &typeDef);
        if (FAILED(hr))
            continue;

        IUnknown* pUnk = nullptr;
        hr = m_profilerInfo->GetModuleMetaData(moduleId, ofRead, IID_IMetaDataImport, &pUnk);
        if (FAILED(hr) || pUnk == nullptr)
            continue;

        IMetaDataImport* metaImport = nullptr;
        hr = pUnk->QueryInterface(IID_IMetaDataImport, reinterpret_cast<void**>(&metaImport));
        pUnk->Release();
        if (FAILED(hr) || metaImport == nullptr)
            continue;

        WCHAR typeName[512];
        ULONG typeNameLen = 0;
        hr = metaImport->GetTypeDefProps(typeDef, typeName, 512, &typeNameLen, nullptr, nullptr);
        metaImport->Release();

        std::string className = "Unknown";
        if (SUCCEEDED(hr) && typeNameLen > 0)
            className = MethodCache::WideToUtf8(typeName, static_cast<int>(typeNameLen - 1));

        DWORD tid = GetCurrentThreadId();
        g_ndjsonWriter->WriteAllocByClass(tsNs, pid, g_sessionId, tid, className, cObjects[i]);
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
HRESULT STDMETHODCALLTYPE MetrejaProfiler::ExceptionUnwindFunctionEnter(FunctionID functionId) { return S_OK; }
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
    if (g_ndjsonWriter == nullptr || !g_config.trackMemory)
        return S_OK;

    g_gcStartNs = CallStackManager::GetTimestampNs();
    DWORD pid = GetCurrentProcessId();

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

    g_ndjsonWriter->WriteGcStarted(g_gcStartNs, pid, g_sessionId, gen0, gen1, gen2, reasonStr);
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
    if (g_ndjsonWriter == nullptr || !g_config.trackMemory)
        return S_OK;

    long long nowNs = CallStackManager::GetTimestampNs();
    long long durationNs = (g_gcStartNs > 0) ? (nowNs - g_gcStartNs) : 0;
    DWORD pid = GetCurrentProcessId();

    g_ndjsonWriter->WriteGcFinished(nowNs, pid, g_sessionId, durationNs);
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

// Common preamble: validates subsystems, resolves method info, captures timing context.
// Returns false if the event should be skipped.
static inline bool PrepareStubContext(FunctionIDOrClientID functionIDOrClientID, const MethodInfo*& info,
                                      FunctionID& funcId, long long& tsNs, DWORD& pid, DWORD& tid)
{
    if (g_methodCache == nullptr || g_ndjsonWriter == nullptr || g_callStackManager == nullptr)
        return false;

    funcId = functionIDOrClientID.functionID;
    info = g_methodCache->Lookup(funcId);
    if (info == nullptr || !info->isIncluded)
        return false;

    tsNs = CallStackManager::GetTimestampNs();
    pid = GetCurrentProcessId();
    tid = GetCurrentThreadId();
    return true;
}

extern "C" void STDMETHODCALLTYPE EnterStub(FunctionIDOrClientID functionIDOrClientID, COR_PRF_ELT_INFO eltInfo)
{
    const MethodInfo* info;
    FunctionID funcId;
    long long tsNs;
    DWORD pid, tid;
    if (!PrepareStubContext(functionIDOrClientID, info, funcId, tsNs, pid, tid))
        return;

    int depth = g_callStackManager->GetDepth();
    g_callStackManager->Push(funcId, tsNs);
    g_ndjsonWriter->WriteEnter(tsNs, pid, g_sessionId, tid, depth, *info);
}

extern "C" void STDMETHODCALLTYPE LeaveStub(FunctionIDOrClientID functionIDOrClientID, COR_PRF_ELT_INFO eltInfo)
{
    const MethodInfo* info;
    FunctionID funcId;
    long long tsNs;
    DWORD pid, tid;
    if (!PrepareStubContext(functionIDOrClientID, info, funcId, tsNs, pid, tid))
        return;

    CallEntry entry = g_callStackManager->Pop();
    long long deltaNs = (g_config.computeDeltas && entry.enterTsNs > 0) ? (tsNs - entry.enterTsNs) : 0;
    int depth = g_callStackManager->GetDepth();

    g_ndjsonWriter->WriteLeave(tsNs, pid, g_sessionId, tid, depth, *info, deltaNs);
}

extern "C" void STDMETHODCALLTYPE TailcallStub(FunctionIDOrClientID functionIDOrClientID, COR_PRF_ELT_INFO eltInfo)
{
    // Treat tail call as a leave
    LeaveStub(functionIDOrClientID, eltInfo);
}

// FunctionIDMapper2 callback - filter excluded functions at JIT time
extern "C" UINT_PTR STDMETHODCALLTYPE FunctionMapper(FunctionID funcId, void* clientData, BOOL* pbHookFunction)
{
    if (g_methodCache == nullptr)
    {
        *pbHookFunction = FALSE;
        return funcId;
    }

    // ShouldHook resolves and caches the function, then checks filters
    *pbHookFunction = g_methodCache->ShouldHook(funcId) ? TRUE : FALSE;
    return funcId;
}
