@echo off
setlocal

echo === Building Metreja ===
echo.

REM Build C# CLI
echo [1/2] Building Metreja.Cli...
dotnet build src\Metreja.Cli\Metreja.Cli.csproj -c Release
if %ERRORLEVEL% neq 0 (
    echo FAILED: Metreja.Cli build failed
    exit /b 1
)
echo.

REM Build C++ Profiler DLL
echo [2/2] Building Metreja.Profiler...

REM Try vswhere from both Program Files locations
set "VSWHERE="
if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" (
    set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
) else if exist "%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe" (
    set "VSWHERE=%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"
)

if not defined VSWHERE (
    echo WARNING: vswhere not found. Skipping native DLL build.
    echo Install Visual Studio Build Tools with "Desktop development with C++" workload.
    echo CLI was built successfully.
    exit /b 0
)

set "MSBUILD="
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -find MSBuild\**\Bin\MSBuild.exe`) do (
    set "MSBUILD=%%i"
)

if not defined MSBUILD (
    echo WARNING: MSBuild not found. Skipping native DLL build.
    echo Install Visual Studio Build Tools with "Desktop development with C++" workload.
    echo CLI was built successfully.
    exit /b 0
)

set "SOLUTIONDIR=%~dp0"
"%MSBUILD%" src\Metreja.Profiler\Metreja.Profiler.vcxproj /p:Configuration=Release /p:Platform=x64 "/p:SolutionDir=%SOLUTIONDIR%"
if %ERRORLEVEL% neq 0 (
    echo FAILED: Metreja.Profiler build failed
    exit /b 1
)

echo.
echo === Build Complete ===
echo CLI:      src\Metreja.Cli\bin\Release\net10.0\metreja.exe
echo Profiler: bin\Release\Metreja.Profiler.dll
