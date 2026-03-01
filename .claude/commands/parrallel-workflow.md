---
description: Create worktree, do work, review and merge back (all automated)
---

# Worktree Workflow

Create a worktree with feature branch, complete the task, then review and merge.

## Parse Task and Context

```bash
# Split on colon to separate task from context
FULL_INPUT="$ARGUMENTS"
TASK=$(echo "$FULL_INPUT" | cut -d':' -f1 | xargs)
CONTEXT=$(echo "$FULL_INPUT" | cut -d':' -f2- | xargs)
```

**Task:** $TASK
**Context:** $CONTEXT

## Step 1: Create Worktree

```bash
CURRENT_BRANCH=$(git branch --show-current)
MAIN_WORKTREE=$(git rev-parse --show-toplevel)
TASK_SLUG=$(echo "$TASK" | tr '[:upper:]' '[:lower:]' | tr ' ' '-' | cut -c1-30)
FEATURE_BRANCH="feature/$TASK_SLUG"
WORKTREE_PATH="$MAIN_WORKTREE/../worktrees/$FEATURE_BRANCH"

mkdir -p "$MAIN_WORKTREE/../worktrees"
git worktree add "$WORKTREE_PATH" -b "$FEATURE_BRANCH" "$CURRENT_BRANCH"

# Change to the worktree
cd "$WORKTREE_PATH"
```

## Step 2: Complete the Task

**Task:** $TASK

**Additional Context:** $CONTEXT

Work on the task in this worktree using the provided context.

[Complete the implementation here]

## Step 3: Review Changes

```bash
echo "📋 CHANGES IN: $FEATURE_BRANCH"
echo "──────────────────────────────"
git status
git diff --stat HEAD
git diff HEAD
```

**Present to user:**
```
⚠️  APPROVAL REQUIRED

Review changes above.
Type 'yes' to commit and merge to $CURRENT_BRANCH
```

## Step 4: Commit and Merge (only if approved)

```bash
# Commit changes
git add -A
git commit -m "$TASK_SLUG

Task: $TASK
Context: $CONTEXT

Completed and approved"

# Switch back to main worktree
cd "$MAIN_WORKTREE"

# Merge with --no-ff
git merge "$FEATURE_BRANCH" --no-ff -m "Merge $FEATURE_BRANCH"

# Cleanup
git worktree remove "$WORKTREE_PATH"
git branch -d "$FEATURE_BRANCH"

echo "✅ Merged to $CURRENT_BRANCH"
echo "📝 Test, then: git push origin $CURRENT_BRANCH"
```

## Safety Rules

- Show complete diff before commit
- Require explicit "yes" approval
- Never push to origin
- Use --no-ff for clear history