#include "ConfigReader.h"

#include "platform/pal_io.h"
#include <fstream>
#include <sstream>
#include <nlohmann/json.hpp>

using json = nlohmann::json;

static std::string ExpandPlaceholders(const std::string& path, const std::string& sessionId, DWORD pid)
{
    std::string result = path;

    // Replace {sessionId}
    size_t pos = result.find("{sessionId}");
    while (pos != std::string::npos)
    {
        result.replace(pos, 11, sessionId);
        pos = result.find("{sessionId}", pos + sessionId.length());
    }

    // Replace {pid}
    std::string pidStr = std::to_string(pid);
    pos = result.find("{pid}");
    while (pos != std::string::npos)
    {
        result.replace(pos, 5, pidStr);
        pos = result.find("{pid}", pos + pidStr.length());
    }

    return result;
}

ProfilerConfig ConfigReader::Load()
{
    ProfilerConfig config;

    // Read config file path from environment
    std::string configPath = GetEnvVar("METREJA_CONFIG");
    if (configPath.empty())
        return config;

    // Read and parse JSON config
    std::ifstream file(configPath);
    if (!file.is_open())
        return config;

    json root;
    try
    {
        root = json::parse(file);
    }
    catch (const json::parse_error&)
    {
        return config;
    }

    // Parse top-level session ID
    if (root.contains("sessionId"))
        config.sessionId = root["sessionId"].get<std::string>();

    // Parse metadata
    if (root.contains("metadata"))
    {
        auto& meta = root["metadata"];
        if (meta.contains("scenario"))
            config.scenario = meta["scenario"].get<std::string>();
    }

    // Parse instrumentation
    if (root.contains("instrumentation"))
    {
        auto& inst = root["instrumentation"];
        if (inst.contains("maxEvents"))
            config.maxEvents = inst["maxEvents"].get<int64_t>();
        if (inst.contains("computeDeltas"))
            config.computeDeltas = inst["computeDeltas"].get<bool>();
        if (inst.contains("disableInlining"))
            config.disableInlining = inst["disableInlining"].get<bool>();
        if (inst.contains("statsFlushIntervalSeconds"))
            config.statsFlushIntervalSeconds = inst["statsFlushIntervalSeconds"].get<int>();

        auto parseRules = [](const json& arr, std::vector<FilterRule>& rules)
        {
            for (const auto& item : arr)
            {
                FilterRule rule;
                if (item.contains("level"))
                    rule.level = item["level"].get<std::string>();
                if (item.contains("pattern"))
                    rule.pattern = item["pattern"].get<std::string>();
                rules.push_back(rule);
            }
        };

        if (inst.contains("events") && inst["events"].is_array())
        {
            EventType mask = EventType::None;
            for (const auto& item : inst["events"])
            {
                if (!item.is_string())
                    continue;
                std::string name = item.get<std::string>();
                if (name == "enter")
                    mask |= EventType::Enter;
                else if (name == "leave")
                    mask |= EventType::Leave;
                else if (name == "exception")
                    mask |= EventType::Exception;
                else if (name == "gc_start")
                    mask |= EventType::GcStart;
                else if (name == "gc_end")
                    mask |= EventType::GcEnd;
                else if (name == "alloc_by_class")
                    mask |= EventType::AllocByClass;
                else if (name == "method_stats")
                    mask |= EventType::MethodStats;
                else if (name == "exception_stats")
                    mask |= EventType::ExceptionStats;
                else if (name == "contention_start")
                    mask |= EventType::ContentionStart;
                else if (name == "contention_end")
                    mask |= EventType::ContentionEnd;
                else if (name == "gc_heap_stats")
                    mask |= EventType::GcHeapStats;
                else
                {
                    char msg[256];
                    snprintf(msg, sizeof(msg), "[Metreja] Unknown event type in config: '%s'\n", name.c_str());
                    PalDebugPrint(msg);
                }
            }
            config.enabledEvents = mask;
        }

        if (inst.contains("includes"))
            parseRules(inst["includes"], config.includes);
        if (inst.contains("excludes"))
            parseRules(inst["excludes"], config.excludes);
    }

    // Parse output
    if (root.contains("output"))
    {
        auto& out = root["output"];
        if (out.contains("path"))
            config.outputPath = out["path"].get<std::string>();
    }

    // Override from environment variables
    std::string envSessionId = GetEnvVar("METREJA_SESSION_ID");
    if (!envSessionId.empty())
        config.sessionId = envSessionId;

    std::string envOutput = GetEnvVar("METREJA_OUTPUT");
    if (!envOutput.empty())
        config.outputPath = envOutput;

    // Expand placeholders in output path
    DWORD pid = PalGetCurrentProcessId();
    config.outputPath = ExpandPlaceholders(config.outputPath, config.sessionId, pid);

    return config;
}

std::string ConfigReader::GetEnvVar(const char* name)
{
    char buffer[4096];
    DWORD len = PalGetEnvironmentVariable(name, buffer, sizeof(buffer));
    if (len == 0 || len >= sizeof(buffer))
        return {};
    return std::string(buffer, len);
}

bool ConfigReader::SimpleGlobMatch(const std::string& pattern, const std::string& value)
{
    if (pattern == "*")
        return true;

    // Check for trailing wildcard: "Prefix*" or "Prefix.*" matches anything starting with prefix
    if (pattern.size() >= 2 && pattern.back() == '*')
    {
        std::string prefix = pattern.substr(0, pattern.size() - 1);
        return value.substr(0, prefix.size()) == prefix;
    }

    // Check for leading wildcard: "*.Suffix" matches "Anything.Suffix"
    if (pattern.size() >= 2 && pattern.front() == '*' && pattern[1] == '.')
    {
        std::string suffix = pattern.substr(1);
        if (value.size() >= suffix.size())
            return value.substr(value.size() - suffix.size()) == suffix;
        return false;
    }

    // Exact match
    return pattern == value;
}
