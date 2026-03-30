#pragma once

// Platform Abstraction Layer — High-resolution timing

#ifdef _WIN32

#include <Windows.h>

inline void PalQueryPerformanceCounter(long long& ticks)
{
    LARGE_INTEGER counter;
    QueryPerformanceCounter(&counter);
    ticks = counter.QuadPart;
}

inline void PalQueryPerformanceFrequency(long long& freq)
{
    LARGE_INTEGER f;
    QueryPerformanceFrequency(&f);
    freq = f.QuadPart;
}

#else // macOS

#include <mach/mach_time.h>
#include <cstdint>

inline void PalQueryPerformanceCounter(long long& ticks) { ticks = static_cast<long long>(mach_absolute_time()); }

inline void PalQueryPerformanceFrequency(long long& freq)
{
    // On macOS, mach_absolute_time() ticks are in units of timebase.numer/timebase.denom nanoseconds.
    // To convert ticks to nanoseconds: ns = ticks * numer / denom
    // Our GetTimestampNs formula is: ns = (ticks / freq) * 1e9 + ((ticks % freq) * 1e9) / freq
    // So if we set freq such that ticks/freq * 1e9 == ticks * numer / denom,
    // we need freq = 1e9 * denom / numer.
    mach_timebase_info_data_t info;
    mach_timebase_info(&info);
    freq = static_cast<long long>(1000000000ULL * info.denom / info.numer);
}

#endif
