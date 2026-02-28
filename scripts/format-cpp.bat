@echo off
setlocal

REM Format all C++ source files using clang-format
set "CLANG_FORMAT="

REM Try PATH first
where clang-format >nul 2>&1
if %ERRORLEVEL% equ 0 (
    set "CLANG_FORMAT=clang-format"
    goto :found
)

REM Try VS Build Tools
set "CANDIDATE=%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\VC\Tools\Llvm\x64\bin\clang-format.exe"
if exist "%CANDIDATE%" (
    set "CLANG_FORMAT=%CANDIDATE%"
    goto :found
)

set "CANDIDATE=%ProgramFiles%\Microsoft Visual Studio\2022\Community\VC\Tools\Llvm\x64\bin\clang-format.exe"
if exist "%CANDIDATE%" (
    set "CLANG_FORMAT=%CANDIDATE%"
    goto :found
)

echo ERROR: clang-format not found.
exit /b 1

:found
echo Using: %CLANG_FORMAT%
echo.

set "ROOT=%~dp0..\src\Metreja.Profiler"

for %%f in ("%ROOT%\*.cpp" "%ROOT%\*.h") do (
    if exist "%%f" (
        echo Formatting: %%~nxf
        "%CLANG_FORMAT%" -i "%%f"
    )
)

echo.
echo Done. Remember to 'git add' the formatted files.
