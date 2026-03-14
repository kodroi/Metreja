#pragma once

#include <memory>
#include <shared_mutex>
#include <string>
#include <unordered_map>
#include "include/profiling.h"
#include "ConfigReader.h"

struct MethodInfo
{
    FunctionID methodId = 0;
    std::string assemblyName;
    std::string namespaceName;
    std::string className;
    std::string methodName;
    mdMethodDef methodToken = 0;
    ModuleID moduleId = 0;
    bool isIncluded = false;
    bool isAsyncStateMachine = false;
    std::string originalMethodName;

    const char* GetDisplayName() const
    {
        return isAsyncStateMachine && !originalMethodName.empty()
                   ? originalMethodName.c_str()
                   : methodName.c_str();
    }
};

class MethodCache
{
public:
    MethodCache(ICorProfilerInfo3* profilerInfo, const ProfilerConfig& config);
    ~MethodCache() = default;
    MethodCache(const MethodCache&) = delete;
    MethodCache& operator=(const MethodCache&) = delete;
    MethodCache(MethodCache&&) = delete;
    MethodCache& operator=(MethodCache&&) = delete;

    void ResolveAndCache(FunctionID functionId);
    const MethodInfo* Lookup(FunctionID functionId) const;
    bool ShouldHook(FunctionID functionId);
    std::string ResolveClassName(ClassID classId) const;
    static std::string WideToUtf8(const WCHAR* wide, int len = -1);

private:
    bool MatchesRule(const FilterRule& rule, const std::string& assembly, const std::string& ns, const std::string& cls,
                     const std::string& method) const;
    bool EvaluateFilters(const std::string& assembly, const std::string& ns, const std::string& cls,
                         const std::string& method) const;
    bool IsAsyncStateMachine(const std::string& className, const std::string& methodName) const;
    std::string ExtractOriginalMethodName(const std::string& className) const;

    ICorProfilerInfo3* m_profilerInfo;
    const ProfilerConfig& m_config;
    std::unordered_map<FunctionID, std::unique_ptr<MethodInfo>> m_cache;
    mutable std::shared_mutex m_cacheMutex;
};
