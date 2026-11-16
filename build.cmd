@echo off
rem Builds ClipboardTyper.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No SDK, NuGet or installer needed.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found. .NET Framework 4.x is required.
  exit /b 1
)

set "ICON="
if exist "app.ico" set "ICON=/win32icon:app.ico"

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu ^
  /win32manifest:app.manifest %ICON% /out:ClipboardTyper.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo Build FAILED.
  exit /b 1
)
echo Built ClipboardTyper.exe
