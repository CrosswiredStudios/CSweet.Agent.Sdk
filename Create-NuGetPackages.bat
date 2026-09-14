@echo off
setlocal EnableExtensions

pushd "%~dp0" || exit /b 1

set "SOLUTION=CSweetAgentSdk.slnx"
set "PACKAGE_PROJECT=src\CSweet.Agent.SDK\CSweet.Agent.SDK.csproj"
set "PACKAGE_VERSION=%~1"
set "OUTPUT_ROOT=%~2"
set "DOTNET=dotnet"
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"

if not defined PACKAGE_VERSION (
    for /f "delims=" %%V in ('""%DOTNET%" msbuild "%PACKAGE_PROJECT%" -nologo -getProperty:Version"') do set "PACKAGE_VERSION=%%V"
)

if not defined PACKAGE_VERSION (
    echo Unable to read the package version from %PACKAGE_PROJECT%.
    goto :failed
)

if not defined OUTPUT_ROOT set "OUTPUT_ROOT=artifacts\packages"
set "OUTPUT_DIRECTORY=%OUTPUT_ROOT%\%PACKAGE_VERSION%"
for %%I in ("%OUTPUT_DIRECTORY%") do set "OUTPUT_DIRECTORY=%%~fI"

set "BUILD_PROPERTIES=-p:CSweetAgentSdkPackageVersion=%PACKAGE_VERSION% -p:UseLocalCSweetWorkManagementContracts=false"

echo Package version: %PACKAGE_VERSION%

if not exist "%OUTPUT_DIRECTORY%" (
    mkdir "%OUTPUT_DIRECTORY%" || goto :failed
)

echo.
echo Restoring dependencies...
"%DOTNET%" restore "%SOLUTION%" %BUILD_PROPERTIES%
if errorlevel 1 goto :failed

echo.
echo Running tests...
"%DOTNET%" test "%SOLUTION%" -c Release --no-restore %BUILD_PROPERTIES%
if errorlevel 1 goto :failed

echo.
echo Creating NuGet packages in "%OUTPUT_DIRECTORY%"...
"%DOTNET%" pack "%PACKAGE_PROJECT%" -c Release --no-restore %BUILD_PROPERTIES% -o "%OUTPUT_DIRECTORY%"
if errorlevel 1 goto :failed

echo.
echo NuGet packages created successfully:
for %%P in ("%OUTPUT_DIRECTORY%\*.nupkg" "%OUTPUT_DIRECTORY%\*.snupkg") do (
    if exist "%%~fP" echo   %%~nxP
)

popd
exit /b 0

:failed
set "EXIT_CODE=%ERRORLEVEL%"
if "%EXIT_CODE%"=="0" set "EXIT_CODE=1"
echo.
echo Package creation failed with exit code %EXIT_CODE%.
popd
exit /b %EXIT_CODE%
