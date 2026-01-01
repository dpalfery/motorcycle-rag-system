---
name: mandatory-review
description: Code review and quality validation. MUST be executed before any git commit and before marking tasks as complete. Triggers: "commit", "push", "done", "finished", "complete the task"
---
# Quality Assurance Protocol

**Goal:** Prevent unverified code from being submitted.

**Trigger:** Whenever you (the main agent) are about to:
1. Mark a task as "done" or "completed".
2. Submit a Pull Request.
3. Tell the user you have finished the request.

**Action Required:**
Before taking the final step, you MUST:
1. Call the `code-reviewer` sub-agent to execute the `mandatory-review` protocol defined here.
2. Ask it to: "Review the entire repository (not just your diff) for bugs, security issues, and best practices. Ground the review in the design docs under `specs/` and the constitution (default `.specify/memory/constitution.md`, override via `CONSTITUTION_PATH` if relocated)."
3. If the reviewer finds issues, FIX them.
4. Only mark the task as done after the reviewer gives a "LGTM" (Looks Good To Me) or passes the code
