@echo off
echo === UBERSTRIKE BOTS PHASE 1 COMPILATION ===
echo.

:: detected path: C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Data\Managed
set UNITY_BASE=C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Data\Managed
set OUTPUT=UberStrikeBots.dll
set SOURCES=*.cs

echo Compiling Phase 1: Offline Practice Mode Bots...
echo Using Unity Assemblies from: %UNITY_BASE%
echo.

:: Note: compiling against 2022 for an older game is risky. 
:: Ideally, point UNITY_BASE to the game's "UberStrike_Data\Managed" folder.

"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe" /target:library /out:%OUTPUT% ^
    /langversion:latest ^
    /reference:"%UNITY_BASE%\UnityEngine.dll" ^
    /reference:"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll" ^
    /platform:x86 ^
    /optimize ^
    /debug:pdbonly ^
    /nowarn:0169,0649,0414,0618 ^
    /define:OFFLINE_MODE ^
    %SOURCES%

if %errorlevel% equ 0 (
    echo.
    echo ✅ COMPILATION SUCCESSFUL!
    echo DLL: %OUTPUT% (%CD%\%OUTPUT%)
    echo Size: %~z0 bytes
    echo.
    echo Next steps:
    echo 1. Launch UberStrike 4.3
    echo 2. Enter Practice/Offline mode
    echo 3. Inject this DLL
    echo 4. Test bot functionality (F1 to spawn)
    echo.
) else (
    echo.
    echo ❌ COMPILATION FAILED!
    echo Check for missing references or syntax errors.
    echo.
    exit /b 1
)
