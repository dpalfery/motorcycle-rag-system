#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOYMENT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$DEPLOYMENT_ROOT/.." && pwd)"
KYBER_WEAVE_PROJ="$DEPLOYMENT_ROOT/tools/KyberWeave/src/KyberWeave.Cli"

echo "==> Building Kyber-Weave..."
dotnet build "$KYBER_WEAVE_PROJ" -c Release --nologo -v q

KYBER_WEAVE="dotnet run --project $KYBER_WEAVE_PROJ -c Release --no-build --"

SKILL_DIRS=(".agents/skills" ".claude/skills" ".kilo/skills")
EXIT_CODE=0

for skills_dir in "${SKILL_DIRS[@]}"; do
    target="$REPO_ROOT/$skills_dir"
    if [ ! -d "$target" ]; then
        echo "==> Skipping $skills_dir (not found)"
        continue
    fi

    echo ""
    echo "========================================"
    echo "  Validating: $skills_dir"
    echo "========================================"

    echo "--- validate ---"
    $KYBER_WEAVE skill validate "$target" --format table || EXIT_CODE=1

    echo "--- lint ---"
    $KYBER_WEAVE skill lint "$target" --min-desc-score 70 --format table || EXIT_CODE=1

    echo "--- scan ---"
    $KYBER_WEAVE skill scan "$target" --fail-on critical --format table || EXIT_CODE=1
done

if [ $EXIT_CODE -eq 0 ]; then
    echo ""
    echo "All skill checks passed."
else
    echo ""
    echo "Some skill checks failed. See above."
fi

exit $EXIT_CODE
