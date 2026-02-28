@echo off
setlocal enabledelayedexpansion

echo ============================================
echo   Metreja Profiler - End-to-End Demo
echo ============================================
echo.

set "ROOT=%~dp0"
set "CLI=%ROOT%src\Metreja.Tool\bin\Release\net10.0\metreja.exe"
set "DLL=%ROOT%bin\Release\Metreja.Profiler.dll"
set "TESTAPP=%ROOT%test\Metreja.TestApp"
set "OUTPUT_DIR=%ROOT%demo_output"
set "VALIDATOR=%ROOT%test\validate.py"

REM Check prerequisites
if not exist "%CLI%" (
    echo ERROR: CLI not found at %CLI%
    echo Run build.bat first.
    exit /b 1
)
if not exist "%DLL%" (
    echo ERROR: Profiler DLL not found at %DLL%
    echo Run build.bat first.
    exit /b 1
)

REM Clean previous demo output
if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"
mkdir "%OUTPUT_DIR%"

echo [1/7] Initializing session...
for /f "tokens=*" %%i in ('"!CLI!" init --scenario demo-run') do set "SESSION=%%i"

if not defined SESSION (
    echo ERROR: Failed to create session
    exit /b 1
)
echo    Session ID: !SESSION!
echo.

echo [2/7] Configuring filters...
"%CLI%" add include -s %SESSION% --assembly "Metreja.TestApp"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to add include filter
    exit /b 1
)
"%CLI%" add exclude -s %SESSION% --assembly "System.*"
"%CLI%" add exclude -s %SESSION% --assembly "Microsoft.*"
echo    Filters configured.
echo.

echo [3/7] Setting output...
"%CLI%" set output -s %SESSION% "%OUTPUT_DIR%\trace-{sessionId}-{pid}.ndjson"
"%CLI%" set max-events -s %SESSION% 50000
"%CLI%" set compute-deltas -s %SESSION% true
echo    Output configured.
echo.

echo [4/7] Validating configuration...
"%CLI%" validate -s %SESSION%
if %ERRORLEVEL% neq 0 (
    echo ERROR: Configuration validation failed
    exit /b 1
)
echo    Configuration valid.
echo.

echo [5/7] Generating environment script...
"%CLI%" generate-env -s %SESSION% --dll-path "%DLL%" > "%OUTPUT_DIR%\env.bat"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to generate env script
    exit /b 1
)
echo    Environment script written to demo_output\env.bat
echo.

echo [6/7] Building and running test app with profiler...
REM Build the test app
dotnet build "%TESTAPP%\Metreja.TestApp.csproj" -c Release -o "%OUTPUT_DIR%\testapp" >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo ERROR: Test app build failed
    exit /b 1
)

REM Source the env vars and run
call "%OUTPUT_DIR%\env.bat"
echo    Environment variables set:
echo      CORECLR_ENABLE_PROFILING=%CORECLR_ENABLE_PROFILING%
echo      CORECLR_PROFILER=%CORECLR_PROFILER%
echo      CORECLR_PROFILER_PATH=%CORECLR_PROFILER_PATH%
echo.
echo    Running test app...
"%OUTPUT_DIR%\testapp\Metreja.TestApp.exe"
echo.

REM Unset profiler env vars
set "CORECLR_ENABLE_PROFILING="
set "CORECLR_PROFILER="
set "CORECLR_PROFILER_PATH="
set "METREJA_CONFIG="

REM Find the output file
set "NDJSON_FILE="
for %%f in ("%OUTPUT_DIR%\trace-*.ndjson") do set "NDJSON_FILE=%%f"

if not defined NDJSON_FILE (
    echo ERROR: No NDJSON output file found in %OUTPUT_DIR%
    echo The profiler may not have attached successfully.
    exit /b 1
) else (
    echo    Output: !NDJSON_FILE!
    echo.

    REM Show first few lines
    echo    First 5 lines of output:
    echo    ---
    set /a COUNT=0
    for /f "usebackq delims=" %%l in ("!NDJSON_FILE!") do (
        if !COUNT! lss 5 (
            echo    %%l
            set /a COUNT+=1
        )
    )
    echo    ---
    echo.

    echo [7/7] Validating NDJSON output...
    where python >nul 2>&1
    if %ERRORLEVEL% equ 0 (
        python "%VALIDATOR%" "!NDJSON_FILE!"
    ) else (
        where python3 >nul 2>&1
        if %ERRORLEVEL% equ 0 (
            python3 "%VALIDATOR%" "!NDJSON_FILE!"
        ) else (
            echo WARNING: Python not found. Skipping NDJSON validation.
            echo Run manually: python test\validate.py "!NDJSON_FILE!"
        )
    )
)

echo.
echo ============================================
echo   Demo Complete
echo ============================================
echo.
echo Session: %SESSION%
echo Output:  %OUTPUT_DIR%
echo.
echo To clean up: %CLI% clear -s %SESSION%
