@echo off
setlocal

echo === Packaging Metreja as .NET Tool ===
echo.

REM Build everything first
call "%~dp0build.bat"
if %ERRORLEVEL% neq 0 (
    echo FAILED: Build failed
    exit /b 1
)
echo.

REM Pack the CLI as a .NET tool
echo [Pack] Creating NuGet package...
dotnet pack src\Metreja.Cli\Metreja.Cli.csproj -c Release "/p:SolutionDir=%~dp0"
if %ERRORLEVEL% neq 0 (
    echo FAILED: dotnet pack failed
    exit /b 1
)
echo.

echo === Package Created ===
echo.
echo Install globally:
echo   dotnet tool install -g --add-source src\Metreja.Cli\bin\Release Metreja
echo.
echo Uninstall:
echo   dotnet tool uninstall -g Metreja
echo.
