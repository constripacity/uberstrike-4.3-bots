@echo off
if not exist bin mkdir bin
echo Compiling UberStrikeBots.dll...
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:library /out:bin\UberStrikeBots.dll /reference:GameRefs\UnityEngine.dll /reference:GameRefs\Assembly-CSharp.dll /recurse:UnityIntegration\*.cs
if %errorlevel% neq 0 (
    echo Compilation FAILED!
    exit /b %errorlevel%
)
echo Compilation SUCCESS! Output: bin\UberStrikeBots.dll
