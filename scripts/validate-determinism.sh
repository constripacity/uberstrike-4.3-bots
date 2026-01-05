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
    md5sum run-summary.json | awk '{print $1}' >>"${tmpfile}"
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
