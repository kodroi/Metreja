#pragma once

// Master include for CoreCLR profiling API headers.
// cor.h/corhdr.h must come before corprof.h to provide metadata typedefs
// (mdToken, mdMethodDef, ASSEMBLYMETADATA, etc.)

#ifdef _WIN32
#include <Windows.h>
#else
#define COM_NO_WINDOWS_H
#include "../platform/pal.h"
#endif

#include "corhdr.h"
#include "cor.h"
#include "corprof.h"
