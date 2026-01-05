$scenarios = @("duel", "many_actors", "load_spike_test", "bad_payload")
$project = "BotRunner"

Write-Output "Scenario,Time(s),PeakMem(MB),DecisionConfidence"

foreach ($scenario in $scenarios) {
    dotnet run --project $project -- --scenario $scenario --seed 1 --quiet | Out-Null
    if (-not (Test-Path run-summary.json)) {
        Write-Output "${scenario},ERROR,ERROR,ERROR"
        continue
    }
    
    # Extract metrics using Python
    $metrics = python -c "import json; d=json.load(open('run-summary.json')); print(str(d['TotalRuntimeSeconds']) + ',' + '{:.2f}'.format(d['PerformanceMetrics']['PeakWorkingSetMb']) + ',' + str(d['ActionPipeline']['AvgDecisionConfidence']))"
    Write-Output "${scenario},${metrics}"
}