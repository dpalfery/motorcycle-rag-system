#!/bin/bash
INPUT=$(cat)
FILE_PATH=$(echo "$INPUT" | jq -r '.tool_input.filePath // empty')

if [ -z "$FILE_PATH" ]; then
  exit 0
fi

PROJECT_ROOT=$(git rev-parse --show-toplevel 2>/dev/null || pwd)
RELATIVE_PATH="${FILE_PATH#$PROJECT_ROOT/}"

if [[ "$RELATIVE_PATH" != *"/"* ]]; then
  echo "BLOCKED: Creating files at the project root is forbidden. Place the file in the appropriate subdirectory instead." >&2
  exit 1
fi

if [[ "$RELATIVE_PATH" == *.cs ]]; then
  CONTENT=$(echo "$INPUT" | jq -r '.tool_input.content // empty')
  if echo "$CONTENT" | grep -qE '^\s*(public\s+)?interface\s+\w+'; then
    if [[ "$RELATIVE_PATH" != 3-Domain/MotorcycleRAG.Contracts/* ]]; then
      echo "BLOCKED: C# interfaces must be placed in 3-Domain/MotorcycleRAG.Contracts/. Only interfaces belong in the Contracts project. DTOs and models go in the Domain project." >&2
      exit 1
    fi
  fi
fi

exit 0
