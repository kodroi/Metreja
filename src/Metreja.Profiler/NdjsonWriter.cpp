#include "NdjsonWriter.h"
#include "StatsAggregator.h"

#include <cstdio>
#include <cstring>

static size_t FindLastPathSeparator(const std::string& path)
{
    size_t pos = path.rfind('\\');
    if (pos == std::string::npos)
        pos = path.rfind('/');
    return pos;
}

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
    const char* methodName = info.isAsyncStateMachine && !info.originalMethodName.empty()
                                 ? info.originalMethodName.c_str()
                                 : info.methodName.c_str();
    int len = snprintf(
        line, sizeof(line),
        R"({"event":"enter","tsNs":%lld,"pid":%lu,"sessionId":"%s","tid":%lu,"depth":%d,"asm":"%s","ns":"%s","cls":"%s","m":"%s","async":%s})"
        "\n",
        tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), static_cast<unsigned long>(tid), depth,
        info.assemblyName.c_str(), info.namespaceName.c_str(), info.className.c_str(), methodName,
        info.isAsyncStateMachine ? "true" : "false");

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
        WriteLockedEvent(line, static_cast<size_t>(len));
}

void NdjsonWriter::WriteLeave(long long tsNs, DWORD tid, int depth, const MethodInfo& info, long long deltaNs,
                              bool tailcall)
{
    if (CheckEventLimit())
        return;

    char line[2048];
    const char* methodName = info.isAsyncStateMachine && !info.originalMethodName.empty()
                                 ? info.originalMethodName.c_str()
                                 : info.methodName.c_str();
    int len;
    if (tailcall)
    {
        len = snprintf(
            line, sizeof(line),
            R"({"event":"leave","tsNs":%lld,"pid":%lu,"sessionId":"%s","tid":%lu,"depth":%d,"asm":"%s","ns":"%s","cls":"%s","m":"%s","async":%s,"deltaNs":%lld,"tailcall":true})"
            "\n",
            tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), static_cast<unsigned long>(tid), depth,
            info.assemblyName.c_str(), info.namespaceName.c_str(), info.className.c_str(), methodName,
            info.isAsyncStateMachine ? "true" : "false", deltaNs);
    }
    else
    {
        len = snprintf(
            line, sizeof(line),
            R"({"event":"leave","tsNs":%lld,"pid":%lu,"sessionId":"%s","tid":%lu,"depth":%d,"asm":"%s","ns":"%s","cls":"%s","m":"%s","async":%s,"deltaNs":%lld})"
            "\n",
            tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), static_cast<unsigned long>(tid), depth,
            info.assemblyName.c_str(), info.namespaceName.c_str(), info.className.c_str(), methodName,
            info.isAsyncStateMachine ? "true" : "false", deltaNs);
    }

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

void NdjsonWriter::WriteGcStarted(long long tsNs, bool gen0, bool gen1, bool gen2, const char* reason)
{
    char line[2048];
    int len = snprintf(
        line, sizeof(line),
        R"({"event":"gc_start","tsNs":%lld,"pid":%lu,"sessionId":"%s","gen0":%s,"gen1":%s,"gen2":%s,"reason":"%s"})"
        "\n",
        tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), gen0 ? "true" : "false",
        gen1 ? "true" : "false", gen2 ? "true" : "false", reason);

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        AppendToBuffer(line, static_cast<size_t>(len));
        // GC events don't count against m_maxEvents
    }
}

void NdjsonWriter::WriteGcFinished(long long tsNs, long long durationNs)
{
    char line[2048];
    int len = snprintf(line, sizeof(line),
                       R"({"event":"gc_end","tsNs":%lld,"pid":%lu,"sessionId":"%s","durationNs":%lld})"
                       "\n",
                       tsNs, static_cast<unsigned long>(m_pid), m_sessionId.c_str(), durationNs);

    if (len > 0 && static_cast<size_t>(len) < sizeof(line))
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        AppendToBuffer(line, static_cast<size_t>(len));
        // GC events don't count against m_maxEvents
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

void NdjsonWriter::WriteMethodStats(const MethodInfo& info, const MethodStatsAccum& accum)
{
    char line[2048];
    const char* methodName = info.isAsyncStateMachine && !info.originalMethodName.empty()
                                 ? info.originalMethodName.c_str()
                                 : info.methodName.c_str();
    int len = snprintf(
        line, sizeof(line),
        R"({"event":"method_stats","tsNs":0,"pid":%lu,"sessionId":"%s","asm":"%s","ns":"%s","cls":"%s","m":"%s","callCount":%lld,"totalSelfNs":%lld,"maxSelfNs":%lld,"totalInclusiveNs":%lld,"maxInclusiveNs":%lld})"
        "\n",
        static_cast<unsigned long>(m_pid), m_sessionId.c_str(), info.assemblyName.c_str(),
        info.namespaceName.c_str(), info.className.c_str(), methodName, accum.callCount, accum.totalSelfNs,
        accum.maxSelfNs, accum.totalInclusiveNs, accum.maxInclusiveNs);

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
