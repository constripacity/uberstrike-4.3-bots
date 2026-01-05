<#
.SYNOPSIS
One-click validation for UberStrike 4.3 Bot Framework
.DESCRIPTION
Runs all validation tests and creates a summary report
#>

Write-Host "=== UBERSTRIKE BOT FRAMEWORK VALIDATION ===" -ForegroundColor Cyan

# 1. Build check
Write-Host "1. Building project..." -ForegroundColor Yellow
dotnet build
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Build successful" -ForegroundColor Green

# 2. Determinism check
Write-Host "2. Testing determinism (5 seeds)..." -ForegroundColor Yellow
.\scripts\validate-determinism.ps1
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Determinism failed" -ForegroundColor Red
    exit 2
}
Write-Host "✅ Determinism verified" -ForegroundColor Green

# 3. Performance benchmark
Write-Host "3. Running performance benchmark..." -ForegroundColor Yellow
.\scripts\benchmark.ps1 | Out-File "benchmark-results.csv"
Write-Host "✅ Benchmark complete" -ForegroundColor Green

# 4. Generate final report
Write-Host "4. Generating final report..." -ForegroundColor Yellow
$report = @{
    timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    validation = "PASS"
    checksum = (Get-FileHash "run-summary.json" -Algorithm MD5).Hash
    performance = Import-Csv "benchmark-results.csv" | ForEach-Object {
        @{
            scenario = $_.Scenario
            time_seconds = $_.Time
            memory_mb = $_.Memory
        }
    }
} | ConvertTo-Json -Depth 3

$report | Out-File "final-validation-summary.json"

Write-Host "=== VALIDATION COMPLETE ===" -ForegroundColor Cyan
Write-Host "Results saved to: final-validation-summary.json" -ForegroundColor Green
