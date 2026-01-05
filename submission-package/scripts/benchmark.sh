#!/bin/bash
set -euo pipefail

SCENARIOS=(duel many_actors load_spike_test bad_payload)
PROJECT="BotRunner"
OUTPUT_HEADER=${OUTPUT_HEADER:-true}
QUIET_FLAG="--quiet"

if [ "$OUTPUT_HEADER" = "true" ]; then
  echo "Scenario,Time(s),PeakMem(MB),DecisionConfidence"
fi

for scenario in "${SCENARIOS[@]}"; do
  tmp_time="$(mktemp)"
  # /usr/bin/time prints real seconds (%e) and max resident set size in KB (%M)
  /usr/bin/time -f "%e %M" -o "${tmp_time}" dotnet run --project "${PROJECT}" -- --scenario "${scenario}" --seed 1 ${QUIET_FLAG} >/dev/null
  if [ ! -f run-summary.json ]; then
    echo "${scenario},ERROR,ERROR,ERROR"
    rm -f "${tmp_time}"
    continue
  fi
  read -r seconds_kb <"${tmp_time}"
  runtime_sec=$(echo "${seconds_kb}" | awk '{print $1}')
  peak_kb=$(echo "${seconds_kb}" | awk '{print $2}')
  peak_mb=$(python - <<PY
import sys
kb=float(sys.argv[1]) if len(sys.argv)>1 else 0
print(f"{kb/1024:.2f}")
PY
${peak_kb})
  confidence=$(jq -r '.ActionPipeline.AvgDecisionConfidence // 0' run-summary.json 2>/dev/null)
  echo "${scenario},${runtime_sec},${peak_mb},${confidence}"
  rm -f "${tmp_time}"
 done
