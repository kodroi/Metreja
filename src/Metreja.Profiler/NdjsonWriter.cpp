#include "NdjsonWriter.h"
#include "StatsAggregator.h"
#include "StringUtils.h"

#include <cstdio>
#include <cstring>

static void CreateDirectoriesRecursive(const std::string& path)
{
    if (path.empty())
        return;

    // Try creating the directory; if it succeeds or already exists, we're done
    if (PalCreateDirectory(path.c_str()))
        return;

    // Otherwise, create parent first
    size_t sep = FindLastPathSeparator(path);
    if (sep != std::string::npos && sep > 0)
    {
        CreateDirectoriesRecursive(path.substr(0, sep));
        PalCreateDirectory(path.c_str());
    }
}

NdjsonWriter::NdjsonWriter(const std::string& outputPath, int64_t maxEvents, const std::string& sessionId, DWORD pid)
    : m_bufferPos(0)
    , m_maxEvents(maxEvents)
    , m_eventCount(0)
    , m_fileHandle(PAL_INVALID_FILE_HANDLE)
    , m_sessionId(sessionId)
    , m_pid(pid)
{
    m_buffer[0] = '\0';

    // Create parent directories if needed
    size_t lastSlash = FindLastPathSeparator(outputPath);
    if (lastSlash != std::string::npos)
    {
        std::string dir = outputPath.substr(0, lastSlash);
        CreateDirectoriesRecursive(dir);
    }

    m_fileHandle = PalCreateFile(outputPath.c_str());
    if (m_fileHandle == PAL_INVALID_FILE_HANDLE)
    {
        char msg[512];
        snprintf(msg, sizeof(msg), "Metreja: Failed to create output file '%s'\n", outputPath.c_str());
        PalDebugPrint(msg);
    }
}

NdjsonWriter::~NdjsonWriter()
{
    Flush();
    if (m_fileHandle != PAL_INVALID_FILE_HANDLE)
    {
        PalCloseFile(m_fileHandle);
        m_fileHandle = PAL_INVALID_FILE_HANDLE;
    }
}

bool NdjsonWriter::CheckEventLimit() const
{
    return m_maxEvents > 0 && m_eventCount.load(std::memory_order_relaxed) >= m_maxEvents;
}

void NdjsonWriter::WriteLockedEvent(const char* line, size_t len)
{
    std::lock_guard<std::mutex> lock(m_mutex);
    AppendToBuffer(line, len);
    m_eventCount.fetch_add(1, std::memory_order_relaxed);
}

void NdjsonWriter::WriteSessionMetadata(const std::string& scenario, long long tsNs)
{
    char line[2048];
    int len = snprintf(line, sizeof(line),
                       R"({"event":"session_metadata","tsNs":%lld,"pid":%lu,"sessionId":"%s","scenario":"%s"})"
                       "\n",
                       tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), scenario.c_str());

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        AppendToBuffer(line, static_cast<size_t>(len));
    }
}

void NdjsonWriter::WriteEnter(long long tsNs, DWORD tid, int depth, const MethodInfo& info)
{
    if (CheckEventLimit())
        return;

    char line[2048];
    const char* methodName = info.GetDisplayName();
    int len;
    if (info.isAsyncStateMachine)
    {
        len = snprintf(
            line, sizeof(line),
            R"({"event":"enter","tsNs":%lld,"pid":%lu,"sessionId":"%s","tid":%lu,"depth":%d,"asm":"%s","ns":"%s","cls":"%s","m":"%s","async":true})"
            "\n",
            tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), static_cast<unsigned long>(tid), depth,
            info.assemblyName.c_str(), info.namespaceName.c_str(), info.className.c_str(), methodName);
    }
    else
    {
        len = snprintf(
            line, sizeof(line),
            R"({"event":"enter","tsNs":%lld,"pid":%lu,"sessionId":"%s","tid":%lu,"depth":%d,"asm":"%s","ns":"%s","cls":"%s","m":"%s"})"
            "\n",
            tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), static_cast<unsigned long>(tid), depth,
            info.assemblyName.c_str(), info.namespaceName.c_str(), info.className.c_str(), methodName);
    }

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
        WriteLockedEvent(line, static_cast<size_t>(len));
}

void NdjsonWriter::WriteLeave(long long tsNs, DWORD tid, int depth, const MethodInfo& info, long long deltaNs,
                              bool tailcall, long long wallTimeNs)
{
    if (CheckEventLimit())
        return;

    char line[2048];
    const char* methodName = info.GetDisplayName();
    char suffix[128] = "";
    int suffixLen = 0;
    if (info.isAsyncStateMachine)
        suffixLen += snprintf(suffix + suffixLen, sizeof(suffix) - suffixLen, R"(,"async":true)");
    if (tailcall)
        suffixLen += snprintf(suffix + suffixLen, sizeof(suffix) - suffixLen, R"(,"tailcall":true)");
    if (wallTimeNs > 0)
        suffixLen += snprintf(suffix + suffixLen, sizeof(suffix) - suffixLen, R"(,"wallTimeNs":%lld)", wallTimeNs);
    int len = snprintf(
        line, sizeof(line),
        R"({"event":"leave","tsNs":%lld,"pid":%lu,"sessionId":"%s","tid":%lu,"depth":%d,"asm":"%s","ns":"%s","cls":"%s","m":"%s","deltaNs":%lld%s})"
        "\n",
        tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), static_cast<unsigned long>(tid), depth,
        info.assemblyName.c_str(), info.namespaceName.c_str(), info.className.c_str(), methodName, deltaNs, suffix);

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
        WriteLockedEvent(line, static_cast<size_t>(len));
}

