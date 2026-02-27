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
    bool logLines = false;
};

struct ProfilerConfig
{
    std::string runId;
    std::string scenario;
    std::string outputPath = ".metreja/output/{runId}_{pid}.ndjson";
    std::string mode = "elt3";
    int maxEvents = 0;
    bool computeDeltas = true;
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
    static std::string ExpandPlaceholders(const std::string& path, const std::string& runId, DWORD pid);
};
