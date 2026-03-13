#!/bin/bash
set -euo pipefail

# Format all C++ source files using clang-format

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PROFILER_DIR="$ROOT_DIR/src/Metreja.Profiler"

# Find clang-format
if command -v clang-format &>/dev/null; then
    CLANG_FORMAT="clang-format"
else
    echo "ERROR: clang-format not found. Install via 'brew install clang-format' or Xcode CLT."
    exit 1
fi

echo "Using: $CLANG_FORMAT"
echo ""

# Format .cpp and .h files in profiler source directories
find "$PROFILER_DIR" -maxdepth 1 -name '*.cpp' -o -name '*.h' | while read -r f; do
    echo "Formatting: $(basename "$f")"
    "$CLANG_FORMAT" -i "$f"
done

# Format platform/ directory
if [ -d "$PROFILER_DIR/platform" ]; then
    find "$PROFILER_DIR/platform" -name '*.cpp' -o -name '*.h' | while read -r f; do
        echo "Formatting: platform/$(basename "$f")"
        "$CLANG_FORMAT" -i "$f"
    done
fi

echo ""
echo "Done. Remember to 'git add' the formatted files."