void NdjsonWriter::WriteException(long long tsNs, DWORD tid, const MethodInfo& info, const std::string& exType)
{
    if (CheckEventLimit())
        return;

    char line[2048];
    int len = snprintf(
        line, sizeof(line),
        R"({"event":"exception","tsNs":%lld,"pid":%lu,"sessionId":"%s","tid":%lu,"asm":"%s","ns":"%s","cls":"%s","m":"%s","exType":"%s"})"
        "\n",
        tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), static_cast<unsigned long>(tid),
        info.assemblyName.c_str(), info.namespaceName.c_str(), info.className.c_str(), info.methodName.c_str(),
        exType.c_str());

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
        WriteLockedEvent(line, static_cast<size_t>(len));
}

void NdjsonWriter::WriteGcStart(long long tsNs, bool gen0, bool gen1, bool gen2, const char* reason)
{
    char line[2048];
    int len = snprintf(
        line, sizeof(line),
        R"({"event":"gc_start","tsNs":%lld,"pid":%lu,"sessionId":"%s","gen0":%s,"gen1":%s,"gen2":%s,"reason":"%s"})"
        "\n",
        tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), gen0 ? "true" : "false", gen1 ? "true" : "false",
        gen2 ? "true" : "false", reason);

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        AppendToBuffer(line, static_cast<size_t>(len));
        // GC events don't count against m_maxEvents
    }
}

void NdjsonWriter::WriteGcEnd(long long tsNs, long long durationNs, long long heapSizeBytes)
{
    char line[2048];
    char heapPart[64] = "";
    if (heapSizeBytes > 0)
        snprintf(heapPart, sizeof(heapPart), R"(,"heapSizeBytes":%lld)", heapSizeBytes);

    int len = snprintf(line, sizeof(line),
                       R"({"event":"gc_end","tsNs":%lld,"pid":%lu,"sessionId":"%s","durationNs":%lld%s})"
                       "\n",
                       tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), durationNs, heapPart);

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        AppendToBuffer(line, static_cast<size_t>(len));
        // GC events don't count against m_maxEvents
    }
}

void NdjsonWriter::WriteGcHeapStats(long long tsNs, uint64_t gen0Size, uint64_t gen0Promoted, uint64_t gen1Size,
                                    uint64_t gen1Promoted, uint64_t gen2Size, uint64_t gen2Promoted, uint64_t lohSize,
                                    uint64_t lohPromoted, uint64_t pohSize, uint64_t pohPromoted,
                                    long long finalizationQueueLength, int pinnedObjectCount)
{
    char line[2048];
    int len = snprintf(line, sizeof(line),
                       R"({"event":"gc_heap_stats","tsNs":%lld,"pid":%lu,"sessionId":"%s",)"
                       R"("gen0SizeBytes":%llu,"gen0PromotedBytes":%llu,)"
                       R"("gen1SizeBytes":%llu,"gen1PromotedBytes":%llu,)"
                       R"("gen2SizeBytes":%llu,"gen2PromotedBytes":%llu,)"
                       R"("lohSizeBytes":%llu,"lohPromotedBytes":%llu,)"
                       R"("pohSizeBytes":%llu,"pohPromotedBytes":%llu,)"
                       R"("finalizationQueueLength":%lld,"pinnedObjectCount":%d})"
                       "\n",
                       tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(),
                       static_cast<unsigned long long>(gen0Size), static_cast<unsigned long long>(gen0Promoted),
                       static_cast<unsigned long long>(gen1Size), static_cast<unsigned long long>(gen1Promoted),
                       static_cast<unsigned long long>(gen2Size), static_cast<unsigned long long>(gen2Promoted),
                       static_cast<unsigned long long>(lohSize), static_cast<unsigned long long>(lohPromoted),
                       static_cast<unsigned long long>(pohSize), static_cast<unsigned long long>(pohPromoted),
                       finalizationQueueLength, pinnedObjectCount);

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        AppendToBuffer(line, static_cast<size_t>(len));
        // GC heap stats events don't count against m_maxEvents
    }
}

void NdjsonWriter::WriteAllocByClass(long long tsNs, DWORD tid, const std::string& className, ULONG count)
{
    if (CheckEventLimit())
        return;

    char line[2048];
    int len = snprintf(
        line, sizeof(line),
        R"({"event":"alloc_by_class","tsNs":%lld,"pid":%lu,"sessionId":"%s","tid":%lu,"className":"%s","count":%lu})"
        "\n",
        tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), static_cast<unsigned long>(tid),
        className.c_str(), static_cast<unsigned long>(count));

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
        WriteLockedEvent(line, static_cast<size_t>(len));
}

