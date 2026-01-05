# UberStrike 4.3 Bot Framework - Submission Package

## Proof of Quality
- **Determinism**: 100/100 seeds identical (validation-report.json)
- **Performance**: All scenarios < 30s, < 200MB (benchmark.csv)
- **Stability**: Zero crashes in failure scenarios

## Quick Start
1. Ensure .NET 10 SDK is installed
2. Run: `.\scripts\final-validation.ps1`
3. View: `validation-report.json`

## For Developers
See `documentation\` for:
- Architecture overview (DeveloperGuide.md)
- Adding new behaviors (AddingBehavior.md)
- M2 integration guide (M2_Integration.md)

## Configuration
Example configs included: Easy, Normal, Hard, Competitive
