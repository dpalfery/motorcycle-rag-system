---
name: create-pull-request
description: "use when asked to submit or create a Pull Request or PR in Azure DevOps"

---

Requirements:
- Azure DevOps MCP server is the preferred path when available.
- `az` CLI with the `azure-devops` extension installed (`az extension add --name azure-devops`).
- Authentication to Azure DevOps. In many environments, interactive `az login` is sufficient for `az repos pr` commands. Use `az devops login` with a PAT only when Azure DevOps CLI still prompts for credentials or your org requires PAT-based auth.
- Local git repo checked out and up-to-date; remote branch pushed.

Note: The agent will auto-detect `repository`, `organization`, and `project` from the current git remote configuration. Only provide these as explicit inputs if you want to override the detected values. `sourceBranch` defaults to the current branch. `targetBranch` must be provided explicitly or resolved from conclusive evidence about the current branch's parent branch; do not assume the repository default branch.


Inputs:
- `sourceBranch` (string, optional) - the branch to open the PR from. Defaults to the current branch if not specified.
- `targetBranch` (string, optional) - the target branch to merge into. If not specified, the skill must resolve the parent branch from conclusive evidence or ask the user to choose one.
- `title` (string, optional) - override for the PR title. If omitted, the skill will generate a clean title from the branch name.
- `extraNotes` (string, optional) - freeform notes to append to the PR description.
- `repository` (string, optional) - override for repository name. Auto-detected from git remote if not provided.
- `organization` (string, optional) - override for AZDO organization URL. Auto-detected from git remote if not provided.
- `project` (string, optional) - override for AZDO project name. Auto-detected from git remote if not provided.

Outputs:
- A markdown-formatted PR description and either the MCP operation result or the CLI invocation that was executed.

Behavior / Implementation Guidance:
1. Title generation:
  - If `title` not provided, generate it from `sourceBranch` by stripping prefixes (`feature/`, `bugfix/`, `hotfix/`, `task/`), splitting on `/`, `-`, and `_`, and title-casing non-numeric tokens.
  - Preserve leading numeric work item IDs from the branch name. Example: `feature/41171-audit-error-service-build-template` -> `41171 Audit Error Service Build Template`.
  - Do not use fragile regex replacements that can drop the first character of the branch stem.

2. Resolve the target or parent branch before building the PR:
  - If `targetBranch` was provided by the user, use it.
  - Otherwise, determine the parent branch from conclusive evidence only.
  - Conclusive evidence may include:
    - the user explicitly naming the parent branch earlier in the conversation
    - a validated team or repo rule already documented for the current branch type
    - a clearly established branch relationship from local git state, remote tracking configuration, or an existing PR pattern for the same branch
  - Never assume the repository default branch is the PR target without conclusive evidence that it is also the parent branch of the current branch.
  - If the parent branch cannot be determined conclusively, list the long-lived branches for the repository and ask the user to choose one or provide a different target branch.
  - Long-lived branches typically include branches such as `main`, `master`, `dev`, `develop`, and active `release/*` branches, but the actual list should come from the repository.

3. Gather context for description:
   - Use git to collect commits and changes between `targetBranch` and `sourceBranch` ONLY to inform the "Summary" and "Proposed Changes" sections.
   - Do NOT include raw lists of commits or file paths in the final PR description, as these are natively provided by the Azure DevOps UI.
   - Command for context: `git log --no-merges --pretty=format:"%s" origin/${targetBranch}..${sourceBranch}`

