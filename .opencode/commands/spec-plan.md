---
description: Orchestrate the three-phase spec-kit planning flow (requirements → design → tasks) for one feature. Produces planning artifacts only.
agent: orchestrator
---

# Spec Planning Flow

## Context Requirement

Run this command as the first request in a new session.

If this command is invoked in a session that already contains unrelated prior work, stop immediately and tell the user to start a new session, then rerun this command. Do not continue from stale conversation context.

You are orchestrating a three-phase planning flow for one feature. You produce **planning artifacts only** — you never implement the feature. Your responsibilities are: derive the feature name, delegate each phase, run the approval gate between phases, and route backward when a phase reports a gap.

Do **not** describe this workflow to the user, announce which phase you are on, or explain that you are following a process. Just do the work and surface only the gate questions and the artifacts.

## 0. Start

Feature idea: `$1` (describe the feature you want to plan)

If the idea is thin, you may ask for a short vision doc, but do not interrogate the user with a long question list — one brief ask at most, then proceed.

Derive a short, kebab-case `feature_name` from the idea (e.g. "user-authentication"). All artifacts live in `docs/specs/{feature_name}/`.

If a spec already exists for this feature, treat this as an update: read the existing artifacts and resume at the earliest phase the user wants to change.

## 1. Requirements phase

Delegate to the **`@requirements-author`** agent. Pass it: `feature_name`, the feature idea, and any vision doc.

When it returns its digest, read `docs/specs/{feature_name}/requirements.md` and present a concise view of it to the user, then run:

**GATE 1** — ask, verbatim:
> Do the requirements look good? If so, we can move on to the design.

- If the user does **not** give explicit approval (e.g. "yes", "approved", "looks good"), collect their change requests and re-delegate to **`@requirements-author`** with the current file plus the feedback. Re-present and ask again. Repeat until explicitly approved.
- Only on explicit approval, continue to phase 2.

## 2. Design phase

Delegate to the **`@design-architect`** agent. Pass it: `feature_name` and the approved requirements.

Handle the returned digest:
- `STATUS: REQUIREMENTS_GAP` → tell the user the design surfaced a requirements gap (quote the `GAPS`), return to **phase 1** to amend requirements, then come back here.
- `STATUS: READY_FOR_REVIEW` → read `docs/specs/{feature_name}/design.md`, present a concise view, then run:

**GATE 2** — ask, verbatim:
> Does the design look good? If so, we can move on to the implementation plan.

- On anything short of explicit approval, collect feedback, re-delegate to **`@design-architect`**, re-present, ask again. Repeat until approved.
- Only on explicit approval, continue to phase 3.

## 3. Tasks phase

Delegate to the **`@task-planner`** agent. Pass it: `feature_name`, the approved requirements, and the approved design.

Handle the returned digest:
- `STATUS: DESIGN_GAP` → if the gap is in the design, return to **phase 2**; if `GAPS` indicates a missing requirement, return to **phase 1**. Then resume forward.
- `STATUS: READY_FOR_REVIEW` → read `docs/specs/{feature_name}/tasks.md`, present a concise view, then run:

**GATE 3** — ask, verbatim:
> Do the tasks look good?

- On anything short of explicit approval, collect feedback, re-delegate to **`@task-planner`**, re-present, ask again. Repeat until approved.

## 4. Done

Once the tasks are approved, stop. This flow produces planning artifacts only — do not begin implementation. Tell the user the spec is complete and that they can begin executing tasks by opening `docs/specs/{feature_name}/tasks.md` and starting the first task.

## Invariants
- Never proceed past a gate without explicit user approval.
- Never author or edit an artifact yourself — always delegate to the phase agent.
- Carry a tight changelog when re-delegating for revisions: pass the current artifact plus the user's specific requests, not the whole conversation.
- Keep the user's view focused on the artifact and the gate question; do not expose the orchestration mechanics.

User input:
$ARGUMENTS