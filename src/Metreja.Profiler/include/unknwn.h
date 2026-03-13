#pragma once

// Stub unknwn.h for non-Windows platforms
// corprof.h includes "unknwn.h" for IUnknown.
// On Windows, this file is never used — the vcxproj build resolves to the
// Windows SDK unknwn.h via system include paths.
// On macOS, IUnknown is already defined in platform/pal.h.

#ifndef _WIN32
// IUnknown is already defined in pal.h — nothing to do here.
#endif