4. Build the description (Markdown sections):
   - **Summary / TL;DR**: A high-level overview (1–3 sentences) of the change.
     - **Goal**: Explain the "Why" more than the "How." (e.g., "Implements a caching layer for the Denver Airport parking API to reduce latency and stay within rate limits.")
   - **Type of Change**: A checklist to help the reviewer set their mindset:
     - [ ] Bug fix (non-breaking change which fixes an issue)
     - [ ] New feature (non-breaking change which adds functionality)
     - [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
     - [ ] Refactor (code change that neither fixes a bug nor adds a feature)
   - **Related Issue / Ticket**: A direct link to the tracking item (Azure DevOps, Jira, GitHub Issue).
     - Prefer deriving the ticket from the branch name when possible.
     - If the branch contains a leading numeric Azure DevOps work item ID, format it as `Closes AB#4567`.
     - If the branch contains an alphanumeric issue key such as `ABC-123`, preserve that format.
   - **Proposed Changes**: A bulleted list of the technical implementation details summarized from commit messages and file changes. Keep it short but complete.
   - **How Has This Been Tested?**: Documentation of verification steps:
     - **Unit Tests**: Mention new test coverage or updated suites.
     - **Manual Verification**: List the steps taken.
     - **Environments**: Specify if it was tested in a Dev container, a local VNet-integrated environment, etc.
   - **Notes**: Include `extraNotes` if provided.



5. Prefer Azure DevOps MCP first:
   - First check whether the Azure DevOps MCP tools are available and healthy by performing a lightweight read such as listing projects, resolving the repository, or listing pull requests for the source branch.
  - If `targetBranch` is not provided, use MCP or git to list long-lived branches and ask the user to choose one when the parent branch is not conclusively known.
   - If the Azure DevOps MCP server is available and healthy, use it as the first choice for PR operations.
   - Use MCP to:
     - resolve the repository
     - check whether a PR already exists for the same source and target branch
     - create the PR if none exists
     - update the PR if one already exists
     - validate the created or updated PR by reading it back
   - Preferred MCP PR flow:
     - `mcp_microsoft_azu_repo_list_pull_requests_by_repo_or_project`
     - `mcp_microsoft_azu_repo_create_pull_request`
     - `mcp_microsoft_azu_repo_update_pull_request`
     - `mcp_microsoft_azu_repo_get_pull_request_by_id`

6. If Azure DevOps MCP is not available, not installed, or unhealthy:
   - Ask the user whether they want to install or configure the Azure DevOps MCP server.
   - If the user says yes, assist them with installation or configuration before falling back to CLI.
   - If the user says no, fall back to the Azure DevOps CLI flow below.

7. Azure DevOps MCP installation and troubleshooting guidance:
   - If the MCP server is not installed, help the user add it to their MCP configuration.
   - A validated configuration on this machine used:
     - package: `@azure-devops/mcp@latest`
     - organization positional argument: organization name only, such as `flydenver`
     - authentication: `-a azcli`
     - domains: `-d all`
   - If the server is installed but fails before returning data, inspect the MCP configuration.
   - If the error includes `A potentially dangerous Request.Path value was detected from the client (:)`, the likely cause is that the organization value was configured as a full URL instead of an organization name.
   - The organization value must be the Azure DevOps organization name only, for example `flydenver`, not `https://dev.azure.com/flydenver`.
   - After fixing MCP configuration, reload the MCP server or restart VS Code and re-test a read-only MCP call before attempting PR creation.

8. Create or update the PR via Azure DevOps CLI fallback:
   - Write the final markdown description to a temporary UTF-8 file first.
  - If `targetBranch` is not provided and the parent branch is not conclusively known, list long-lived branches first and ask the user to choose one before proceeding.
   - Configure Azure DevOps defaults before list/create/update/show operations when `organization` and `project` are known:
     - `az devops configure --defaults organization="${organization}" project="${project}"`
   - The skill will attempt to create a PR using:
     - `az repos pr create --repository "${repository}" --source-branch "${sourceBranch}" --target-branch "${targetBranch}" --title "${title}" --description "@${descriptionFile}"`
   - If a PR already exists for the same source and target branch, the skill will update the existing PR's description and title using:
     - `az repos pr update --id <prId> --title "${title}" --description "@${descriptionFile}"`
   - `az repos pr list` and `az repos pr create` accept `--project`, but the currently validated `azure-devops` extension flow did not accept `--project` on `az repos pr update`. Prefer `az devops configure --defaults` and pass `--org` explicitly on update.
   - If `organization` or `project` are provided, the skill should prefer configuring defaults first, then pass `--org` and `--project` only on commands that support them.
   - In PowerShell, build `descriptionMarkdown` with a here-string, save it to `descriptionFile`, and pass `@<file>` to the CLI. Do not pass a multi-line description directly as a native command argument, and do not pass a string containing literal `\n` escape sequences.
   - After creation or update, the skill will validate that the PR's title and description match the intended values and that the description does not contain literal `\n` text. Use `az repos pr show --id <prId>` for this validation. If validation fails, retry the update or report an error.

9. Safety & validation:
   - Ensure `sourceBranch` exists locally or remotely; fail fast if not found.
  - Ensure `targetBranch` is explicit or resolved from conclusive evidence; otherwise ask the user before creating the PR.
  - Do not create a test PR in the target repository unless the user asked for one or the workflow explicitly requires an MCP health check.
   - Escape/encode the markdown description for the shell environment used.
   - Do not include secrets or credentials from the environment in the PR description.

Example script (POSIX / Bash):
```
# fetch
git fetch --prune --no-tags origin
SB=feature/my-change
TB=main # replace with the explicitly chosen or conclusively resolved parent branch
COMMITS=$(git log --no-merges --pretty=format:"- [%h] %s (%an)" origin/${TB}..${SB})
FILES=$(git diff --name-only origin/${TB}..${SB} | sed -n '1,20p')
SUMMARY=$(git log --no-merges --pretty=format:"%s" -n1 origin/${TB}..${SB})
TITLE="${SB##*/}" # simple fallback
DESCRIPTION_FILE=$(mktemp)
cat > "${DESCRIPTION_FILE}" <<EOF
## Summary

${SUMMARY}

## Type of Change

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Refactor (code change that neither fixes a bug nor adds a feature)

## Proposed Changes

Summarized implementation detail here...
EOF
az repos pr create --repository "my-repo" --source-branch "${SB}" --target-branch "${TB}" --title "${TITLE}" --description "@${DESCRIPTION_FILE}"
```

Example PowerShell snippet (Windows):
```
git fetch --prune --no-tags origin
$SB = 'feature/my-change'
$TB = 'main' # replace with the explicitly chosen or conclusively resolved parent branch
$org = 'https://dev.azure.com/myorg'
$project = 'DIA'
$repo = 'my-repo'
$summary = git log --no-merges --pretty=format:"%s" -n 1 "origin/$TB..$SB"
$branchStem = $SB -replace '^(feature/|bugfix/|hotfix/|task/)', ''
$titleParts = ($branchStem -split '[-_/]+' | Where-Object { $_ }) | ForEach-Object {
  if ($_ -match '^\d+$') { $_ }
  else { [System.Globalization.CultureInfo]::InvariantCulture.TextInfo.ToTitleCase($_.ToLowerInvariant()) }
}
$title = [string]::Join(' ', $titleParts)
if ($branchStem -match '^(\d+)(?:[-_/]|$)') { $ticket = "AB#$($Matches[1])" }
elseif ($branchStem -match '([A-Z]+-\d+)') { $ticket = $Matches[1] }
else { $ticket = '' }
$description = @"
## Summary

$summary

## Type of Change

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Refactor (code change that neither fixes a bug nor adds a feature)

## Related Issue / Ticket

Closes $ticket

## Proposed Changes

Summarized implementation detail here...
"@
$descriptionFile = Join-Path $env:TEMP 'create-pr-description.md'
Set-Content -Path $descriptionFile -Value $description -Encoding utf8
az devops configure --defaults organization=$org project=$project
$existing = az repos pr list --repository $repo --source-branch $SB --target-branch $TB --status active --org $org --project $project --output json | ConvertFrom-Json
if ($existing -and $existing.Count -gt 0) {
  $prId = $existing[0].pullRequestId
  az repos pr update --id $prId --title $title --description "@$descriptionFile" --org $org
}
else {
  az repos pr create --repository $repo --source-branch $SB --target-branch $TB --title $title --description "@$descriptionFile" --org $org --project $project
}
```

Example prompts to try with this skill:
- "Create a PR from `feature/auth-fix` into `main` for repo `apps-shared-services` using the org `https://dev.azure.com/myorg` and project `DIA`." 
- "Open a PR from my branch `ABCD-123/update-readme` — include commit messages and files changed." 
- "Use Azure DevOps MCP to open a PR from my current branch, and only fall back to Azure CLI if MCP is unavailable."

Ambiguities & follow-ups (ask user):
- Which Azure DevOps organization and project to target by default? (or should the skill always require `organization` + `project` inputs?)
- If the parent branch cannot be conclusively determined, which long-lived branch should be the PR target?
- Preferred auth method (PAT via `az devops login`, service principal, or interactive)?

Next steps / suggested automation:
- Provide a small cross-platform script (`scripts/create-pr.sh` and `scripts/create-pr.ps1`) that implements the command flow above and is callable by the skill.
- Optionally add parsing for work-item IDs in branch names and auto-link those items in the PR.

Notes for implementers:
- Keep the description markdown concise; long diffs are better left to the commit/diff view in the PR UI.
- Prefer to run the git commands against `origin` to avoid surprise local-only changes.
- Never assume the repository default branch is the PR target unless you have conclusive evidence it is the parent branch of the current branch.
- If the parent branch is unclear, list the repository's long-lived branches and ask the user to choose or provide the target branch before creating the PR.
- Prefer Azure DevOps MCP as the first choice when it is available and healthy.
- If Azure DevOps MCP is unavailable, ask the user whether they want to install or configure it before falling back to CLI.
- If the user declines MCP installation or troubleshooting, proceed with Azure CLI.
- For PowerShell, prefer here-strings for multi-line PR descriptions, write them to a UTF-8 temp file, and pass `@<file>` to `az repos pr create` or `az repos pr update`.
- A validated flow in this repo used `az login`, then `az devops configure --defaults organization=... project=...`, then `az repos pr list` and `az repos pr create` with `--project`, but `az repos pr update` required relying on configured defaults and omitting `--project`.
- A validated Azure DevOps MCP flow in this repo successfully listed projects, resolved the repository, created a temporary branch, created a draft PR, read it back, and abandoned it.
- A validated Azure DevOps MCP configuration on this machine used `@azure-devops/mcp@latest`, organization name `flydenver`, `-a azcli`, and `-d all`.
- A validated Azure DevOps MCP failure mode on this machine was caused by passing the full Azure DevOps URL instead of the organization name, which produced `A potentially dangerous Request.Path value was detected from the client (:)`.
- This workflow was validated against branch `feature/41171-audit-error-service-buld-template`, which required preserving the leading work item digits in the title and using `Closes AB#41171` in the PR description.
- This workflow was also validated against branch `patch/fixed-easy-auth-in-function-to-apim`, where the PR was created successfully after Azure CLI login without requiring `az devops login`.