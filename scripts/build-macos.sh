#!/bin/bash
set -euo pipefail

# Build the Metreja profiler dylib on macOS using CMake

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PROFILER_DIR="$ROOT_DIR/src/Metreja.Profiler"

echo "Building Metreja.Profiler (macOS ARM64)..."

cd "$PROFILER_DIR"
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build

# Copy to bin/Release/ (matching Windows convention)
mkdir -p "$ROOT_DIR/bin/Release"
cp build/libMetreja.Profiler.dylib "$ROOT_DIR/bin/Release/"

echo ""
echo "Build complete: bin/Release/libMetreja.Profiler.dylib"
