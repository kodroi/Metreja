// Platform Abstraction Layer — Implementation of non-inline PAL functions

#ifndef _WIN32

#include "pal.h"

// All GUID definitions for macOS.
// On Windows, these are defined via initguid.h + DEFINE_GUID in Guids.h.
// On macOS, the #pragma once on pal.h prevents INITGUID from taking effect
// in Guids.h, so we define all GUIDs explicitly here.

// IID_IUnknown {00000000-0000-0000-C000-000000000046}
extern const GUID IID_IUnknown = {0x00000000, 0x0000, 0x0000, {0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46}};

// IID_IClassFactory {00000001-0000-0000-C000-000000000046}
extern const GUID IID_IClassFactory = {0x00000001, 0x0000, 0x0000, {0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46}};

// IID_IMetaDataImport {7DAC8207-D3AE-4c75-9B67-92801A497D44}
extern const GUID IID_IMetaDataImport = {0x7DAC8207, 0xD3AE, 0x4C75, {0x9B, 0x67, 0x92, 0x80, 0x1A, 0x49, 0x7D, 0x44}};

// Metreja Profiler CLSID {7C8F944B-4810-4999-BF98-6A3361185FC2}
extern const GUID CLSID_MetrejaProfiler = {0x7C8F944B, 0x4810, 0x4999, {0xBF, 0x98, 0x6A, 0x33, 0x61, 0x18, 0x5F, 0xC2}};

// ICorProfilerCallback {176FBED1-A55C-4796-98CA-A9DA0EF883E7}
extern const GUID IID_ICorProfilerCallback = {0x176FBED1, 0xA55C, 0x4796, {0x98, 0xCA, 0xA9, 0xDA, 0x0E, 0xF8, 0x83, 0xE7}};

// ICorProfilerCallback2 {8A8CC829-CCF2-49fe-BBAE-0F022228071A}
extern const GUID IID_ICorProfilerCallback2 = {0x8A8CC829, 0xCCF2, 0x49FE, {0xBB, 0xAE, 0x0F, 0x02, 0x22, 0x28, 0x07, 0x1A}};

// ICorProfilerCallback3 {4FD2ED52-7731-4b8d-9469-03D2CC3086C5}
extern const GUID IID_ICorProfilerCallback3 = {0x4FD2ED52, 0x7731, 0x4B8D, {0x94, 0x69, 0x03, 0xD2, 0xCC, 0x30, 0x86, 0xC5}};

// ICorProfilerInfo {28B5557D-3F3F-48b4-90B2-5F9EEA2F6C48}
extern const GUID IID_ICorProfilerInfo = {0x28B5557D, 0x3F3F, 0x48B4, {0x90, 0xB2, 0x5F, 0x9E, 0xEA, 0x2F, 0x6C, 0x48}};

// ICorProfilerInfo2 {CC0935CD-A518-487d-B0BB-A93214E65478}
extern const GUID IID_ICorProfilerInfo2 = {0xCC0935CD, 0xA518, 0x487D, {0xB0, 0xBB, 0xA9, 0x32, 0x14, 0xE6, 0x54, 0x78}};

// ICorProfilerInfo3 {B555ED4F-452A-4E54-8B39-B5360BAD32A0}
extern const GUID IID_ICorProfilerInfo3 = {0xB555ED4F, 0x452A, 0x4E54, {0x8B, 0x39, 0xB5, 0x36, 0x0B, 0xAD, 0x32, 0xA0}};

#endif // !_WIN32
