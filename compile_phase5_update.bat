@echo off
set UNITY_LIBS="C:\Program Files\Unity\Hub\Editor\5.6.7f1\Editor\Data\Mono\lib\mono\2.0"
set GAME_MANAGED_PATH="C:\Users\Shadow\Desktop\Photon-research\UberClient-4.8.3\UberStrike_Data\Managed"
set OUTPUT=UberStrikeBots_Phase5.dll
set CSC="C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"

echo Compiling %OUTPUT%...
cd UnityIntegration

%CSC% /noconfig /target:library /out:..\%OUTPUT% ^
    /nostdlib ^
    /reference:%UNITY_LIBS%\mscorlib.dll ^
    /reference:%UNITY_LIBS%\System.dll ^
    /reference:%UNITY_LIBS%\System.Core.dll ^
    /reference:%GAME_MANAGED_PATH%\UnityEngine.dll ^
    /reference:%GAME_MANAGED_PATH%\Assembly-CSharp.dll ^
    /platform:x86 ^
    /optimize- ^
    /debug+ ^
    /langversion:3 ^
    /define:UNITY_2017 ^
    *.cs

if %errorlevel% equ 0 (
    echo SUCCESS! Created: %OUTPUT%
) else (
    echo COMPILATION FAILED!
    exit /b 1
)
