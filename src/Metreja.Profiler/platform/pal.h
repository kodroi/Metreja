#pragma once

// Platform Abstraction Layer — Core types and macros
// On Windows: pass-through to Windows.h + unknwn.h
// On macOS: provides equivalent types for COM/profiler compilation

#ifdef _WIN32

#include <Windows.h>
#include <unknwn.h>

#else // !_WIN32 (macOS / POSIX)

#include <cstdint>
#include <cstring>

// ─── Base types ───────────────────────────────────────────────────────────────

typedef int32_t HRESULT;
typedef uint32_t DWORD;
typedef unsigned long ULONG;
typedef long LONG;
typedef int BOOL;
typedef char16_t WCHAR;
typedef uintptr_t UINT_PTR;
typedef intptr_t INT_PTR;
typedef void* LPVOID;
typedef uint8_t BYTE;
typedef unsigned int UINT;
typedef uint16_t USHORT;
typedef uint16_t WORD;
typedef uint64_t ULONG64;
typedef int64_t LONG64;
typedef uint64_t ULONGLONG;
typedef uint32_t ULONG32;
typedef int32_t LONG32;
typedef uint64_t UINT64;
typedef int64_t INT64;
typedef uint32_t UINT32;
typedef int32_t INT32;
typedef uint16_t UINT16;
typedef int16_t INT16;
typedef uint8_t UINT8;
typedef int8_t INT8;
typedef char16_t OLECHAR;
typedef OLECHAR* LPOLESTR;
typedef const OLECHAR* LPCOLESTR;
typedef char* LPSTR;
typedef const char* LPCSTR;
typedef WCHAR* LPWSTR;
typedef const WCHAR* LPCWSTR;
typedef void* HANDLE;
typedef void* HMODULE;
typedef void* HINSTANCE;

#ifndef TRUE
#define TRUE 1
#endif

#ifndef FALSE
#define FALSE 0
#endif

// ─── HRESULT constants ───────────────────────────────────────────────────────

#define S_OK ((HRESULT)0)
#define S_FALSE ((HRESULT)1)
#define E_FAIL ((HRESULT)0x80004005L)
#define E_POINTER ((HRESULT)0x80004003L)
#define E_OUTOFMEMORY ((HRESULT)0x8007000EL)
#define E_NOINTERFACE ((HRESULT)0x80004002L)
#define E_NOTIMPL ((HRESULT)0x80004001L)
#define E_INVALIDARG ((HRESULT)0x80070057L)

#define CLASS_E_CLASSNOTAVAILABLE ((HRESULT)0x80040111L)
#define CLASS_E_NOAGGREGATION ((HRESULT)0x80040110L)

#define SEVERITY_SUCCESS 0
#define SEVERITY_ERROR 1

#define FACILITY_URT 0x13
#define FACILITY_NULL 0
#define FACILITY_ITF 4
#define FACILITY_WIN32 7

#define SUCCEEDED(hr) (((HRESULT)(hr)) >= 0)
#define FAILED(hr) (((HRESULT)(hr)) < 0)

#define MAKE_HRESULT(sev, fac, code)                                                                                   \
    ((HRESULT)(((unsigned long)(sev) << 31) | ((unsigned long)(fac) << 16) | ((unsigned long)(code))))

#define HRESULT_FROM_WIN32(x)                                                                                          \
    ((HRESULT)(x) <= 0 ? ((HRESULT)(x)) : ((HRESULT)(((x) & 0x0000FFFF) | (FACILITY_WIN32 << 16) | 0x80000000)))

// winerror.h constants used by corerror.h
#ifndef ERROR_ALREADY_EXISTS
#define ERROR_ALREADY_EXISTS 183L
#endif

// ─── GUID ─────────────────────────────────────────────────────────────────────

#ifndef GUID_DEFINED
#define GUID_DEFINED

