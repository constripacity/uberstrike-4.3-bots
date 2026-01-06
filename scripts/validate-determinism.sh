#!/bin/bash
set -euo pipefail

SCENARIO="flipping_regression"
SEEDS=(42 777 12345 999 555)
RUNS=3
PROJECT="BotRunner"
QUIET_FLAG="--quiet"

pass=true

for seed in "${SEEDS[@]}"; do
  tmpfile="$(mktemp)"
  echo "Validating seed ${seed}..." >&2
  for i in $(seq 1 ${RUNS}); do
    dotnet run --project "${PROJECT}" -- --scenario "${SCENARIO}" --seed "${seed}" ${QUIET_FLAG} >/dev/null
    if [ ! -f run-summary.json ]; then
      echo "run-summary.json missing after run ${i} for seed ${seed}" >&2
      pass=false
      break
    fi
    checksum=$(python - <<'PY'
import json, sys
try:
    with open("run-summary.json", "r", encoding="utf-8") as f:
        data = json.load(f)
    print(data.get("ChecksumMd5", ""))
except Exception as exc:
    print(f"ERROR:{exc}", file=sys.stderr)
    sys.exit(1)
PY
)
    if echo "${checksum}" | grep -q "^ERROR:"; then
      echo "${checksum}" >&2
      pass=false
      break
    fi
    if [ -z "${checksum}" ]; then
      echo "ChecksumMd5 missing after run ${i} for seed ${seed}" >&2
      pass=false
      break
    fi
    echo "${checksum}" >>"${tmpfile}"
  done
  if [ "$pass" = false ]; then
    rm -f "${tmpfile}"
    break
  fi
  uniq_hashes=$(sort "${tmpfile}" | uniq | wc -l)
  if [ "$uniq_hashes" -eq 1 ]; then
    echo "Seed ${seed}: ✅ PASS"
  else
    echo "Seed ${seed}: ❌ FAIL (hashes differ)"
    pass=false
  fi
  rm -f "${tmpfile}"
 done

if [ "$pass" = true ]; then
  exit 0
else
  exit 1
fi
