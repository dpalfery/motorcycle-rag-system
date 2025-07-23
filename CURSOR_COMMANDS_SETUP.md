# Cursor Custom Commands - Global Setup Guide

This guide shows you how to add the custom `/push`, `/status`, and `/commit` commands to your **global** Cursor user settings so they work on any machine you use Cursor on.

## Step 1: Open Your Global Cursor Settings

### Method 1: Via Cursor Menu
1. Open Cursor
2. Go to **File** → **Preferences** → **Settings** (or press `Ctrl+,`)
3. Click the **"Open Settings (JSON)"** icon in the top-right corner

### Method 2: Direct File Access
Navigate to your Cursor user settings file:

**Windows:**
```
%APPDATA%\Cursor\User\settings.json
```

**macOS:**
```
~/Library/Application Support/Cursor/User/settings.json
```

**Linux:**
```
~/.config/Cursor/User/settings.json
```

## Step 2: Add the Custom Commands

Add this JSON configuration to your global `settings.json` file:

```json
{
  "cursor.chat.customCommands": [
    {
      "name": "push",
      "description": "Complete Git workflow: save, stage, commit, and push to origin",
      "prompt": "Execute the complete git workflow:\n\n1. Save all open/modified files first\n2. Run 'git add .' to stage all changes\n3. Analyze the staged changes with 'git status' and 'git diff --cached' to understand what was modified\n4. Create a descriptive commit message that summarizes the changes (what was added, modified, or removed)\n5. Commit with 'git commit -m \"descriptive message\"'\n6. Push to origin with 'git push origin'\n\nProvide clear feedback on each step and the final result.",
      "type": "chat"
    },
    {
      "name": "status",
      "description": "Check git status and recent commits",
      "prompt": "Please check the current git status:\n\n1. Run 'git status' to see current changes\n2. Run 'git log --oneline -5' to see recent commits\n3. If there are changes, run 'git diff' to show what's modified\n\nSummarize the current state of the repository.",
      "type": "chat"
    },
    {
      "name": "commit",
      "description": "Stage and commit changes with descriptive message",
      "prompt": "Stage and commit current changes:\n\n1. Run 'git add .' to stage all changes\n2. Analyze the changes with 'git diff --cached'\n3. Create a descriptive commit message based on the changes\n4. Commit with 'git commit -m \"descriptive message\"'\n\nDo not push - just stage and commit.",
      "type": "chat"
    }
  ]
}
```

## Step 3: If You Already Have Existing Settings

If your `settings.json` already has content, **merge** the `cursor.chat.customCommands` array with your existing settings:

```json
{
  "your.existing.setting": "value",
  "another.setting": true,
  "cursor.chat.customCommands": [
    {
      "name": "push",
      "description": "Complete Git workflow: save, stage, commit, and push to origin",
      "prompt": "Execute the complete git workflow:\n\n1. Save all open/modified files first\n2. Run 'git add .' to stage all changes\n3. Analyze the staged changes with 'git status' and 'git diff --cached' to understand what was modified\n4. Create a descriptive commit message that summarizes the changes (what was added, modified, or removed)\n5. Commit with 'git commit -m \"descriptive message\"'\n6. Push to origin with 'git push origin'\n\nProvide clear feedback on each step and the final result.",
      "type": "chat"
    },
    {
      "name": "status", 
      "description": "Check git status and recent commits",
      "prompt": "Please check the current git status:\n\n1. Run 'git status' to see current changes\n2. Run 'git log --oneline -5' to see recent commits\n3. If there are changes, run 'git diff' to show what's modified\n\nSummarize the current state of the repository.",
      "type": "chat"
    },
    {
      "name": "commit",
      "description": "Stage and commit changes with descriptive message", 
      "prompt": "Stage and commit current changes:\n\n1. Run 'git add .' to stage all changes\n2. Analyze the changes with 'git diff --cached'\n3. Create a descriptive commit message based on the changes\n4. Commit with 'git commit -m \"descriptive message\"'\n\nDo not push - just stage and commit.",
      "type": "chat"
    }
  ]
}
```

## Step 4: Save and Restart

1. Save the `settings.json` file
2. Restart Cursor
3. Open any git repository
4. Type `/push` in the Cursor chat to test

## ✅ Verification

After setup, you should be able to:
- Type `/push` in Cursor chat on ANY project/machine
- See the commands auto-complete when you type `/`
- Use the commands in any git repository

## Benefits of Global Settings

- **Works everywhere:** Available in all Cursor workspaces
- **Sync across machines:** If you use Cursor settings sync
- **One-time setup:** Configure once, use everywhere
- **Consistent workflow:** Same commands on all projects

## Troubleshooting

**Commands not showing up?**
1. Check JSON syntax is valid (no trailing commas, proper brackets)
2. Restart Cursor completely
3. Verify the file was saved correctly

**Already have custom commands?**
- Add the new commands to your existing `cursor.chat.customCommands` array
- Don't duplicate the array, just add the objects inside

## What Stays in Workspace

The following remain workspace-specific (in `.vscode/` folder):
- VS Code tasks for Command Palette access
- Keyboard shortcuts
- Project-specific git configurations

This gives you the best of both worlds: global chat commands + project-specific shortcuts! 