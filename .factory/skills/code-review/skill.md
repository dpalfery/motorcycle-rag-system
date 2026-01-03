---
name: code-review
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
1. **Run analyzer checks**: Execute `dotnet build --no-incremental --verbosity minimal` to capture all Roslyn and SonarLint analyzer violations.
2. **Check build output**: Verify there are NO analyzer warnings or errors (CAxxxx rules, Sxxxx rules).
3. **Fix analyzer violations**: If any analyzer violations exist, fix them BEFORE proceeding.
4. Call the `code-reviewer` sub-agent.
5. Ask it to: "Review the changes in [files you modified] for bugs, security issues, style, AND verify analyzer violations have been addressed."
6. If the reviewer finds issues, FIX them.
7. Only mark the task as done after:
   - The build passes with NO analyzer warnings/errors (Option A: warnings treated as errors).
   - The reviewer gives a "LGTM" (Looks Good To Me) or passes the code.

**Analyzer Enforcement (Option A - Aggressive):**
- All analyzer warnings are treated as build errors due to `TreatWarningsAsErrors=true` in Directory.Build.props
- SonarLint rules (Sxxxx) and .NET rules (CAxxxx) must pass before code can be committed
- Build failure due to analyzers means code review FAILED