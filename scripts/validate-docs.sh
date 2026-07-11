#!/usr/bin/env bash
set -euo pipefail

required_files=(
  "README.md"
  "AGENTS.md"
  "6-Docs/README.md"
  "6-Docs/catalog.md"
  "6-Docs/documentation-standard.md"
  "6-Docs/system/onboarding.md"
  "6-Docs/system/architecture.md"
  "6-Docs/system/requirements.md"
  "LICENSE"
  "CONTRIBUTING.md"
  "CODE_OF_CONDUCT.md"
  "SECURITY.md"
  "SUPPORT.md"
  "1-Presentation/MotorcycleRAG.API/README.md"
  "1-Presentation/MotorcycleRAG.AdminDesktop/README.md"
  "1-Presentation/MotorcycleRAG.MobileApp/README.md"
  "1-Presentation/MotorcycleRag.WebUI/README.md"
  "2-Application/local-processing-service/README.md"
  "7-Deployment/README.md"
  "7-Deployment/DbSetup/README.md"
  "7-Deployment/tools/SkillForge/README.md"
  "CODEOWNERS"
)

required_agent_files=(
  ".codex/AGENTS.md"
  "0-Base/MotorcycleRAG.Core/AGENTS.md"
  "1-Presentation/MotorcycleRAG.API/AGENTS.md"
  "1-Presentation/MotorcycleRAG.AdminDesktop/AGENTS.md"
  "1-Presentation/MotorcycleRAG.MobileApp/AGENTS.md"
  "1-Presentation/MotorcycleRag.WebUI/AGENTS.md"
  "1-Presentation/MotorcycleRag.WebUI.BFF/AGENTS.md"
  "2-Application/MotorcycleRAG.Application/AGENTS.md"
  "2-Application/local-processing-service/AGENTS.md"
  "3-Domain/MotorcycleRAG.Contracts/AGENTS.md"
  "3-Domain/MotorcycleRAG.Contracts.Models/AGENTS.md"
  "3-Domain/MotorcycleRAG.Domain/AGENTS.md"
  "4-Persistence/MotorcycleRAG.Persistence/AGENTS.md"
  "5-Test/MotorcycleRAG.MobileApp.Tests/AGENTS.md"
  "5-Test/tests/MotorcycleRAG.EndToEndTests/AGENTS.md"
  "5-Test/tests/MotorcycleRAG.IntegrationTests/AGENTS.md"
  "5-Test/tests/MotorcycleRAG.LoadTests/AGENTS.md"
  "5-Test/tests/MotorcycleRAG.UnitTests/AGENTS.md"
  "7-Deployment/infrastructure/AGENTS.md"
)

for file in "${required_files[@]}"; do
  if [[ ! -f "$file" ]]; then
    echo "Missing required documentation file: $file" >&2
    exit 1
  fi
done

for file in "${required_agent_files[@]}"; do
  if [[ ! -f "$file" ]]; then
    echo "Missing required agent instruction file: $file" >&2
    exit 1
  fi
done

while IFS= read -r file; do
  echo "Lowercase agent instruction files are forbidden; rename to AGENTS.md: $file" >&2
  exit 1
done < <(find . \( -path './.git' -o -path './.kilo' \) -prune -o -type f -name 'agents.md' -print)

components=(
  "MotorcycleRAG system"
  "MotorcycleRAG API"
  "MotorcycleRAG Admin Desktop"
  "MotorcycleRAG Mobile App"
  "MotorcycleRAG Web UI and BFF"
  "Local Processing Service"
  "Azure Environment"
  "Database Setup CLI"
  "SkillForge"
)

for component in "${components[@]}"; do
  if ! rg --fixed-strings --quiet "$component" 6-Docs/catalog.md; then
    echo "Catalog is missing component: $component" >&2
    exit 1
  fi
done

application_docs=(
  "6-Docs/MotorcycleRAG.API"
  "6-Docs/MotorcycleRAG.AdminDesktop"
  "6-Docs/MotorcycleRAG.MobileApp"
  "6-Docs/MotorcycleRag.WebUI"
  "6-Docs/local-processing-service"
  "6-Docs/AzureEnvironment"
)

for directory in "${application_docs[@]}"; do
  for document in onboarding.md architecture.md requirements.md; do
    if [[ ! -f "$directory/$document" ]]; then
      echo "Missing required component document: $directory/$document" >&2
      exit 1
    fi
  done
done

canonical_markdown=(
  "README.md"
  "AGENTS.md"
  "CONTRIBUTING.md"
  "CODE_OF_CONDUCT.md"
  "SECURITY.md"
  "SUPPORT.md"
  "6-Docs/README.md"
  "6-Docs/catalog.md"
  "6-Docs/documentation-standard.md"
  "6-Docs/deployment/README.md"
  "6-Docs/deployment/database-setup.md"
  "6-Docs/reference/README.md"
  "6-Docs/reference/skillforge.md"
  "2-Application/MotorcycleRAG.Application/Services/Caching/README.md"
)

while IFS= read -r file; do
  canonical_markdown+=("$file")
done < <(find . \( -path './.git' -o -path './.kilo' \) -prune -o -type f -name 'AGENTS.md' -print | sort)

for directory in "6-Docs/system" "${application_docs[@]}"; do
  while IFS= read -r file; do
    canonical_markdown+=("$file")
  done < <(find "$directory" -type f -name '*.md' | sort)
done

for file in "${canonical_markdown[@]}"; do
  while IFS= read -r raw_link; do
    link="${raw_link#](}"
    link="${link%)}"
    link="${link#<}"
    link="${link%>}"
    link="${link%%#*}"

    if [[ -z "$link" || "$link" == http://* || "$link" == https://* || "$link" == mailto:* || "$link" == /* ]]; then
      continue
    fi

    if [[ ! -e "$(dirname "$file")/$link" ]]; then
      echo "Broken local Markdown link in $file: $link" >&2
      exit 1
    fi
  done < <(grep -oE '\]\([^)]*\)' "$file" || true)
done

echo "Documentation structure, agent instruction hierarchy, catalog, and local-link validation passed."
