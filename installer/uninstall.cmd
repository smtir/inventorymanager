@echo off
rem Removes Inventory Manager (program, shortcuts, uninstall entry). Saved data in %AppData% is kept.
setlocal EnableExtensions
set "APPDIR=%~dp0"
set "APPDIR=%APPDIR:~0,-1%"
set "KEY=HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\InventoryManager"

net session >nul 2>&1
if errorlevel 1 (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

tasklist /FI "IMAGENAME eq InventoryManager.exe" 2>nul | find /I "InventoryManager.exe" >nul
if not errorlevel 1 (
    cscript //nologo "%APPDIR%\message.vbs" warn "Inventory Manager is open.|Please close it, then uninstall again."
    exit /b 1
)

cscript //nologo "%APPDIR%\message.vbs" confirm "Remove Inventory Manager from this computer?||Your products, sales and backups are NOT deleted. They stay in your AppData folder and come back if you install again."
if not "%errorlevel%"=="6" exit /b 0

cscript //nologo "%APPDIR%\shortcuts.vbs" remove
reg delete "%KEY%" /f >nul 2>&1
cd /d "%TEMP%"
rem Deletes the folder including this script once it has finished running.
(goto) 2>nul & rd /s /q "%APPDIR%"
