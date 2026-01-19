#!/bin/bash
# Claude Code Status Line Script
# Displays: agent | foldername | branch [~staged:?modified] | model | ctx:X%

# ANSI color codes
BLUE_BG='\033[48;2;33;113;181m'  # #2171b5 background
WHITE_FG='\033[97m'               # White text
RESET='\033[0m'

# Read JSON input from stdin and parse with Python
read -r -d '' input

# Extract values from JSON using Python
json_data=$(python3 -c "
import sys, json
try:
    data = json.loads('''$input''')
    cwd = data.get('workspace', {}).get('current_dir') or data.get('cwd', '.')
    model = data.get('model', {}).get('display_name') or data.get('model', {}).get('id', '')
    ctx = data.get('context_window', {}).get('remaining_percentage', '')
    agent = data.get('agent', {}).get('name') or data.get('mode', 'claude')
    print(f'{cwd}|{model}|{ctx}|{agent}')
except:
    print('.|unknown||claude')
" 2>/dev/null)

# Split the Python output
IFS='|' read -r cwd model context_remaining agent <<< "$json_data"

# Change to the working directory to get git info
if [ -n "$cwd" ] && [ -d "$cwd" ]; then
    cd "$cwd" 2>/dev/null || true
fi

# Get git branch and detailed status
git_branch=$(git -c core.fileMode=false rev-parse --abbrev-ref HEAD 2>/dev/null)
staged_count=0
modified_count=0

if [ -n "$git_branch" ]; then
    # Count staged files
    staged_count=$(git -c core.fileMode=false diff --cached --numstat 2>/dev/null | wc -l)

    # Count modified + untracked files
    modified_files=$(git -c core.fileMode=false diff --numstat 2>/dev/null | wc -l)
    untracked_files=$(git -c core.fileMode=false ls-files --others --exclude-standard 2>/dev/null | wc -l)
    modified_count=$((modified_files + untracked_files))
fi

# Extract just the folder name
folder_name=$(basename "${cwd:-.}")

# Build status line with | separators
parts=()

# Agent name first
if [ -n "$agent" ]; then
    parts+=("$agent")
fi

# Folder name
parts+=("$folder_name")

# Git branch with detailed status
if [ -n "$git_branch" ]; then
    git_info="$git_branch"

    # Add status counts if there are any changes
    if [ "$staged_count" -gt 0 ] || [ "$modified_count" -gt 0 ]; then
        git_info="$git_info [~$staged_count:?$modified_count]"
    fi

    parts+=("$git_info")
fi

# Model
if [ -n "$model" ]; then
    parts+=("$model")
fi

# Context
if [ -n "$context_remaining" ]; then
    context_int=$(printf "%.0f" "$context_remaining" 2>/dev/null || echo "$context_remaining")
    parts+=("ctx:$context_int%")
fi

# Join with | and apply blue background with white text
printf "${BLUE_BG}${WHITE_FG} %s" "${parts[0]}"
for ((i=1; i<${#parts[@]}; i++)); do
    printf " | %s" "${parts[$i]}"
done
printf " ${RESET}\n"
