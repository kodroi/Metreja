#pragma once

#include <string>
#include <unordered_map>
#include "include/profiling.h"
#include "ConfigReader.h"

struct MethodInfo
{
    FunctionID functionId = 0;
    std::string assemblyName;
    std::string namespaceName;
    std::string className;
    std::string methodName;
    mdMethodDef methodToken = 0;
    ModuleID moduleId = 0;
    bool isIncluded = false;
    bool isAsyncStateMachine = false;
    std::string originalMethodName;
    bool logLines = false;
};

class MethodCache
{
public:
    MethodCache(ICorProfilerInfo3* profilerInfo, const ProfilerConfig& config);

    void ResolveAndCache(FunctionID functionId);
    const MethodInfo* Lookup(FunctionID functionId) const;
    bool ShouldHook(FunctionID functionId);
    static std::string WideToUtf8(const WCHAR* wide, int len = -1);

private:
    bool EvaluateFilters(const std::string& assembly, const std::string& ns, const std::string& cls, const std::string& method, bool& outLogLines) const;
    bool IsAsyncStateMachine(const std::string& className, const std::string& methodName) const;
    std::string ExtractOriginalMethodName(const std::string& className) const;

    ICorProfilerInfo3* m_profilerInfo;
    ProfilerConfig m_config;
    std::unordered_map<FunctionID, MethodInfo> m_cache;
};
