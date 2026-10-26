@echo off
rem Builds bin\FileDateChanger.exe with the C# compiler built into Windows (.NET Framework 4.x)
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework 4 compiler not found.
  pause
  exit /b 1
)

if not exist bin mkdir bin

"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:bin\FileDateChanger.exe ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  /r:System.Xml.Linq.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  src\FileDateChanger.cs

if errorlevel 1 (
  echo.
  echo BUILD FAILED
  pause
  exit /b 1
)

echo.
echo OK: bin\FileDateChanger.exe
start "" bin\FileDateChanger.exe
timeout /t 2 >nul
