@echo off
setlocal
set "task_host_dir=C:\Program Files\YINYU-GuestTools"
set "task_compiler=%SystemRoot%\Microsoft.NET\Framework64\v2.0.50727\csc.exe"
set "task_assembly=%SystemRoot%\assembly\GAC_MSIL\System.Management.Automation\1.0.0.0__31bf3856ad364e35\System.Management.Automation.dll"
if not exist "%task_compiler%" (
  echo Tested .NET 2 compiler not found. Check this image's framework layout.
  exit /b 1
)
if not exist "%task_assembly%" (
  echo Tested PowerShell 2 SDK assembly not found. Check this image's engine layout.
  exit /b 1
)
if exist "%task_host_dir%\LegacyPowerShellHost.exe" (
  echo Host already exists. Preserve and review it before replacing it.
  exit /b 1
)
if not exist "%task_host_dir%" mkdir "%task_host_dir%"
if errorlevel 1 exit /b 1
"%task_compiler%" /nologo /target:exe /reference:"%task_assembly%" /out:"%task_host_dir%\LegacyPowerShellHost.exe" "%~dp0LegacyPowerShellHost.cs"
if errorlevel 1 exit /b 1
echo Built the optional host. Verify administrator/SYSTEM directory permissions before publishing.
exit /b 0
