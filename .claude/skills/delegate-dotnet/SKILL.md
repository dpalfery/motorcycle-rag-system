---
name: delegate-dotnet
description: Forces the use of the specialized dotnet-dev agent for any C#, .NET, or backend coding tasks.
license: MIT
metadata:
  author: David R Palfery
  version: 1.0.0
---

# .NET Delegation Protocol

**Trigger:**
This skill MUST be used whenever the user requests:
- Writing or editing C# code (`.cs` files).
- Creating or modifying `.csproj` or `.sln` files.
- Running `dotnet` commands.
- Any backend logic involving ASP.NET Core or Entity Framework.

**Procedure:**
1. **DO NOT** write the code yourself.
2. Immediately invoke the `dotnet-dev` sub-agent.
3. Pass the full user request and any relevant file context to the sub-agent.
4. Wait for the sub-agent to complete the task (write code, run tests, etc.).
5. Report the sub-agent's results back to the user.

**Example Hand-off:**
> "I see you need a new API controller. I will have the `.NET Specialist` handle this to ensure strict typing and correct dependency injection patterns."
