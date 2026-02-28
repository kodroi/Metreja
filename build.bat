@echo off
setlocal

echo === Building Metreja ===
echo.

REM Build C# CLI
echo [1/4] Building Metreja.Tool...
dotnet build src\Metreja.Tool\Metreja.Tool.csproj -c Release
if %ERRORLEVEL% neq 0 (
    echo FAILED: Metreja.Tool build failed
    exit /b 1
)
echo.

REM Build C++ Profiler DLL
echo [2/4] Building Metreja.Profiler...

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

REM Copy skill to root Claude Code directory
echo [3/4] Installing Claude Code skill...
xcopy /Y /I /E skills\metreja-profiler "%USERPROFILE%\.claude\skills\metreja-profiler" >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo FAILED: Could not copy skill to %USERPROFILE%\.claude\skills\metreja-profiler
    exit /b 1
)
echo Skill installed to %USERPROFILE%\.claude\skills\metreja-profiler
echo.

REM Always update global .NET tool
echo [4/4] Updating metreja CLI tool...
dotnet pack src\Metreja.Tool\Metreja.Tool.csproj -c Release >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo FAILED: dotnet pack failed
    exit /b 1
)
dotnet tool list -g 2>nul | findstr /I "metreja" >nul 2>&1
if %ERRORLEVEL% equ 0 (
    dotnet tool uninstall -g Metreja.Tool >nul 2>&1
)
dotnet tool install -g --add-source src\Metreja.Tool\bin\Release Metreja.Tool >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo FAILED: Could not install metreja global tool
    exit /b 1
)
echo metreja CLI tool updated globally

echo.
echo === Build Complete ===
echo CLI:      src\Metreja.Tool\bin\Release\net10.0\metreja.exe
echo Profiler: bin\Release\Metreja.Profiler.dll
echo Skill:    %USERPROFILE%\.claude\skills\metreja-profiler\
echo Tool:     metreja (global dotnet tool)
