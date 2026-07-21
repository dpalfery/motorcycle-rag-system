# Agent Scratchpad

Working area for agent-generated intermediate output: research notes, analysis, plan
drafts, build logs, coverage inventories, and anything else produced while working that
is not a deliverable.

This directory is registered as **Agent Scratchpad** in the Config Registry of the root
[AGENTS.md](../AGENTS.md).

## Rules

- **Everything here is git-ignored** except this README and `.gitkeep`. Nothing in it is
  ever a deliverable.
- **Never cite a file in this directory from canonical documentation.** Scratch content
  does not exist on a fresh clone, so such a link is broken for everyone but its author.
  If scratch content turns out to be worth keeping, promote it into `6-Docs/` under the
  [documentation standard](../6-Docs/documentation-standard.md) and give it frontmatter
  per the [documentation ontology](../6-Docs/documentation-ontology.md).
- **Do not put scratch output in `6-Docs/`.** That tree is canonical documentation only,
  and is the corpus the documentation knowledge graph ingests.
- **Do not create scratch files or folders elsewhere at the repository root.** This
  directory is the single approved exception to the root-cleanliness rule.

## History

This replaces `6-Docs/agent-notes/`, which sat inside the canonical documentation root and
had to be special-cased by every documentation glob, ingestion sweep, and link check.
