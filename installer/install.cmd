@echo off
rem Inventory Manager setup: runs from the folder IExpress unpacked the setup into.
rem Installs .NET Framework 4.8 from the bundled offline installer only when the PC has no .NET 4.5+,
rem then copies the app to Program Files and adds shortcuts and an uninstall entry.
setlocal EnableExtensions
title Inventory Manager Setup
set "SRC=%~dp0"
if defined ProgramW6432 (set "PF=%ProgramW6432%") else (set "PF=%ProgramFiles%")
set "APPDIR=%PF%\Inventory Manager"
set "KEY=HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\InventoryManager"

rem ---- administrator rights are needed for Program Files and .NET ----
rem The setup itself starts without them, so ask Windows (UAC) and wait for the elevated copy:
rem IExpress deletes the unpacked files as soon as this script returns.
net session >nul 2>&1
if not errorlevel 1 goto admin
if /i "%~1"=="elevated" (
    cscript //nologo "%SRC%message.vbs" error "Setup needs administrator rights.|Please sign in with an administrator account and run the setup again."
    exit /b 1
)
for %%I in ("%~f0") do set "SELF=%%~sI"
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $p = Start-Process -FilePath cmd.exe -ArgumentList '/c %SELF% elevated' -Verb RunAs -Wait -PassThru; exit $p.ExitCode } catch { exit 1223 }"
if "%errorlevel%"=="1223" (
    cscript //nologo "%SRC%message.vbs" warn "Setup was cancelled.|Run it again and click Yes when Windows asks for permission."
)
exit /b

:admin

rem ---- the running app cannot be replaced ----
tasklist /FI "IMAGENAME eq InventoryManager.exe" 2>nul | find /I "InventoryManager.exe" >nul
if not errorlevel 1 (
    cscript //nologo "%SRC%message.vbs" warn "Inventory Manager is open.|Please close it, then run the setup again."
    exit /b 1
)

rem ---- .NET Framework 4.5 or later (Release 378389+) ----
set "NETREL=0"
for /f "tokens=3" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul ^| find /I "Release"') do set /a NETREL=%%a
set "REBOOT=0"
if %NETREL% GEQ 378389 goto app

echo.
echo  Installing Microsoft .NET Framework 4.8 (included in this setup).
echo  This can take 5-10 minutes. Please wait...
echo.
rem Offline Windows 7 PCs may miss the root certificate the .NET installer is signed with.
certutil -addstore -f Root "%SRC%MicRooCerAut2011.cer" >nul 2>&1
"%SRC%ndp48-x86-x64-allos-enu.exe" /passive /norestart
set "RC=%errorlevel%"
if "%RC%"=="0" goto app
if "%RC%"=="3010" set "REBOOT=1"
if "%RC%"=="1641" set "REBOOT=1"
if "%REBOOT%"=="1" goto app
cscript //nologo "%SRC%message.vbs" error "Microsoft .NET Framework 4.8 could not be installed (code %RC%).|Windows 7 needs Service Pack 1. Install it from Windows Update, then run this setup again."
exit /b 1

:app
echo  Installing Inventory Manager...
if not exist "%APPDIR%" mkdir "%APPDIR%"
copy /y "%SRC%InventoryManager.exe" "%APPDIR%\InventoryManager.exe" >nul
if errorlevel 1 goto copyfail
copy /y "%SRC%uninstall.cmd" "%APPDIR%\uninstall.cmd" >nul
copy /y "%SRC%shortcuts.vbs" "%APPDIR%\shortcuts.vbs" >nul
copy /y "%SRC%message.vbs" "%APPDIR%\message.vbs" >nul
copy /y "%SRC%README.txt" "%APPDIR%\README.txt" >nul
cscript //nologo "%APPDIR%\shortcuts.vbs" create "%APPDIR%\InventoryManager.exe"

reg add "%KEY%" /v DisplayName /d "Inventory Manager" /f >nul
reg add "%KEY%" /v DisplayVersion /d "__VERSION__" /f >nul
reg add "%KEY%" /v Publisher /d "Tawhidul Islam" /f >nul
reg add "%KEY%" /v URLInfoAbout /d "https://tawhid.playvai.com" /f >nul
reg add "%KEY%" /v DisplayIcon /d "%APPDIR%\InventoryManager.exe" /f >nul
reg add "%KEY%" /v InstallLocation /d "%APPDIR%" /f >nul
reg add "%KEY%" /v UninstallString /d "\"%APPDIR%\uninstall.cmd\"" /f >nul
reg add "%KEY%" /v NoModify /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v NoRepair /t REG_DWORD /d 1 /f >nul

if "%REBOOT%"=="1" (
    cscript //nologo "%SRC%message.vbs" info "Inventory Manager is installed.|Please RESTART the computer once to finish the .NET Framework installation, then open Inventory Manager from the desktop shortcut."
) else (
    cscript //nologo "%SRC%message.vbs" info "Inventory Manager is installed.|Open it from the desktop or the Start menu shortcut."
)
exit /b 0

:copyfail
cscript //nologo "%SRC%message.vbs" error "Could not copy the app to:|%APPDIR%||Close Inventory Manager if it is open and run the setup again."
exit /b 1
