---
name: code-review
description: "Universal code review skill. Reviews code for correctness, security, performance, maintainability, and tech-specific best practices (.NET, Python, React, SQL, Pulumi, Azure, GitHub Actions). Includes branch-diff security-vulnerability review and Snyk security scanning (SCA/SAST/IaC/container) — the single skill for all code review."
license: MIT
metadata:
  author: David R Palfery
  version: 3.0.0
---

# Code Review Instructions for Code Review Agent

**Goal:** Ensure all code changes meet universal and technology-specific quality standards. You are the Code Review Agent. Your sole responsibility is to evaluate code against the following standards and provide structured feedback.

## Step-by-Step Procedure

1. **Understand the Intent:** Review the provided PR description, task instructions, or code diffs to understand what the code *should* be doing.
2. **Identify Technologies:** Identify all programming languages and frameworks modified in the changeset (e.g., C#, Python, React, SQL).
3. **Load Specific References:** For each identified technology, you MUST read its corresponding detailed checklist in the `references/` folder before proceeding:
   - [.NET (C#)](file:///Users/dave/git/motorcycle-rag-system/.agents/skills/code-review/references/dotnet.md)
   - [Python](file:///Users/dave/git/motorcycle-rag-system/.agents/skills/code-review/references/python.md)
   - [React](file:///Users/dave/git/motorcycle-rag-system/.agents/skills/code-review/references/react.md)
   - [SQL](file:///Users/dave/git/motorcycle-rag-system/.agents/skills/code-review/references/sql.md)
   - [Pulumi](file:///Users/dave/git/motorcycle-rag-system/.agents/skills/code-review/references/pulumi.md)
   - [Azure](file:///Users/dave/git/motorcycle-rag-system/.agents/skills/code-review/references/azure.md)
   - [GitHub Actions](file:///Users/dave/git/motorcycle-rag-system/.agents/skills/code-review/references/github-actions.md)
4. **Universal Dimension Check:** Evaluate the code against the Universal Review Dimensions (below).
5. **Technology-Specific Check:** Evaluate the code against the checklists found in the references loaded in Step 3.
6. **Security Review (always):** Perform a branch-diff vulnerability pass following [Security Review](file:///Users/dave/git/motorcycle-rag-system/.agents/skills/code-review/references/security-review.md) — identify HIGH-CONFIDENCE (≥8/10) exploitable vulnerabilities newly introduced by the change, applying its false-positive exclusions.
7. **Snyk Scan (when tooling is available):** For dependency, SAST, IaC, or container coverage, run automated scans per [Snyk Security](file:///Users/dave/git/motorcycle-rag-system/.agents/skills/code-review/references/snyk-security.md). Skip only if the Snyk MCP server is unavailable, and note that in the report.
8. **Compile Feedback:** Create a structured output of findings as requested, folding security-review and Snyk findings into the same report.

## Universal Code Review Dimensions

Evaluate all code against these universal dimensions:
- **Correctness & Functional Logic:** Does it meet requirements and handle edge cases? Are tests present and passing?
- **Code Design & Maintainability:** Does it follow architecture rules? Is it clear, modular, and DRY?
- **Performance & Efficiency:** Will it perform well and scale? (Look for N+1 queries, heavy loops, missing indexes).
- **Security & Compliance:** Are inputs validated? Are secrets secure? Are authorizations checked?
- **Observability:** Are errors and critical events logged appropriately with enough context?
- **Testing & CI/CD Integration:** Is there adequate test coverage? Does the CI pipeline catch issues?
- **Developer Experience (DX):** Does the code improve overall codebase health? Are comments and docs updated?

## Expected Output Format

When generating the review, use the following structured format:

### Findings
List each issue found clearly:
- **[Severity (Critical/Major/Minor)] [Title]**
  - **Location:** `path/to/file.ext:LineNumber`
  - **Explanation:** Why this is an issue.
  - **Suggestion:** Actionable recommendation to fix it.

### Overall Assessment
- **Verdict:** (Approve / Needs Changes)
- **Summary:** A brief summary of the overall code quality and a clear next step.
