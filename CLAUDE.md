@AGENTS.md
<!-- rtk-instructions v2 -->
# RTK (Rust Token Killer) - Token-Optimized Commands

## Golden Rule

**Always prefix commands with `rtk`**. If RTK has a dedicated filter, it uses it. If not, it passes through unchanged. This means RTK is always safe to use.

**Important**: Even in command chains with `&&`, use `rtk`:
```bash
# ❌ Wrong
git add . && git commit -m "msg" && git push

# ✅ Correct
rtk git add . && rtk git commit -m "msg" && rtk git push
```

A `PreToolUse:Bash` hook (`rtk hook claude`) rewrites commands automatically, so the
full command reference is not needed here. Run `rtk --help` for the current filter list,
or `rtk gain` for token-savings analytics.
<!-- /rtk-instructions -->
