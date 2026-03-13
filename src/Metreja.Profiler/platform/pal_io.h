#pragma once

// Platform Abstraction Layer — File I/O, process info, environment, debug output

#ifdef _WIN32

#include <Windows.h>
#include <cstdio>

// ─── File I/O ─────────────────────────────────────────────────────────────────
typedef HANDLE PalFileHandle;
#define PAL_INVALID_FILE_HANDLE INVALID_HANDLE_VALUE

inline PalFileHandle PalCreateFile(const char* path)
{
    return CreateFileA(path, GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
}

inline bool PalWriteFile(PalFileHandle h, const void* data, size_t len)
{
    DWORD written = 0;
    return WriteFile(h, data, static_cast<DWORD>(len), &written, nullptr) != 0;
}

inline void PalCloseFile(PalFileHandle h)
{
    if (h != PAL_INVALID_FILE_HANDLE)
        CloseHandle(h);
}

// ─── Directories ──────────────────────────────────────────────────────────────

inline bool PalCreateDirectory(const char* path)
{
    return CreateDirectoryA(path, nullptr) || GetLastError() == ERROR_ALREADY_EXISTS;
}

// ─── Process / Thread ID ──────────────────────────────────────────────────────

inline DWORD PalGetCurrentProcessId() { return GetCurrentProcessId(); }
inline DWORD PalGetCurrentThreadId() { return GetCurrentThreadId(); }

// ─── Debug output ─────────────────────────────────────────────────────────────

inline void PalDebugPrint(const char* msg) { OutputDebugStringA(msg); }

// ─── Environment variables ───────────────────────────────────────────────────

inline DWORD PalGetEnvironmentVariable(const char* name, char* buffer, DWORD size)
{
    return GetEnvironmentVariableA(name, buffer, size);
}

// ─── Wide string conversion ──────────────────────────────────────────────────

inline int PalWideCharToMultiByte(const WCHAR* wide, int wideLen, char* mb, int mbSize)
{
    return WideCharToMultiByte(CP_UTF8, 0, wide, wideLen, mb, mbSize, nullptr, nullptr);
}

#else // !_WIN32 (macOS / POSIX)

#include <unistd.h>
#include <fcntl.h>
#include <sys/stat.h>
#include <cerrno>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <pthread.h>

#include "pal.h"

// ─── File I/O ─────────────────────────────────────────────────────────────────
typedef int PalFileHandle;
#define PAL_INVALID_FILE_HANDLE (-1)

inline PalFileHandle PalCreateFile(const char* path)
{
    return open(path, O_WRONLY | O_CREAT | O_TRUNC, 0644);
}

inline bool PalWriteFile(PalFileHandle h, const void* data, size_t len)
{
    const char* p = static_cast<const char*>(data);
    size_t remaining = len;
    while (remaining > 0)
    {
        ssize_t written = write(h, p, remaining);
        if (written < 0)
        {
            if (errno == EINTR)
                continue;
            return false;
        }
        p += written;
        remaining -= static_cast<size_t>(written);
    }
    return true;
}

inline void PalCloseFile(PalFileHandle h)
{
    if (h != PAL_INVALID_FILE_HANDLE)
        close(h);
}

// ─── Directories ──────────────────────────────────────────────────────────────

inline bool PalCreateDirectory(const char* path)
{
    return mkdir(path, 0755) == 0 || errno == EEXIST;
}

// ─── Process / Thread ID ──────────────────────────────────────────────────────

inline DWORD PalGetCurrentProcessId() { return static_cast<DWORD>(getpid()); }

inline DWORD PalGetCurrentThreadId()
{
#ifdef __APPLE__
    uint64_t tid = 0;
    pthread_threadid_np(nullptr, &tid);
    return static_cast<DWORD>(tid);
#else
    return static_cast<DWORD>(gettid());
#endif
}

// ─── Debug output ─────────────────────────────────────────────────────────────

inline void PalDebugPrint(const char* msg) { fprintf(stderr, "%s", msg); }

// ─── Environment variables ───────────────────────────────────────────────────

inline DWORD PalGetEnvironmentVariable(const char* name, char* buffer, DWORD size)
{
    const char* val = getenv(name);
    if (val == nullptr)
        return 0;
    size_t len = strlen(val);
    if (len >= size)
        return static_cast<DWORD>(len + 1); // needed size
    memcpy(buffer, val, len + 1);
    return static_cast<DWORD>(len);
}

// ─── Wide string conversion ──────────────────────────────────────────────────
// Manual UTF-16 (char16_t) to UTF-8 conversion

inline int PalWideCharToMultiByte(const WCHAR* wide, int wideLen, char* mb, int mbSize)
{
    if (wide == nullptr || wideLen == 0)
        return 0;

    // If wideLen is -1, compute length from null terminator
    if (wideLen < 0)
    {
        wideLen = 0;
        while (wide[wideLen] != 0)
            wideLen++;
    }

    // First pass: calculate required size (or write if mb != nullptr)
    int outLen = 0;
    for (int i = 0; i < wideLen; i++)
    {
        uint32_t cp = static_cast<uint16_t>(wide[i]);

        // Handle surrogate pairs
        if (cp >= 0xD800 && cp <= 0xDBFF && i + 1 < wideLen)
        {
            uint32_t lo = static_cast<uint16_t>(wide[i + 1]);
            if (lo >= 0xDC00 && lo <= 0xDFFF)
            {
                cp = 0x10000 + ((cp - 0xD800) << 10) + (lo - 0xDC00);
                i++;
            }
        }

        int bytes;
        if (cp < 0x80)
            bytes = 1;
        else if (cp < 0x800)
            bytes = 2;
        else if (cp < 0x10000)
            bytes = 3;
        else
            bytes = 4;

        if (mb != nullptr && outLen + bytes <= mbSize)
        {
            if (bytes == 1)
            {
                mb[outLen] = static_cast<char>(cp);
            }
            else if (bytes == 2)
            {
                mb[outLen] = static_cast<char>(0xC0 | (cp >> 6));
                mb[outLen + 1] = static_cast<char>(0x80 | (cp & 0x3F));
            }
            else if (bytes == 3)
            {
                mb[outLen] = static_cast<char>(0xE0 | (cp >> 12));
                mb[outLen + 1] = static_cast<char>(0x80 | ((cp >> 6) & 0x3F));
                mb[outLen + 2] = static_cast<char>(0x80 | (cp & 0x3F));
            }
            else
            {
                mb[outLen] = static_cast<char>(0xF0 | (cp >> 18));
                mb[outLen + 1] = static_cast<char>(0x80 | ((cp >> 12) & 0x3F));
                mb[outLen + 2] = static_cast<char>(0x80 | ((cp >> 6) & 0x3F));
                mb[outLen + 3] = static_cast<char>(0x80 | (cp & 0x3F));
            }
        }
        outLen += bytes;
    }
    return outLen;
}

#endif // !_WIN32
