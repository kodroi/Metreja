#pragma once

#include <cstdint>
#include <string>
#include <vector>

enum class EventType : uint32_t
{
    None = 0,
    Enter = 1 << 0,
    Leave = 1 << 1,
    Exception = 1 << 2,
    GcStart = 1 << 3,
    GcEnd = 1 << 4,
    AllocByClass = 1 << 5,
    MethodStats = 1 << 6,
    ExceptionStats = 1 << 7,
    ContentionStart = 1 << 8,
    ContentionEnd = 1 << 9,
    GcHeapStats = 1 << 10,
};

inline EventType operator|(EventType a, EventType b)
{
    return static_cast<EventType>(static_cast<uint32_t>(a) | static_cast<uint32_t>(b));
}

inline EventType& operator|=(EventType& a, EventType b)
{
    a = a | b;
    return a;
}

inline bool HasEvent(EventType mask, EventType flag)
{
    return (static_cast<uint32_t>(mask) & static_cast<uint32_t>(flag)) != 0;
}

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
    bool disableInlining = true;
    EventType enabledEvents = EventType::None;
    int statsFlushIntervalSeconds = 30;
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
