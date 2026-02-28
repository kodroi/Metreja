#include "NdjsonWriter.h"

#include <cstdio>
#include <cstring>

NdjsonWriter::NdjsonWriter(const std::string& outputPath, int maxEvents)
    : m_bufferPos(0)
    , m_maxEvents(maxEvents)
    , m_eventCount(0)
    , m_fileHandle(INVALID_HANDLE_VALUE)
{
    m_buffer[0] = '\0';

    // Create parent directories if needed
    size_t lastSlash = outputPath.rfind('\\');
    if (lastSlash == std::string::npos)
        lastSlash = outputPath.rfind('/');
    if (lastSlash != std::string::npos)
    {
        std::string dir = outputPath.substr(0, lastSlash);
        CreateDirectoryA(dir.c_str(), nullptr);
    }

    m_fileHandle = CreateFileA(
        outputPath.c_str(),
        GENERIC_WRITE,
        FILE_SHARE_READ,
        nullptr,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
}

NdjsonWriter::~NdjsonWriter()
{
    Flush();
    if (m_fileHandle != INVALID_HANDLE_VALUE)
    {
        CloseHandle(m_fileHandle);
        m_fileHandle = INVALID_HANDLE_VALUE;
    }
}

bool NdjsonWriter::CheckEventLimit() const
{
    return m_maxEvents > 0 && m_eventCount >= m_maxEvents;
}

void NdjsonWriter::WriteLockedEvent(const char* line, size_t len)
{
    std::lock_guard<std::mutex> lock(m_mutex);
    AppendToBuffer(line, len);
    m_eventCount++;
}

void NdjsonWriter::WriteRunMetadata(const std::string& runId, const std::string& scenario, DWORD pid, long long tsNs)
{
    char line[2048];
    int len = snprintf(line, sizeof(line),
        R"({"event":"run_metadata","tsNs":%lld,"pid":%lu,"runId":"%s","scenario":"%s"})""\n",
        tsNs, pid, runId.c_str(), scenario.c_str());

    if (len > 0)
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        AppendToBuffer(line, static_cast<size_t>(len));
    }
}

void NdjsonWriter::WriteEnter(long long tsNs, DWORD pid, const std::string& runId,
    DWORD tid, int depth, const MethodInfo& info)
{
    if (CheckEventLimit())
        return;

    char line[2048];
    const char* methodName = info.isAsyncStateMachine ? info.originalMethodName.c_str() : info.methodName.c_str();
    int len = snprintf(line, sizeof(line),
        R"({"event":"enter","tsNs":%lld,"pid":%lu,"runId":"%s","tid":%lu,"depth":%d,"asm":"%s","ns":"%s","cls":"%s","m":"%s","async":%s})""\n",
        tsNs, pid, runId.c_str(), tid, depth,
        info.assemblyName.c_str(), info.namespaceName.c_str(),
        info.className.c_str(), methodName,
        info.isAsyncStateMachine ? "true" : "false");

    if (len > 0)
        WriteLockedEvent(line, static_cast<size_t>(len));
}

void NdjsonWriter::WriteLeave(long long tsNs, DWORD pid, const std::string& runId,
    DWORD tid, int depth, const MethodInfo& info, long long deltaNs)
{
    if (CheckEventLimit())
        return;

    char line[2048];
    const char* methodName = info.isAsyncStateMachine ? info.originalMethodName.c_str() : info.methodName.c_str();
    int len = snprintf(line, sizeof(line),
        R"({"event":"leave","tsNs":%lld,"pid":%lu,"runId":"%s","tid":%lu,"depth":%d,"asm":"%s","ns":"%s","cls":"%s","m":"%s","async":%s,"deltaNs":%lld})""\n",
        tsNs, pid, runId.c_str(), tid, depth,
        info.assemblyName.c_str(), info.namespaceName.c_str(),
        info.className.c_str(), methodName,
        info.isAsyncStateMachine ? "true" : "false",
        deltaNs);

    if (len > 0)
        WriteLockedEvent(line, static_cast<size_t>(len));
}

