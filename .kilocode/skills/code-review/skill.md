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
1. Call the `code-reviewer` sub-agent.
2. Ask it to: "Review the changes in [files you modified] for bugs, security issues, and style."
3. If the reviewer finds issues, FIX them.
4. Only mark the task as done after the reviewer gives a "LGTM" (Looks Good To Me) or passes the code

5. **Code Quality**
- No build errors
- no Warnings of any kind. Un resolved warning make me cranky