void NdjsonWriter::WriteAllocByClassDetailed(long long tsNs, DWORD tid, const std::string& className, ULONG count,
                                             const MethodInfo& allocMethod)
{
    if (CheckEventLimit())
        return;

    char line[2048];
    int len = snprintf(
        line, sizeof(line),
        R"({"event":"alloc_by_class","tsNs":%lld,"pid":%lu,"sessionId":"%s","tid":%lu,"className":"%s","count":%lu,"allocAsm":"%s","allocNs":"%s","allocCls":"%s","allocM":"%s"})"
        "\n",
        tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), static_cast<unsigned long>(tid),
        className.c_str(), static_cast<unsigned long>(count), allocMethod.assemblyName.c_str(),
        allocMethod.namespaceName.c_str(), allocMethod.className.c_str(), allocMethod.methodName.c_str());

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
        WriteLockedEvent(line, static_cast<size_t>(len));
}

void NdjsonWriter::WriteContentionStart(long long tsNs, DWORD tid)
{
    if (CheckEventLimit())
        return;

    char line[2048];
    int len = snprintf(line, sizeof(line),
                       R"({"event":"contention_start","tsNs":%lld,"pid":%lu,"sessionId":"%s","tid":%lu})"
                       "\n",
                       tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), static_cast<unsigned long>(tid));

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
        WriteLockedEvent(line, static_cast<size_t>(len));
}

void NdjsonWriter::WriteContentionEnd(long long tsNs, DWORD tid, long long durationNs)
{
    if (CheckEventLimit())
        return;

    char line[2048];
    int len = snprintf(
        line, sizeof(line),
        R"({"event":"contention_end","tsNs":%lld,"pid":%lu,"sessionId":"%s","tid":%lu,"durationNs":%lld})"
        "\n",
        tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), static_cast<unsigned long>(tid), durationNs);

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
        WriteLockedEvent(line, static_cast<size_t>(len));
}

void NdjsonWriter::WriteMethodStats(const MethodInfo& info, const MethodStatsAccum& accum)
{
    char line[2048];
    const char* methodName = info.GetDisplayName();
    int len = snprintf(
        line, sizeof(line),
        R"({"event":"method_stats","tsNs":0,"pid":%lu,"sessionId":"%s","asm":"%s","ns":"%s","cls":"%s","m":"%s","callCount":%lld,"totalSelfNs":%lld,"maxSelfNs":%lld,"totalInclusiveNs":%lld,"maxInclusiveNs":%lld})"
        "\n",
        static_cast<unsigned long>(m_pid), m_sessionId.c_str(), info.assemblyName.c_str(), info.namespaceName.c_str(),
        info.className.c_str(), methodName, accum.callCount, accum.totalSelfNs, accum.maxSelfNs, accum.totalInclusiveNs,
        accum.maxInclusiveNs);

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        AppendToBuffer(line, static_cast<size_t>(len));
        // Stats events bypass maxEvents limit (like session_metadata)
    }
}

void NdjsonWriter::WriteExceptionStats(const std::string& exType, const ExceptionStatsAccum& accum)
{
    char line[2048];
    int len = snprintf(
        line, sizeof(line),
        R"({"event":"exception_stats","tsNs":0,"pid":%lu,"sessionId":"%s","exType":"%s","asm":"%s","ns":"%s","cls":"%s","m":"%s","count":%lld})"
        "\n",
        static_cast<unsigned long>(m_pid), m_sessionId.c_str(), exType.c_str(), accum.assemblyName.c_str(),
        accum.namespaceName.c_str(), accum.className.c_str(), accum.methodName.c_str(), accum.count);

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        AppendToBuffer(line, static_cast<size_t>(len));
        // Stats events bypass maxEvents limit (like session_metadata)
    }
}

void NdjsonWriter::Flush()
{
    std::lock_guard<std::mutex> lock(m_mutex);
    if (m_bufferPos == 0 || m_fileHandle == PAL_INVALID_FILE_HANDLE)
        return;

    PalWriteFile(m_fileHandle, m_buffer, m_bufferPos);
    m_bufferPos = 0;
}

void NdjsonWriter::AppendToBuffer(const char* data, size_t len)
{
    // Caller must hold m_mutex
    if (len == 0)
        return;

    // Flush at 80% capacity to avoid frequent small writes
    if (m_bufferPos + len > BUFFER_SIZE * 80 / 100)
    {
        if (m_fileHandle != PAL_INVALID_FILE_HANDLE && m_bufferPos > 0)
        {
            PalWriteFile(m_fileHandle, m_buffer, m_bufferPos);
            m_bufferPos = 0;
        }
    }

    // If single line exceeds buffer, write directly
    if (len > BUFFER_SIZE)
    {
        if (m_fileHandle != PAL_INVALID_FILE_HANDLE)
        {
            PalWriteFile(m_fileHandle, data, len);
        }
        return;
    }

    memcpy(m_buffer + m_bufferPos, data, len);
    m_bufferPos += len;
}