typedef struct _GUID
{
    uint32_t Data1;
    uint16_t Data2;
    uint16_t Data3;
    uint8_t Data4[8];
} GUID;

#endif // GUID_DEFINED

typedef GUID IID;
typedef GUID CLSID;
typedef const GUID& REFGUID;
typedef const IID& REFIID;
typedef const CLSID& REFCLSID;

inline bool operator==(const GUID& a, const GUID& b) { return memcmp(&a, &b, sizeof(GUID)) == 0; }

inline bool operator!=(const GUID& a, const GUID& b) { return !(a == b); }

#ifndef DEFINE_GUID
#ifdef INITGUID
#define DEFINE_GUID(name, l, w1, w2, b1, b2, b3, b4, b5, b6, b7, b8)                                                   \
    extern const GUID name = {l, w1, w2, {b1, b2, b3, b4, b5, b6, b7, b8}}
#else
#define DEFINE_GUID(name, l, w1, w2, b1, b2, b3, b4, b5, b6, b7, b8) extern const GUID name
#endif
#endif

#ifndef EXTERN_GUID
#ifdef INITGUID
#define EXTERN_GUID(name, l, w1, w2, b1, b2, b3, b4, b5, b6, b7, b8)                                                   \
    extern const GUID name = {l, w1, w2, {b1, b2, b3, b4, b5, b6, b7, b8}}
#else
#define EXTERN_GUID(name, l, w1, w2, b1, b2, b3, b4, b5, b6, b7, b8) extern const GUID name
#endif
#endif

#ifdef __cplusplus
#define EXTERN_C extern "C"
#else
#define EXTERN_C extern
#endif

#define EXTERN_C_START                                                                                                 \
    extern "C"                                                                                                         \
    {
#define EXTERN_C_END }

// ─── COM interfaces ──────────────────────────────────────────────────────────

// Forward-declare IID constants
EXTERN_C const IID IID_IUnknown;
EXTERN_C const IID IID_IClassFactory;

struct IUnknown
{
    virtual HRESULT QueryInterface(REFIID riid, void** ppvObject) = 0;
    virtual ULONG AddRef() = 0;
    virtual ULONG Release() = 0;
    // No virtual destructor — COM vtable must have exactly 3 slots.
    // Implementing classes use delete this in Release().
};

struct IClassFactory : public IUnknown
{
    virtual HRESULT CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppvObject) = 0;
    virtual HRESULT LockServer(BOOL fLock) = 0;
};

// ─── Calling convention / decoration stubs ────────────────────────────────────

#define STDMETHODCALLTYPE
#define STDAPI extern "C" HRESULT
#define STDAPI_(type) extern "C" type
#define __stdcall
#define APIENTRY
#define WINAPI
#define CALLBACK

#define __control_entrypoint(x)

#define DECLSPEC_UUID(x)
#define DECLSPEC_NOVTABLE
#define DECLSPEC_XFGVIRT(base, func)
#define MIDL_INTERFACE(x) class DECLSPEC_NOVTABLE

// ─── SAL annotation stubs ────────────────────────────────────────────────────

#ifndef _In_
#define _In_
#define _In_opt_
#define _Out_
#define _Out_opt_
#define _Outptr_
#define _Outptr_opt_
#define _Outptr_result_maybenull_
#define _Inout_
#define _Inout_opt_
#define _In_reads_(x)
#define _In_reads_opt_(x)
#define _In_reads_bytes_(x)
#define _Out_writes_(x)
#define _Out_writes_opt_(x)
#define _Out_writes_bytes_(x)
#define _Out_writes_to_(x, y)
#define _Out_writes_to_opt_(x, y)
#define _Out_writes_bytes_to_(x, y)
#define _Out_writes_bytes_to_opt_(x, y)
#define _Out_writes_all_(x)
#define _Out_writes_all_opt_(x)
#define _In_z_
#define _Out_z_cap_(x)
#define _Check_return_
#define _Ret_maybenull_
#define _Deref_out_range_(x, y)
#define _Field_range_(x, y)
#define _Pre_maybenull_
#define _Post_invalid_
#endif

