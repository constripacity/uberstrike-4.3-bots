$InjectorPath = "C:\Users\Shadow\Downloads\SharpMonoInjector.Console\smi.exe"
$DllPath = "C:\Users\Shadow\Downloads\uberstrike-4.3-bots\bin\UberStrikeBots.dll"

Write-Host "Injecting UberStrikeBots.dll..." -ForegroundColor Cyan
& $InjectorPath inject -p UberStrike -a $DllPath -n UberStrikeBot -c BotInjector -m Load