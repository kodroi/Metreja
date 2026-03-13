#include "include/profiling.h"
#include <new>
#include "Guids.h"
#include "ClassFactory.h"

#ifdef _WIN32

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH: DisableThreadLibraryCalls(hModule); break;
    default: break;
    }
    return TRUE;
}

_Check_return_ STDAPI DllGetClassObject(_In_ REFCLSID rclsid, _In_ REFIID riid, _Outptr_ LPVOID* ppv)

#else // macOS — no DllMain needed, export with visibility

extern "C" __attribute__((visibility("default"))) HRESULT DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID* ppv)

#endif
{
    if (ppv == nullptr)
    {
        return E_POINTER;
    }

    if (rclsid != CLSID_MetrejaProfiler)
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto* factory = new (std::nothrow) ClassFactory();
    if (factory == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    const HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}

#ifdef _WIN32
__control_entrypoint(DllExport) STDAPI DllCanUnloadNow() { return S_FALSE; }
#else
extern "C" __attribute__((visibility("default"))) HRESULT DllCanUnloadNow() { return S_FALSE; }
#endif
