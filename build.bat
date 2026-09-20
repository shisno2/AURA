@echo off
setlocal
cd /d "%~dp0"

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

set "ASSETS_DIR=%~dp0assets"
set "DL=%USERPROFILE%\Downloads"

rem Check icon
set "ICON="
if exist "%ASSETS_DIR%\aura.ico" (
    set "ICON=%ASSETS_DIR%\aura.ico"
) else if exist "%USERPROFILE%\.gemini\antigravity\scratch\aura.ico" (
    set "ICON=%USERPROFILE%\.gemini\antigravity\scratch\aura.ico"
) else (
    echo Building icon...
    "%CSC%" /nologo /target:exe /out:"%TEMP%\MakeIcon.exe" /r:System.Drawing.dll "src\MakeIcon.cs" 2>nul
    if exist "%TEMP%\MakeIcon.exe" "%TEMP%\MakeIcon.exe" 2>nul
    if exist "%USERPROFILE%\.gemini\antigravity\scratch\aura.ico" set "ICON=%USERPROFILE%\.gemini\antigravity\scratch\aura.ico"
)

set "ICON_FLAG="
if defined ICON set "ICON_FLAG=/win32icon:"%ICON%""

rem Locate resources
set "MUSIC="
if exist "%ASSETS_DIR%\music.mp3" (set "MUSIC=%ASSETS_DIR%\music.mp3") else if exist "%DL%\TIKI TIKI.mp3" (set "MUSIC=%DL%\TIKI TIKI.mp3")

set "WALL="
if exist "%ASSETS_DIR%\wallpaper.png" (set "WALL=%ASSETS_DIR%\wallpaper.png") else if exist "%DL%\1394671.png" (set "WALL=%DL%\1394671.png")

set "IMG1="
if exist "%ASSETS_DIR%\1.png" (set "IMG1=%ASSETS_DIR%\1.png") else if exist "%ASSETS_DIR%\1.jpg" (set "IMG1=%ASSETS_DIR%\1.jpg") else if exist "%DL%\1.png" (set "IMG1=%DL%\1.png") else if exist "%DL%\1.jpg" (set "IMG1=%DL%\1.jpg")

set "IMG2="
if exist "%ASSETS_DIR%\2.png" (set "IMG2=%ASSETS_DIR%\2.png") else if exist "%ASSETS_DIR%\2.jpg" (set "IMG2=%ASSETS_DIR%\2.jpg") else if exist "%DL%\2.png" (set "IMG2=%DL%\2.png") else if exist "%DL%\2.jpg" (set "IMG2=%DL%\2.jpg")

set "IMG3="
if exist "%ASSETS_DIR%\3.png" (set "IMG3=%ASSETS_DIR%\3.png") else if exist "%ASSETS_DIR%\3.jpg" (set "IMG3=%ASSETS_DIR%\3.jpg") else if exist "%DL%\3.png" (set "IMG3=%DL%\3.png") else if exist "%DL%\3.jpg" (set "IMG3=%DL%\3.jpg")

set "RES_FLAGS="
if defined MUSIC set "RES_FLAGS=%RES_FLAGS% /resource:"%MUSIC%",music.mp3"
if defined WALL set "RES_FLAGS=%RES_FLAGS% /resource:"%WALL%",wallpaper.png"
if defined IMG1 set "RES_FLAGS=%RES_FLAGS% /resource:"%IMG1%",1.png /resource:"%IMG1%",1.jpg"
if defined IMG2 set "RES_FLAGS=%RES_FLAGS% /resource:"%IMG2%",2.png /resource:"%IMG2%",2.jpg"
if defined IMG3 set "RES_FLAGS=%RES_FLAGS% /resource:"%IMG3%",3.png /resource:"%IMG3%",3.jpg"

echo ========================================================
echo [1/2] Compiling AURA.exe (Full Version)...
echo ========================================================
"%CSC%" /nologo /target:winexe %ICON_FLAG% /out:"AURA.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll %RES_FLAGS% "src\Program.cs"

if exist "AURA.exe" (
    echo [OK] AURA.exe successfully compiled!
) else (
    echo [ERROR] Failed to compile AURA.exe.
)

echo.
echo ========================================================
echo [2/2] Compiling AURA_Lite.exe (Safe/Lite Version)...
echo (No wallpaper changes, No desktop files created)
echo ========================================================
"%CSC%" /nologo /target:winexe %ICON_FLAG% /define:LITE /out:"AURA_Lite.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll %RES_FLAGS% "src\Program.cs"

if exist "AURA_Lite.exe" (
    echo [OK] AURA_Lite.exe successfully compiled!
) else (
    echo [ERROR] Failed to compile AURA_Lite.exe.
)
echo.
echo Done!
