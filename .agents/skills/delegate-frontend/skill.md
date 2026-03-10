---
name: delegate-frontend
description: Forces the use of the specialized react-dev agent for any Frontend, React, or CSS coding tasks.
---

# Frontend Delegation Protocol

**Trigger:**
This skill MUST be used whenever the user requests:
- Writing or editing React code (`.jsx`, `.tsx`).
- Modifying UI components, HTML, or CSS (`.css`, `.scss`, `.tailwind`).
- Managing frontend dependencies (`npm`, `yarn`, `package.json`).
- Any logic involving React Hooks, State, or browser interactions.

**Procedure:**
1. **DO NOT** write the code yourself.
2. Immediately invoke the `react-dev` sub-agent.
3. Pass the full user request and any relevant file context (especially `package.json` or existing component files).
4. Wait for the sub-agent to complete the task.
5. Report the sub-agent's results back to the user.

**Example Hand-off:**
> "I will ask the `frontend-dev` to build this component to ensure it matches the project's styling and hook patterns."