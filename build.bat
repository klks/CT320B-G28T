@echo off
rem Build the CT320B Label Designer (app + its libraries) into Bin\<Config>.
rem Usage:  build.bat [Debug|Release]      (default: Release)
setlocal
set "ROOT=%~dp0"
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Release"
set "OUTDIR=%ROOT%bin\%CONFIG%"

echo Building CT320B Label Designer (%CONFIG%) -^> %OUTDIR%
dotnet build "%ROOT%src\CT320B.LabelDesigner\CT320B.LabelDesigner.csproj" -c "%CONFIG%" -o "%OUTDIR%" --nologo
set "RC=%ERRORLEVEL%"
echo.
if "%RC%"=="0" echo Build succeeded: %OUTDIR%
if not "%RC%"=="0" echo Build FAILED (exit %RC%).
exit /b %RC%
