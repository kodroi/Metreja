#pragma once

#include "include/profiling.h"
#include "platform/pal_io.h"
#include <atomic>
#include <cstdint>
#include <string>
#include <mutex>
#include "MethodCache.h"

struct MethodStatsAccum;
struct ExceptionStatsAccum;

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
                    bool tailcall = false, long long wallTimeNs = 0);
    void WriteException(long long tsNs, DWORD tid, const MethodInfo& info, const std::string& exType);
    void WriteGcStarted(long long tsNs, bool gen0, bool gen1, bool gen2, const char* reason);
    void WriteGcFinished(long long tsNs, long long durationNs, long long heapSizeBytes = 0);
    void WriteAllocByClass(long long tsNs, DWORD tid, const std::string& className, ULONG count);
    void WriteAllocByClassDetailed(long long tsNs, DWORD tid, const std::string& className, ULONG count,
                                   const MethodInfo& allocMethod);
    void WriteGcHeapStats(long long tsNs, uint64_t gen0Size, uint64_t gen0Promoted, uint64_t gen1Size,
                          uint64_t gen1Promoted, uint64_t gen2Size, uint64_t gen2Promoted, uint64_t lohSize,
                          uint64_t lohPromoted, uint64_t pohSize, uint64_t pohPromoted,
                          long long finalizationQueueLength, int pinnedObjectCount);
    void WriteContentionStart(long long tsNs, DWORD tid);
    void WriteContentionEnd(long long tsNs, DWORD tid, long long durationNs);
    void WriteMethodStats(const MethodInfo& info, const MethodStatsAccum& accum);
    void WriteExceptionStats(const std::string& exType, const ExceptionStatsAccum& accum);
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
    PalFileHandle m_fileHandle;
    std::mutex m_mutex;
    std::string m_sessionId;
    DWORD m_pid;
};
