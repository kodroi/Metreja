#!/bin/bash
cd "$CLAUDE_PROJECT_DIR" || exit 0

# Build CLI
if ! dotnet build src/Metreja.Tool/Metreja.Tool.csproj -c Release 2>&1; then
    echo '{"decision": "block", "reason": "CLI build failed. Fix compilation errors before exiting."}'
    exit 0
fi

# Build profiler (platform-specific)
OS="$(uname -s)"
if [ "$OS" = "Darwin" ]; then
    if ! scripts/build-macos.sh 2>&1; then
        echo '{"decision": "block", "reason": "Profiler build (macOS) failed. Fix C++ compilation errors before exiting."}'
        exit 0
    fi
elif [ "$OS" = "Linux" ] || [ -n "$MSYSTEM" ] || [ -n "$OS" ] && command -v msbuild >/dev/null 2>&1; then
    if ! msbuild src/Metreja.Profiler/Metreja.Profiler.vcxproj /p:Configuration=Release /p:Platform=x64 "/p:SolutionDir=$CLAUDE_PROJECT_DIR\\" 2>&1; then
        echo '{"decision": "block", "reason": "Profiler build (Windows) failed. Fix C++ compilation errors before exiting."}'
        exit 0
    fi
fi

# Run integration tests
if ! dotnet test test/Metreja.IntegrationTests/Metreja.IntegrationTests.csproj -c Release 2>&1; then
    echo '{"decision": "block", "reason": "Integration tests failed. Fix test failures before exiting."}'
    exit 0
fi

# All passed — allow stop
exit 0
