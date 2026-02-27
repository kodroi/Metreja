#pragma once

// Master include for CoreCLR profiling API headers.
// cor.h/corhdr.h must come before corprof.h to provide metadata typedefs
// (mdToken, mdMethodDef, ASSEMBLYMETADATA, etc.)

#include <Windows.h>
#include "corhdr.h"
#include "cor.h"
#include "corprof.h"
