Write-Host "--- PROCESS DIAGNOSTIC ---" -ForegroundColor Cyan
Get-Process | Where-Object { $_.ProcessName -like "*Uber*" } | Format-Table Id, ProcessName, MainWindowTitle, Path -AutoSize

Write-Host "--- INJECTOR CHECK ---" -ForegroundColor Cyan
$Smi = "C:\Users\Shadow\Downloads\SharpMonoInjector.Console\smi.exe"
if (Test-Path $Smi) {
    Write-Host "SMI Path found."
} else {
    Write-Host "SMI Path NOT found." -ForegroundColor Red
}

Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
