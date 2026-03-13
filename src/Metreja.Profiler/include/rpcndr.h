#pragma once

// Stub rpcndr.h for non-Windows platforms
// corprof.h (MIDL-generated) includes "rpcndr.h" unconditionally.
// On Windows, this file is never used — the vcxproj build resolves to the
// Windows SDK rpcndr.h via system include paths.

#ifndef _WIN32

#ifndef __PAL_RPCNDR_H__
#define __PAL_RPCNDR_H__

// Satisfy the version check in corprof.h
#ifndef __RPCNDR_H_VERSION__
#define __RPCNDR_H_VERSION__ 500
#endif

// Types used in MIDL-generated code
typedef unsigned char byte;
typedef unsigned char boolean;

#ifndef __RPCNDR_H__
#define __RPCNDR_H__
#endif

#endif // __PAL_RPCNDR_H__

#endif // !_WIN32
