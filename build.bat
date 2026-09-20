@echo off
setlocal
cd /d "%~dp0"

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

set "DL=%USERPROFILE%\Downloads"

echo [1/2] Building icon...
"%CSC%" /nologo /target:exe /out:"%TEMP%\MakeIcon.exe" /r:System.Drawing.dll "src\MakeIcon.cs"
if exist "%TEMP%\MakeIcon.exe" (
    "%TEMP%\MakeIcon.exe"
)

echo [2/2] Compiling AURA.exe with embedded resources...
set "ICON_FLAG="
if exist "%USERPROFILE%\.gemini\antigravity\scratch\aura.ico" (
    set "ICON_FLAG=/win32icon:"%USERPROFILE%\.gemini\antigravity\scratch\aura.ico""
)

set "RES_FLAGS=/resource:"%DL%\TIKI TIKI.mp3",music.mp3 /resource:"%DL%\1394671.png",wallpaper.png /resource:"%DL%\1.jpg",1.jpg /resource:"%DL%\2.jpg",2.jpg /resource:"%DL%\3.jpg",3.jpg"

"%CSC%" /nologo /target:winexe %ICON_FLAG% /out:"AURA.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll %RES_FLAGS% "src\Program.cs"

if exist "AURA.exe" (
    echo Successfully compiled AURA.exe!
) else (
    echo Compilation failed.
)
pause
