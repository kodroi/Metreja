#include "MethodCache.h"

static size_t FindLastPathSeparator(const std::string& path)
{
    size_t pos = path.rfind('\\');
    if (pos == std::string::npos)
        pos = path.rfind('/');
    return pos;
}

MethodCache::MethodCache(ICorProfilerInfo3* profilerInfo, const ProfilerConfig& config)
    : m_profilerInfo(profilerInfo)
    , m_config(config)
{
}

void MethodCache::ResolveAndCache(FunctionID functionId)
{
    // Fast path: check if already cached under shared lock
    {
        std::shared_lock lock(m_cacheMutex);
        if (m_cache.count(functionId) > 0)
            return;
    }

    // Resolve outside lock (metadata API calls are thread-safe)
    auto info = std::make_unique<MethodInfo>();
    info->methodId = functionId;

    // Step 1: GetFunctionInfo2 -> ClassID, ModuleID, mdMethodDef
    ClassID classId = 0;
    ModuleID moduleId = 0;
    mdToken token = 0;

    HRESULT hr = m_profilerInfo->GetFunctionInfo2(functionId, 0, &classId, &moduleId, &token, 0, nullptr, nullptr);
    if (FAILED(hr))
    {
        // Fallback: try GetFunctionInfo (ICorProfilerInfo basic version)
        hr = m_profilerInfo->GetFunctionInfo(functionId, &classId, &moduleId, &token);
        if (FAILED(hr))
        {
            info->isIncluded = false;
            std::unique_lock lock(m_cacheMutex);
            m_cache.try_emplace(functionId, std::move(info));
            return;
        }
    }

    info->methodToken = static_cast<mdMethodDef>(token);
    info->moduleId = moduleId;

    // Step 2: GetModuleMetaData -> IMetaDataImport
    IUnknown* pUnk = nullptr;
    hr = m_profilerInfo->GetModuleMetaData(moduleId, ofRead, IID_IMetaDataImport, &pUnk);
    if (FAILED(hr) || pUnk == nullptr)
    {
        info->isIncluded = false;
        std::unique_lock lock(m_cacheMutex);
        m_cache.try_emplace(functionId, std::move(info));
        return;
    }

    IMetaDataImport* metaImport = nullptr;
    hr = pUnk->QueryInterface(IID_IMetaDataImport, reinterpret_cast<void**>(&metaImport));
    pUnk->Release();
    if (FAILED(hr) || metaImport == nullptr)
    {
        info->isIncluded = false;
        std::unique_lock lock(m_cacheMutex);
        m_cache.try_emplace(functionId, std::move(info));
        return;
    }

    // Step 3: GetMethodProps -> method name, mdTypeDef
    WCHAR methodName[512];
    ULONG methodNameLen = 0;
    mdTypeDef typeDef = 0;
    hr = metaImport->GetMethodProps(info->methodToken, &typeDef, methodName, 512, &methodNameLen, nullptr, nullptr,
                                    nullptr, nullptr, nullptr);
    if (SUCCEEDED(hr))
    {
        info->methodName = WideToUtf8(methodName, static_cast<int>(methodNameLen - 1));
    }

    // Step 4: GetTypeDefProps -> class name (includes namespace)
    WCHAR typeName[1024];
    ULONG typeNameLen = 0;
    hr = metaImport->GetTypeDefProps(typeDef, typeName, 1024, &typeNameLen, nullptr, nullptr);
    if (SUCCEEDED(hr))
    {
        std::string fullTypeName = WideToUtf8(typeName, static_cast<int>(typeNameLen - 1));

        // Split namespace and class at last '.'
        size_t lastDot = fullTypeName.rfind('.');
        if (lastDot != std::string::npos)
        {
            info->namespaceName = fullTypeName.substr(0, lastDot);
            info->className = fullTypeName.substr(lastDot + 1);
        }
        else
        {
            info->namespaceName = "";
            info->className = fullTypeName;
        }
    }

    metaImport->Release();

    // Step 5: GetModuleInfo -> assembly name
    WCHAR moduleName[512];
    ULONG moduleNameLen = 0;
    AssemblyID assemblyId = 0;
    hr = m_profilerInfo->GetModuleInfo(moduleId, nullptr, 512, &moduleNameLen, moduleName, &assemblyId);
    if (SUCCEEDED(hr))
    {
        // Module name is the file path; extract just the file name without extension
        std::string fullPath = WideToUtf8(moduleName, static_cast<int>(moduleNameLen - 1));
        size_t lastSlash = FindLastPathSeparator(fullPath);
        std::string fileName = (lastSlash != std::string::npos) ? fullPath.substr(lastSlash + 1) : fullPath;
        size_t dotPos = fileName.rfind('.');
        info->assemblyName = (dotPos != std::string::npos) ? fileName.substr(0, dotPos) : fileName;
    }

    // Step 6: Detect async state machine
    info->isAsyncStateMachine = IsAsyncStateMachine(info->className, info->methodName);
    if (info->isAsyncStateMachine)
    {
        info->originalMethodName = ExtractOriginalMethodName(info->className);
    }

    // Step 7: Evaluate include/exclude filters
    info->isIncluded = EvaluateFilters(info->assemblyName, info->namespaceName, info->className, info->methodName);

    // Insert under exclusive lock (double-check: another thread may have inserted)
    std::unique_lock lock(m_cacheMutex);
    m_cache.try_emplace(functionId, std::move(info));
}

