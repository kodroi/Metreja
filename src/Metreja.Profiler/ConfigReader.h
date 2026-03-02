#pragma once

#include <cstdint>
#include <string>
#include <vector>

struct FilterRule
{
    std::string level = "assembly"; // "assembly", "namespace", "class", "method"
    std::string pattern = "*";
};

struct ProfilerConfig
{
    std::string sessionId;
    std::string scenario;
    std::string outputPath = ".metreja/output/{sessionId}_{pid}.ndjson";
    int64_t maxEvents = 0;
    bool computeDeltas = true;
    bool trackMemory = false;
    bool disableInlining = true;
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
};
