#pragma once

#include <Windows.h>
#include <string>
#include <vector>

struct FilterRule
{
    std::string assembly = "*";
    std::string nameSpace = "*";
    std::string cls = "*";
    std::string method = "*";
};

struct ProfilerConfig
{
    std::string sessionId;
    std::string scenario;
    std::string outputPath = ".metreja/output/{sessionId}_{pid}.ndjson";
    int maxEvents = 0;
    bool computeDeltas = true;
    bool trackMemory = false;
    std::vector<FilterRule> includes;
    std::vector<FilterRule> excludes;
};

class ConfigReader
{
public:
    static ProfilerConfig Load();
    static bool SimpleGlobMatch(const std::string& pattern, const std::string& value);

private:
    static std::string GetEnvVar(const char* name);
    static std::string ExpandPlaceholders(const std::string& path, const std::string& sessionId, DWORD pid);
};
