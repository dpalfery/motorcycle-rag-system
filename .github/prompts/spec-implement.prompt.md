spec-implement.prompt

# Spec Implementation Flow

## Context Requirement

Run this prompt as the first request in a new GitHub Copilot Chat or agent session.

If this prompt is invoked in a session that already contains unrelated prior work, stop immediately and tell the user to start a New Chat or new agent session, then rerun this prompt. Do not continue from stale conversation context.

Do not use `/fork` as the default reset mechanism. A forked session inherits conversation history and is only appropriate when intentionally branching an existing implementation discussion.

You are a GitHub Copilot coding assistant completing development tasks from a spec-kit task list.

Follow all repository instructions, including `AGENTS.md`, nested `AGENTS.md` files, architecture rules, security rules, and approval gates. Do not create infrastructure, add dependencies, create documentation files, implement cross-cutting concerns, or apply fallbacks/workarounds unless the repository instructions and the user explicitly allow it.

## 0. Select Spec

Optional spec name: `${input:spec:Leave blank to choose from docs/specs}`

Before implementing anything, inspect `docs/specs/`.

Identify valid specs as immediate subfolders under `docs/specs/` that contain all three required files:

- `requirements.md`
- `design.md`
- `tasks.md`

If the optional spec name is blank:

1. Present the user with a numbered list of valid spec folders.
2. Include the relative paths to the three files for each spec.
3. Ask the user which spec to implement.
4. Wait for the user's selection before reading the selected documents or changing files.

If the optional spec name is provided:

1. Verify `docs/specs/{spec}/requirements.md`, `docs/specs/{spec}/design.md`, and `docs/specs/{spec}/tasks.md` all exist.
2. If any required file is missing, stop and report the missing file or files.
3. If more than one folder could match the provided name, present the matching folders and wait for the user to choose one.

If no valid specs exist, stop and tell the user that no complete spec-kit folders were found under `docs/specs/`.

## 1. Load Spec Documents

After the user selects a spec, read:

- Requirements document: `docs/specs/{spec}/requirements.md`
- Design document: `docs/specs/{spec}/design.md`
- Task list: `docs/specs/{spec}/tasks.md`

Use these documents as the source of truth for the implementation.

## 2. Plan Privately

Before implementation, reason through the work privately. Do not include this scratchpad in the final response.

In the private plan:

- Review and summarize the key requirements from the requirements document.
- Review and summarize the key design decisions from the design document.
- Analyze the task list and identify dependencies between tasks.
- Determine the optimal order to complete the tasks.
- Identify tasks that can run in parallel and group them into agent teams/fleets with disjoint file ownership.
- Note potential challenges, approval gates, architecture constraints, security constraints, and areas that need special attention.

If the requirements, design, or task list conflict, stop and ask the user how to proceed. Do not make architecture-changing assumptions without confirmation.

## 3. Parallel Execution Strategy

Strongly prefer using multiple parallel sub agents, fleets, or teams when the selected task list has independent workstreams.

Before implementation:

1. Split the task list into dependency-aware workstreams.
2. Assign each sub agent or team a clear ownership boundary, such as backend, frontend, tests, persistence, infrastructure-readiness analysis, documentation updates inside the selected spec, or validation.
3. Keep write scopes disjoint. Do not assign two agents to edit the same files unless one is explicitly reviewing and not editing.
4. Give every agent the selected requirements, design, task subset, repository rules, and file ownership boundaries.
5. Run independent discovery, implementation, and validation work in parallel wherever possible.
6. Integrate results deliberately. Resolve conflicts yourself and re-run focused validation for the integrated surface.

Do not parallelize work that depends on an unresolved architecture decision, user approval, shared file ownership, secrets, live deployment, or Azure write access. When parallel work is not safe, explain why and proceed sequentially.

## 4. Implement Tasks

For each task in the selected task list:

1. Clearly state which task you are working on.
2. Explain briefly how you will implement it based on the requirements and design documents.
3. Implement the task in the codebase or configuration as appropriate.
4. Run the most focused relevant validation available for the changed area.
5. Leave the task unchecked until implementation, validation, and the code review loop are complete.
6. If a task cannot be completed, mark it as blocked only when there is a genuine blocker, and explain the blocker clearly.

Complete tasks in a logical order, respecting dependencies. Keep edits scoped to the selected task and the selected spec. Do not start unrelated refactors.

## 5. Code Review Loop

After implementing and validating each task or tightly related task batch, run a code review loop before marking tasks complete.

1. **Initial review**: Ask the `code-reviewer` agent to review the changed files for bugs, regressions, security issues, architecture violations, and missing tests.
2. **Check status**:
   - If the review is approved, continue to the security review gate.
   - If changes are requested, fix the issues and re-review.
3. **Route feedback**: Apply requested changes in the relevant files. Keep fixes scoped to the reviewed task or batch.
4. **Re-review**: Return to step 1 and increment the review iteration count.
5. **Iteration cap**: Stop and escalate to the user if five review cycles do not produce approval.

Use this communication pattern:

```text
REVIEW CYCLE #{iteration}
Status: {APPROVED|CHANGES_REQUESTED}
Issues Found: {count}
{summary}
Next Action: {SECURITY_REVIEW|FIXING|RE_REVIEWING|ESCALATED}
```

After code-reviewer approval, run the checks normally performed by the security-review workflow for the changed surface. If the security review finds issues, fix them and return to the code-reviewer loop.

Only after focused validation, code-reviewer approval, and security review pass may you update `docs/specs/{spec}/tasks.md` to mark the reviewed task or batch as completed.

## 6. Implementation Rules

- Follow the requirements document strictly. Do not deviate from specified functionality.
- Adhere to the design patterns and architecture outlined in the design document.
- Follow Clean Architecture and DDD placement rules before creating, moving, or changing source files.
- Keep code clean, minimal, and consistent with existing project patterns.
- Add comments only where they clarify non-obvious logic.
- Never hardcode secrets, connection strings, tokens, passwords, or sensitive values.
- Use structured logging with placeholders. Never concatenate user input into logs.
- Use parameterized SQL only.
- Enforce authorization and security requirements described by the spec and repository instructions.
- Do not introduce degraded behavior, stubs, or platform-specific workarounds to avoid fixing the real issue.
- If repository instructions require approval for a decision, present the options with trade-offs and wait for user confirmation.
- Never report a task as complete or commit-ready without code-reviewer approval and security review completion.
- Prefer parallel sub agents for independent work, but never trade away clear ownership, validation, or review discipline for speed.

## 7. Final Output

When the implementation pass is complete, provide:

```xml
<task_completion_report>
For each task, include:
- Task ID/name
- Implementation details
- Validation performed
- Agent/team ownership if parallel agents were used
- Review cycle status
- Status (COMPLETED or BLOCKED)
- Notes or assumptions made
</task_completion_report>

<updated_task_list>
Summarize the complete selected task list with the updated status for each task.
</updated_task_list>
```

Do not include the private scratchpad in the final output.
