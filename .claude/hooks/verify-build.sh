#!/bin/bash
cd "$CLAUDE_PROJECT_DIR" || exit 0

# Build CLI
if ! dotnet build src/Metreja.Tool/Metreja.Tool.csproj -c Release 2>&1; then
    echo '{"decision": "block", "reason": "CLI build failed. Fix compilation errors before exiting."}'
    exit 0
fi

# Build profiler (macOS only)
if [ "$(uname -s)" = "Darwin" ]; then
    if ! scripts/build-macos.sh 2>&1; then
        echo '{"decision": "block", "reason": "Profiler build failed. Fix C++ compilation errors before exiting."}'
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
