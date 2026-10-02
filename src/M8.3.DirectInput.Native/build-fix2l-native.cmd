@echo off
setlocal
set "SRC=%~1"
set "OUT=%~2"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"

rem FIX2L4: serialize the shared native prerequisite.
rem M8.4.Desktop and M8.5.Desktop are built in parallel by Visual Studio and both
rem publish the same native DLL. The lock prevents collisions in discovery,
rem compilation and final publication while leaving the frozen projects untouched.
set "LOCKDIR=%TEMP%\tot-fix2l-native-build.lock"
set /a LOCKWAIT=0

:acquire_lock
mkdir "%LOCKDIR%" >nul 2>nul
if not errorlevel 1 goto :lock_acquired
set /a LOCKWAIT+=1
if %LOCKWAIT% GEQ 120 goto :lock_timeout
ping 127.0.0.1 -n 2 -w 1000 >nul
goto :acquire_lock

:lock_acquired
rem Another parallel project may have completed the exact shared artifact while we waited.
if exist "%OUT%" goto :success
if not exist "%VSWHERE%" goto :no_vswhere

set "VSROOTFILE=%TEMP%\tot-fix2l-vsroot-%RANDOM%-%RANDOM%.txt"
"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath > "%VSROOTFILE%"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto :vswhere_failed

set "VSROOT="
set /p "VSROOT=" < "%VSROOTFILE%"
del /q "%VSROOTFILE%" >nul 2>nul
if not defined VSROOT goto :no_msvc
if not exist "%VSROOT%\Common7\Tools\VsDevCmd.bat" goto :no_vsdevcmd

call "%VSROOT%\Common7\Tools\VsDevCmd.bat" -no_logo -arch=x64 -host_arch=x64
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto :fail

for %%I in ("%OUT%") do if not exist "%%~dpI" mkdir "%%~dpI"

set "TMPBASE=%TEMP%\tot-fix2l-native-%RANDOM%-%RANDOM%"
set "TMPOBJ=%TMPBASE%.obj"
set "TMPDLL=%TMPBASE%.dll"
cl /nologo /O2 /LD /DWIN32 /Fo:"%TMPOBJ%" /Fe:"%TMPDLL%" "%SRC%" user32.lib /link /NOIMPLIB
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto :compile_failed
copy /y "%TMPDLL%" "%OUT%" >nul
set "RC=%ERRORLEVEL%"
del /q "%TMPOBJ%" "%TMPDLL%" >nul 2>nul
if not "%RC%"=="0" goto :fail

goto :success

:compile_failed
del /q "%TMPOBJ%" "%TMPDLL%" >nul 2>nul
goto :fail

:no_vswhere
echo FIX2L BUILD ERROR: vswhere.exe not found: %VSWHERE%
set "RC=20"
goto :fail

:vswhere_failed
del /q "%VSROOTFILE%" >nul 2>nul
echo FIX2L BUILD ERROR: vswhere.exe failed with ExitCode=%RC%.
goto :fail

:no_msvc
echo FIX2L BUILD ERROR: Visual Studio MSVC x64 tools not found.
set "RC=21"
goto :fail

:no_vsdevcmd
echo FIX2L BUILD ERROR: VsDevCmd.bat not found under: %VSROOT%
set "RC=22"
goto :fail

:lock_timeout
echo FIX2L BUILD ERROR: timed out waiting for native build lock: %LOCKDIR%
exit /b 23

:success
set "RC=0"

:fail
rmdir "%LOCKDIR%" >nul 2>nul
exit /b %RC%