// ─── Misc Windows macros/types ───────────────────────────────────────────────

#define GENERIC_WRITE 0x40000000
#define GENERIC_READ 0x80000000
#define FILE_SHARE_READ 0x00000001
#define CREATE_ALWAYS 2
#define FILE_ATTRIBUTE_NORMAL 0x00000080

#define INVALID_HANDLE_VALUE ((HANDLE)(intptr_t)-1)

#define INFINITE 0xFFFFFFFF
#define WAIT_OBJECT_0 0
#define WAIT_TIMEOUT 258
#define WAIT_FAILED 0xFFFFFFFF

typedef int64_t LARGE_INTEGER_QUADPART;
typedef union _LARGE_INTEGER
{
    struct
    {
        uint32_t LowPart;
        int32_t HighPart;
    };
    int64_t QuadPart;
} LARGE_INTEGER;

// COM helper macros used in MIDL-generated code
#ifndef interface
#define interface class
#endif

#ifndef __RPC_FAR
#define __RPC_FAR
#endif

// Code page constants
#define CP_UTF8 65001

// Alignment macros
#ifndef UNALIGNED
#define UNALIGNED
#endif

// COM interface declaration macros (used in cor.h metadata interfaces)
// Use struct (not class) so members are public by default
#ifndef DECLARE_INTERFACE
#define DECLARE_INTERFACE(iface) struct DECLSPEC_NOVTABLE iface
#define DECLARE_INTERFACE_(iface, base) struct DECLSPEC_NOVTABLE iface : public base
#endif

// STDMETHOD macros used in DECLARE_INTERFACE blocks
#ifndef STDMETHOD
#define STDMETHOD(method) virtual HRESULT method
#define STDMETHOD_(type, method) virtual type method
#endif

#ifndef STDMETHODIMP
#define STDMETHODIMP HRESULT
#define STDMETHODIMP_(type) type
#endif

#ifndef PURE
#define PURE = 0
#endif

#ifndef THIS_
#define THIS_
#define THIS
#endif

// __uuidof stub (not needed at runtime, but some headers reference it)
// We don't support __uuidof on non-MSVC; GUIDs are passed explicitly.

// STDAPI variants
#ifndef STDAPICALLTYPE
#define STDAPICALLTYPE
#endif

// CoTaskMemAlloc/Free stubs (metadata headers reference these)
#include <cstdlib>
inline void* CoTaskMemAlloc(size_t size) { return malloc(size); }
inline void CoTaskMemFree(void* p) { free(p); }

// Security attributes (not used but referenced in type signatures)
typedef void* LPSECURITY_ATTRIBUTES;

// Pointer types
typedef void* PVOID;
typedef const GUID* LPCGUID;
typedef GUID* LPGUID;

// Additional types referenced by cor.h metadata interfaces
typedef const void* LPCVOID;
typedef struct _VARIANT
{
    int vt; /* stub */
} VARIANT;

// IStream / ITypeInfo stubs — metadata interfaces reference these
#ifndef __IStream_FWD_DEFINED__
#define __IStream_FWD_DEFINED__
typedef struct IStream IStream;
#endif

#ifndef __ITypeInfo_FWD_DEFINED__
#define __ITypeInfo_FWD_DEFINED__
typedef struct ITypeInfo ITypeInfo;
#endif

// FORCEINLINE
#ifndef FORCEINLINE
#define FORCEINLINE inline __attribute__((always_inline))
#endif

// SIZE_T / PSIZE_T
#ifndef _SIZE_T_DEFINED
typedef size_t SIZE_T;
#define _SIZE_T_DEFINED
#endif

// LCID (locale ID)
typedef DWORD LCID;

// HCORENUM (metadata enumeration handle)
typedef void* HCORENUM;

#endif // !_WIN32
