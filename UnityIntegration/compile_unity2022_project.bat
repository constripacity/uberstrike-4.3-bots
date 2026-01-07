@echo off
set PROJECT_LIBS=C:\Users\Shadow\Desktop\uber-client-4-3-8-unity_2022_working\References\UnityEngine
set OUTPUT=UberStrikeBots_Unity2022.dll
set CSC="C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"

echo Compiling for Unity 2022 Project...
echo Libs: %PROJECT_LIBS%

%CSC% /target:library /out:%OUTPUT% /langversion:latest /reference:"%PROJECT_LIBS%\UnityEngine.dll" /reference:"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll" /platform:x86 /optimize /debug:pdbonly BotInjector.cs BotController.cs InjectionTester.cs PracticeModeDetector.cs LocalSimulationManager.cs BotTestingHarness.cs VersionDetector.cs BotMetrics.cs ReflectionProbe.cs

if %errorlevel% equ 0 (
    echo.
    echo SUCCESS! Created %OUTPUT%
    echo.
) else (
    echo.
    echo FAILED!
    echo.
    exit /b 1
)