void NdjsonWriter::WriteException(long long tsNs, DWORD pid, const std::string& runId,
    DWORD tid, const MethodInfo& info, const std::string& exType)
{
    if (CheckEventLimit())
        return;

    char line[2048];
    int len = snprintf(line, sizeof(line),
        R"({"event":"exception","tsNs":%lld,"pid":%lu,"runId":"%s","tid":%lu,"asm":"%s","ns":"%s","cls":"%s","m":"%s","exType":"%s"})""\n",
        tsNs, pid, runId.c_str(), tid,
        info.assemblyName.c_str(), info.namespaceName.c_str(),
        info.className.c_str(), info.methodName.c_str(),
        exType.c_str());

    if (len > 0)
        WriteLockedEvent(line, static_cast<size_t>(len));
}

void NdjsonWriter::WriteGcStarted(long long tsNs, DWORD pid, const std::string& runId,
    bool gen0, bool gen1, bool gen2, const char* reason)
{
    char line[2048];
    int len = snprintf(line, sizeof(line),
        R"({"event":"gc_start","tsNs":%lld,"pid":%lu,"runId":"%s","gen0":%s,"gen1":%s,"gen2":%s,"reason":"%s"})""\n",
        tsNs, pid, runId.c_str(),
        gen0 ? "true" : "false",
        gen1 ? "true" : "false",
        gen2 ? "true" : "false",
        reason);

    if (len > 0)
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        AppendToBuffer(line, static_cast<size_t>(len));
        // GC events don't count against m_maxEvents
    }
}

void NdjsonWriter::WriteGcFinished(long long tsNs, DWORD pid, const std::string& runId, long long durationNs)
{
    char line[2048];
    int len = snprintf(line, sizeof(line),
        R"({"event":"gc_end","tsNs":%lld,"pid":%lu,"runId":"%s","durationNs":%lld})""\n",
        tsNs, pid, runId.c_str(), durationNs);

    if (len > 0)
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        AppendToBuffer(line, static_cast<size_t>(len));
        // GC events don't count against m_maxEvents
    }
}

void NdjsonWriter::WriteAllocByClass(long long tsNs, DWORD pid, const std::string& runId,
    DWORD tid, const std::string& className, ULONG count)
{
    if (CheckEventLimit())
        return;

    char line[2048];
    int len = snprintf(line, sizeof(line),
        R"({"event":"alloc_by_class","tsNs":%lld,"pid":%lu,"runId":"%s","tid":%lu,"className":"%s","count":%lu})""\n",
        tsNs, pid, runId.c_str(), tid, className.c_str(), count);

    if (len > 0)
        WriteLockedEvent(line, static_cast<size_t>(len));
}

void NdjsonWriter::Flush()
{
    std::lock_guard<std::mutex> lock(m_mutex);
    if (m_bufferPos == 0 || m_fileHandle == INVALID_HANDLE_VALUE)
        return;

    DWORD written = 0;
    WriteFile(m_fileHandle, m_buffer, static_cast<DWORD>(m_bufferPos), &written, nullptr);
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
        if (m_fileHandle != INVALID_HANDLE_VALUE && m_bufferPos > 0)
        {
            DWORD written = 0;
            WriteFile(m_fileHandle, m_buffer, static_cast<DWORD>(m_bufferPos), &written, nullptr);
            m_bufferPos = 0;
        }
    }

    // If single line exceeds buffer, write directly
    if (len > BUFFER_SIZE)
    {
        if (m_fileHandle != INVALID_HANDLE_VALUE)
        {
            DWORD written = 0;
            WriteFile(m_fileHandle, data, static_cast<DWORD>(len), &written, nullptr);
        }
        return;
    }

    memcpy(m_buffer + m_bufferPos, data, len);
    m_bufferPos += len;
}
