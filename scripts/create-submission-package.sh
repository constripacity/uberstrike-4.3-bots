#!/bin/bash
set -euo pipefail

PACKAGE_ROOT="uberstrike-4.3-bots-submission"
CODE_DIR="${PACKAGE_ROOT}/Code"
DOCS_DIR="${PACKAGE_ROOT}/Documentation"
EXAMPLES_DIR="${PACKAGE_ROOT}/Examples"
VALIDATION_DIR="${PACKAGE_ROOT}/Validation"

rm -rf "${PACKAGE_ROOT}"
mkdir -p "${CODE_DIR}" "${DOCS_DIR}" "${EXAMPLES_DIR}/Configurations" "${EXAMPLES_DIR}/Behaviors" "${EXAMPLES_DIR}/Scenarios" "${VALIDATION_DIR}"

git archive --format=tar HEAD | tar -xf - -C "${CODE_DIR}"

# Documentation
cp Docs/DeveloperGuide.md "${DOCS_DIR}/" 2>/dev/null || true
cp Docs/M2_Integration.md "${DOCS_DIR}/" 2>/dev/null || true
[ -f validation-report.json ] && cp validation-report.json "${DOCS_DIR}/ValidationReport.pdf" 2>/dev/null || true

# Examples (configs)
if [ -d Config/Examples ]; then
  cp Config/Examples/*.json "${EXAMPLES_DIR}/Configurations/" 2>/dev/null || true
fi

# Validation artifacts
[ -f scripts/validate-determinism.sh ] && cp scripts/validate-determinism.sh "${VALIDATION_DIR}/" || true
[ -f scripts/benchmark.sh ] && cp scripts/benchmark.sh "${VALIDATION_DIR}/" || true
[ -f scripts/final-validation.sh ] && cp scripts/final-validation.sh "${VALIDATION_DIR}/" || true
[ -f validation-report.json ] && cp validation-report.json "${VALIDATION_DIR}/" || true

cat > "${PACKAGE_ROOT}/README_SUBMISSION.md" <<README
# UberStrike 4.3 Bots Submission

- Code: full repository snapshot
- Documentation: core guides and validation report
- Examples: configuration presets
- Validation: scripts and captured reports

To regenerate validation data:
- ./scripts/validate-determinism.sh
- ./scripts/benchmark.sh
- ./scripts/final-validation.sh
README

echo "Submission package created at ${PACKAGE_ROOT}" >&2
