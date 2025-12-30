---
name: mandatory-review
description: Enforces a code review by the specialist agent before any task is marked as complete.
---
# Quality Assurance Protocol

**Goal:** Prevent unverified code from being submitted.

**Trigger:** Whenever you (the main agent) are about to:
1. Mark a task as "done" or "completed".
2. Submit a Pull Request.
3. Tell the user you have finished the request.

**Action Required:**
Before taking the final step, you MUST:
1. Call the `code-reviewer` sub-agent.
2. Ask it to: "Review the changes in [files you modified] for bugs, security issues, and style."
3. If the reviewer finds issues, FIX them.
4. Only mark the task as done after the reviewer gives a "LGTM" (Looks Good To Me) or passes the code