#include <Windows.h>
#include <new>
#include "Guids.h"
#include "ClassFactory.h"

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH: DisableThreadLibraryCalls(hModule); break;
    case DLL_PROCESS_DETACH: break;
    }
    return TRUE;
}

_Check_return_ STDAPI DllGetClassObject(_In_ REFCLSID rclsid, _In_ REFIID riid, _Outptr_ LPVOID* ppv)
{
    if (ppv == nullptr)
        return E_POINTER;

    if (rclsid != CLSID_MetrejaProfiler)
        return CLASS_E_CLASSNOTAVAILABLE;

    auto* factory = new (std::nothrow) ClassFactory();
    if (factory == nullptr)
        return E_OUTOFMEMORY;

    HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}

__control_entrypoint(DllExport) STDAPI DllCanUnloadNow() { return S_FALSE; }
