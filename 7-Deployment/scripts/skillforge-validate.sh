#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOYMENT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$DEPLOYMENT_ROOT/.." && pwd)"
SKILLFORGE_PROJ="$DEPLOYMENT_ROOT/tools/SkillForge/src/SkillForge.Cli"

echo "==> Building SkillForge..."
dotnet build "$SKILLFORGE_PROJ" -c Release --nologo -v q

SKILLFORGE="dotnet run --project $SKILLFORGE_PROJ -c Release --no-build --"

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
    $SKILLFORGE validate "$target" --format table || EXIT_CODE=1

    echo "--- lint ---"
    $SKILLFORGE lint "$target" --min-desc-score 70 --format table || EXIT_CODE=1

    echo "--- scan ---"
    $SKILLFORGE scan "$target" --fail-on critical --format table || EXIT_CODE=1
done

if [ $EXIT_CODE -eq 0 ]; then
    echo ""
    echo "All skill checks passed."
else
    echo ""
    echo "Some skill checks failed. See above."
fi

exit $EXIT_CODE
