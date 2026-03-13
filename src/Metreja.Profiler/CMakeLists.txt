cmake_minimum_required(VERSION 3.20)
project(Metreja.Profiler LANGUAGES CXX ASM)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Collect C++ source files
set(PROFILER_SOURCES
    CallStack.cpp
    ClassFactory.cpp
    ConfigReader.cpp
    dllmain.cpp
    MethodCache.cpp
    NdjsonWriter.cpp
    Profiler.cpp
    StatsAggregator.cpp
    platform/pal.cpp
)

# Platform-specific assembly
if(CMAKE_SYSTEM_PROCESSOR STREQUAL "arm64" OR CMAKE_SYSTEM_PROCESSOR STREQUAL "aarch64")
    list(APPEND PROFILER_SOURCES arm64/asmhelpers.S)
endif()

add_library(Metreja.Profiler SHARED ${PROFILER_SOURCES})

# Include directories
target_include_directories(Metreja.Profiler PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}/include
    ${CMAKE_CURRENT_SOURCE_DIR}/vendor
    ${CMAKE_CURRENT_SOURCE_DIR}/platform
)

# macOS-specific settings
if(APPLE)
    # Define COM_NO_WINDOWS_H so corprof.h doesn't try to include windows.h/ole2.h
    target_compile_definitions(Metreja.Profiler PRIVATE COM_NO_WINDOWS_H)

    # Link pthread (usually implicit on macOS but be explicit)
    find_package(Threads REQUIRED)
    target_link_libraries(Metreja.Profiler PRIVATE Threads::Threads)

    # Export only DllGetClassObject and DllCanUnloadNow
    set_target_properties(Metreja.Profiler PROPERTIES
        LINK_FLAGS "-exported_symbols_list ${CMAKE_CURRENT_SOURCE_DIR}/platform/exports.sym"
    )

    # Output as dylib (default on macOS)
    set_target_properties(Metreja.Profiler PROPERTIES
        PREFIX "lib"
        SUFFIX ".dylib"
    )

    # Ad-hoc code signing (required for ARM64 macOS)
    add_custom_command(TARGET Metreja.Profiler POST_BUILD
        COMMAND codesign --force --sign - $<TARGET_FILE:Metreja.Profiler>
        COMMENT "Ad-hoc code signing libMetreja.Profiler.dylib"
    )
endif()
