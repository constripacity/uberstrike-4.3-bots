$scenario = "flipping_regression"
$seeds = @(42, 777, 12345, 999, 555)
$runs = 3
$project = "BotRunner"
$pass = $true

foreach ($seed in $seeds) {
    Write-Host "Validating seed ${seed}..."
    $checksums = @()
    for ($i = 1; $i -le $runs; $i++) {
        dotnet run --project $project -- --scenario $scenario --seed $seed --quiet | Out-Null
        if (-not (Test-Path run-summary.json)) {
            Write-Error "run-summary.json missing after run $i for seed $seed"
            $pass = $false
            break
        }
        # Extract ChecksumMd5 using Python
        $checksum = python -c "import json; print(json.load(open('run-summary.json'))['ChecksumMd5'])"
        $checksums += $checksum
    }
    if (-not $pass) { break }
    
    $uniqueChecksums = $checksums | Select-Object -Unique
    if ($uniqueChecksums.Count -eq 1) {
        Write-Host "Seed ${seed}: PASS"
    } else {
        Write-Host "Seed ${seed}: FAIL (checksums differ)"
        foreach ($c in $checksums) { Write-Host "  $c" }
        $pass = $false
    }
}

if ($pass) { exit 0 } else { exit 1 }