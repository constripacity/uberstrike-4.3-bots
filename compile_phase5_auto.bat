@echo off
set GAME_MANAGED_PATH="C:\Users\Shadow\Downloads\WindowsStandalone\UberStrike_Data\Managed"
set OUTPUT=UberStrikeBots_Phase5.dll
:: Switch to Legacy Compiler to avoid Array.Empty optimization issues
set CSC="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

echo ==================================================
echo COMPILING FOR UBERSTRIKE 4.3 (Legacy CSC)
echo Target Path: %GAME_MANAGED_PATH%
echo Compiler: %CSC%
echo ==================================================

cd UnityIntegration

%CSC% /target:library /out:..\%OUTPUT% ^
    /reference:%GAME_MANAGED_PATH%\UnityEngine.dll ^
    /platform:x86 ^
    /optimize- ^
    /debug+ ^
    /define:UNITY_2017 ^
    BotInjector.cs ^
    BotController.cs ^
    InjectionTester.cs ^
    PracticeModeDetector.cs ^
    LocalSimulationManager.cs ^
    VersionDetector.cs ^
    InputEmulator.cs ^
    BotMetrics.cs ^
    ReflectionProbe.cs ^
    BotTestingHarness.cs ^
    GameFacade.cs ^
    AvatarInvestigator.cs ^
    CharacterHitAreaProbe.cs

if %errorlevel% equ 0 (
    echo.
    echo ✅ SUCCESS! Created: %OUTPUT%
) else (
    echo.
    echo ❌ COMPILATION FAILED!
)
