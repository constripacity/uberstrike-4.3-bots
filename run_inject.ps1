$InjectorPath = "C:\Users\Shadow\Downloads\SharpMonoInjector.Console\smi.exe"
$DllPath = "C:\Users\Shadow\Downloads\uberstrike-4.3-bots\UberStrikeBots_Phase5.dll"

Write-Host "Injecting UberStrikeBots_Phase5.dll..." -ForegroundColor Cyan
& $InjectorPath inject -p UberStrike -a $DllPath -n UberStrikeBot -c BotInjector -m Load