#!/usr/bin/env bash
set -euo pipefail

required_files=(
  "README.md"
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

for file in "${required_files[@]}"; do
  if [[ ! -f "$file" ]]; then
    echo "Missing required documentation file: $file" >&2
    exit 1
  fi
done

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

echo "Documentation structure, catalog, and local-link validation passed."