const MethodInfo* MethodCache::Lookup(FunctionID functionId) const
{
    std::shared_lock lock(m_cacheMutex);
    auto it = m_cache.find(functionId);
    if (it == m_cache.end())
        return nullptr;
    return it->second.get();
}

bool MethodCache::ShouldHook(FunctionID functionId)
{
    ResolveAndCache(functionId);
    const auto* info = Lookup(functionId);
    return info != nullptr && info->isIncluded;
}

std::string MethodCache::ResolveClassName(ClassID classId) const
{
    ModuleID moduleId = 0;
    mdTypeDef typeDef = 0;
    HRESULT hr = m_profilerInfo->GetClassIDInfo(classId, &moduleId, &typeDef);
    if (FAILED(hr))
        return "Unknown";

    IUnknown* pUnk = nullptr;
    hr = m_profilerInfo->GetModuleMetaData(moduleId, ofRead, IID_IMetaDataImport, &pUnk);
    if (FAILED(hr) || pUnk == nullptr)
        return "Unknown";

    IMetaDataImport* metaImport = nullptr;
    hr = pUnk->QueryInterface(IID_IMetaDataImport, reinterpret_cast<void**>(&metaImport));
    pUnk->Release();
    if (FAILED(hr) || metaImport == nullptr)
        return "Unknown";

    WCHAR typeName[512];
    ULONG typeNameLen = 0;
    hr = metaImport->GetTypeDefProps(typeDef, typeName, 512, &typeNameLen, nullptr, nullptr);
    metaImport->Release();

    if (SUCCEEDED(hr) && typeNameLen > 0)
        return WideToUtf8(typeName, static_cast<int>(typeNameLen - 1));

    return "Unknown";
}

bool MethodCache::MatchesRule(const FilterRule& rule, const std::string& assembly, const std::string& ns,
                              const std::string& cls, const std::string& method) const
{
    if (rule.level == "assembly")
        return ConfigReader::SimpleGlobMatch(rule.pattern, assembly);
    if (rule.level == "namespace")
        return ConfigReader::SimpleGlobMatch(rule.pattern, ns);
    if (rule.level == "class")
        return ConfigReader::SimpleGlobMatch(rule.pattern, cls);
    if (rule.level == "method")
        return ConfigReader::SimpleGlobMatch(rule.pattern, method);
    return false;
}

bool MethodCache::EvaluateFilters(const std::string& assembly, const std::string& ns, const std::string& cls,
                                  const std::string& method) const
{
    // If no includes defined, include everything by default
    bool included = m_config.includes.empty();

    // Check includes
    for (const auto& rule : m_config.includes)
    {
        if (MatchesRule(rule, assembly, ns, cls, method))
        {
            included = true;
            break;
        }
    }

    if (!included)
        return false;

    // Check excludes
    for (const auto& rule : m_config.excludes)
    {
        if (MatchesRule(rule, assembly, ns, cls, method))
        {
            return false;
        }
    }

    return true;
}

bool MethodCache::IsAsyncStateMachine(const std::string& className, const std::string& methodName) const
{
    // Async state machines have class names like <MethodName>d__N
    // and the method being called is MoveNext
    if (methodName != "MoveNext")
        return false;

    if (className.size() < 5)
        return false;

    // Check for pattern: starts with '<' and contains ">d__"
    if (className[0] != '<')
        return false;

    size_t closeBracket = className.find(">d__");
    return closeBracket != std::string::npos;
}

std::string MethodCache::ExtractOriginalMethodName(const std::string& className) const
{
    // Extract "MethodName" from "<MethodName>d__N"
    if (className.size() < 2 || className[0] != '<')
        return "";

    size_t closeBracket = className.find('>');
    if (closeBracket == std::string::npos || closeBracket <= 1)
        return "";

    return className.substr(1, closeBracket - 1);
}

std::string MethodCache::WideToUtf8(const WCHAR* wide, int len)
{
    if (wide == nullptr || (len == 0))
        return {};

    int sizeNeeded = WideCharToMultiByte(CP_UTF8, 0, wide, len, nullptr, 0, nullptr, nullptr);
    if (sizeNeeded <= 0)
        return {};

    std::string result(sizeNeeded, '\0');
    WideCharToMultiByte(CP_UTF8, 0, wide, len, result.data(), sizeNeeded, nullptr, nullptr);
    return result;
}
