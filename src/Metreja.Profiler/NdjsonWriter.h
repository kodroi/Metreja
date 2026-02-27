#pragma once

#include <Windows.h>
#include <string>
#include <mutex>
#include "MethodCache.h"

class NdjsonWriter
{
public:
    NdjsonWriter(const std::string& outputPath, int maxEvents);
    ~NdjsonWriter();

    void WriteRunMetadata(const std::string& runId, const std::string& scenario, DWORD pid, long long tsNs);
    void WriteEnter(long long tsNs, DWORD pid, const std::string& runId, DWORD tid, int depth, const MethodInfo& info);
    void WriteLeave(long long tsNs, DWORD pid, const std::string& runId, DWORD tid, int depth, const MethodInfo& info, long long deltaNs);
    void WriteException(long long tsNs, DWORD pid, const std::string& runId, DWORD tid, const MethodInfo& info, const std::string& exType);
    void Flush();

private:
    void AppendToBuffer(const char* data, size_t len);

    static constexpr size_t BUFFER_SIZE = 64 * 1024;
    char m_buffer[BUFFER_SIZE];
    size_t m_bufferPos;
    int m_maxEvents;
    int m_eventCount;
    HANDLE m_fileHandle;
    std::mutex m_mutex;
};
