@echo off
setlocal
cd /d "%~dp0.."
dotnet build win-fs-tools.csproj -c Release -t:Rebuild
if errorlevel 1 exit /b %errorlevel%
echo.
echo build complete: bin\Release\net8.0-windows\win-fs-tools.exe
