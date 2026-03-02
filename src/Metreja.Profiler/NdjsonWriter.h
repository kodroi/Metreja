#pragma once

#include <Windows.h>
#include <atomic>
#include <cstdint>
#include <string>
#include <mutex>
#include "MethodCache.h"

class NdjsonWriter
{
public:
    NdjsonWriter(const std::string& outputPath, int64_t maxEvents, const std::string& sessionId, DWORD pid);
    ~NdjsonWriter();
    NdjsonWriter(const NdjsonWriter&) = delete;
    NdjsonWriter& operator=(const NdjsonWriter&) = delete;
    NdjsonWriter(NdjsonWriter&&) = delete;
    NdjsonWriter& operator=(NdjsonWriter&&) = delete;

    void WriteSessionMetadata(const std::string& scenario, long long tsNs);
    void WriteEnter(long long tsNs, DWORD tid, int depth, const MethodInfo& info);
    void WriteLeave(long long tsNs, DWORD tid, int depth, const MethodInfo& info, long long deltaNs,
                    bool tailcall = false);
    void WriteException(long long tsNs, DWORD tid, const MethodInfo& info, const std::string& exType);
    void WriteGcStarted(long long tsNs, bool gen0, bool gen1, bool gen2, const char* reason);
    void WriteGcFinished(long long tsNs, long long durationNs);
    void WriteAllocByClass(long long tsNs, DWORD tid, const std::string& className, ULONG count);
    void Flush();

private:
    bool CheckEventLimit() const;
    void WriteLockedEvent(const char* line, size_t len);
    void AppendToBuffer(const char* data, size_t len);

    static constexpr size_t BUFFER_SIZE = 64 * 1024;
    char m_buffer[BUFFER_SIZE];
    size_t m_bufferPos;
    int64_t m_maxEvents;
    std::atomic<int64_t> m_eventCount;
    HANDLE m_fileHandle;
    std::mutex m_mutex;
    std::string m_sessionId;
    DWORD m_pid;
};
