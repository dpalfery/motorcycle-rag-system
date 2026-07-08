# {Feature/Change Title}

**Status:** Draft
**Date:** {YYYY-MM-DD}
**Goal:** {One-sentence summary}

---

## 1. Problem / Motivation

**For a bug or existing situation:** Describe the symptom and the root-cause chain, each link verified against live source or Azure. No re-litigation — this section records the finding, it does not debate it.

**For a new feature:** Describe the gap or opportunity and why the current system cannot satisfy it without this change.

## 2. Approved decisions

Record approved decisions verbatim with a stable identifier (D1, D2, ...). These are immutable once approved and serve as the implementation contract.

## 3. Investigation findings

Summarize facts gathered from live source, Azure read-only queries, and documentation that informed the plan. Include resolved open questions and their answers.

## 4. Task list

Each task has an objective, exact files/symbols, acceptance criteria, required skills, owning agent, and dependencies. No code is written in this plan.

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
|   |       |           |             |        |

## 5. Sequencing / dependency graph

Define task ordering and blocking dependencies. A task should only appear after everything it depends on.

## 6. Residual decisions / risks

Flag decisions still pending at plan time and known risks that remain. Each entry names the owner or condition that will resolve it.

## 7. Out of scope

List work explicitly excluded from this plan to prevent scope creep. Each item should say why it's out of scope and where it belongs if known.

## 8. Skill → agent mapping table

Map each required skill to the specialist agent that owns it. Every task in section 4 references skills from this table.

## 9. Verification harness

Describes the verification gates that must pass before the plan is considered done: unit test coverage expectations per component, code review by `code-reviewer`, security review by `security-review`, and any read-only Azure validation by `azure-reader`.
