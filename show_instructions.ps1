Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "   UberStrike Bot Injection Helper" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. DLL Built Successfully:" -ForegroundColor Green
Write-Host "   Path: $PSScriptRoot\bin\UberStrikeBots.dll"
Write-Host ""
Write-Host "2. Instructions:" -ForegroundColor Yellow
Write-Host "   You must use an external Mono Injector (like SharpMonoInjector)."
Write-Host "   We cannot inject directly from this script without the tool."
Write-Host ""
Write-Host "   [Injector Settings]"
Write-Host "   Namespace: UberStrikeBot"
Write-Host "   Class:     BotInjector"
Write-Host "   Method:    Load"
Write-Host ""
Write-Host "   See INJECTION_GUIDE.md for more details."
Write-Host ""
Write-Host "Press Enter to exit..."
Read-Host
