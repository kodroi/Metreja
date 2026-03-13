#pragma once

// Stub rpc.h for non-Windows platforms
// corprof.h (MIDL-generated) includes "rpc.h" unconditionally.
// On Windows, this file is never used — the vcxproj build resolves to the
// Windows SDK rpc.h via system include paths (which take precedence).
// On macOS (CMake build), we provide minimal type definitions.

#ifndef _WIN32

#ifndef __PAL_RPC_H__
#define __PAL_RPC_H__

typedef void* RPC_IF_HANDLE;
typedef void* handle_t;

#endif // __PAL_RPC_H__

#endif // !_WIN32
