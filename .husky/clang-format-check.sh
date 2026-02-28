#!/bin/bash
# Husky.Net pre-commit task: check C++ formatting with clang-format

# Locate clang-format
CLANG_FORMAT=$(command -v clang-format 2>/dev/null || true)

if [ -z "$CLANG_FORMAT" ]; then
    CANDIDATES=(
        "/c/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/VC/Tools/Llvm/x64/bin/clang-format.exe"
        "/c/Program Files/Microsoft Visual Studio/2022/Community/VC/Tools/Llvm/x64/bin/clang-format.exe"
        "/c/Program Files/Microsoft Visual Studio/2022/Professional/VC/Tools/Llvm/x64/bin/clang-format.exe"
        "/c/Program Files/Microsoft Visual Studio/2022/Enterprise/VC/Tools/Llvm/x64/bin/clang-format.exe"
    )
    for candidate in "${CANDIDATES[@]}"; do
        if [ -f "$candidate" ]; then
            CLANG_FORMAT="$candidate"
            break
        fi
    done
fi

if [ -z "$CLANG_FORMAT" ]; then
    echo "WARNING: clang-format not found, skipping style check"
    exit 0
fi

# Get staged C++ files
STAGED_CPP=$(git diff --cached --name-only --diff-filter=ACM -- '*.cpp' '*.h' | grep -v 'include/' || true)

if [ -z "$STAGED_CPP" ]; then
    exit 0
fi

FAILED=0
for file in $STAGED_CPP; do
    [ -f "$file" ] || continue

    DIFF=$("$CLANG_FORMAT" --style=file "$file" 2>/dev/null | diff - "$file" || true)
    if [ -n "$DIFF" ]; then
        [ $FAILED -eq 0 ] && echo "clang-format: style violations in staged C++ files:" && echo ""
        echo "  $file"
        FAILED=1
    fi
done

if [ $FAILED -ne 0 ]; then
    echo ""
    echo "Fix: scripts/format-cpp.bat (or run clang-format -i on the files above)"
    echo "Then re-stage with 'git add'."
    exit 1
fi

echo "clang-format: all staged C++ files pass."
