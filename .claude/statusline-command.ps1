# Comprehensive Claude Code Status Line
# Reads JSON input from stdin and displays detailed status information

# Read JSON input from stdin (avoid using $input as it's a built-in variable)
$jsonInput = [Console]::In.ReadToEnd()

# Parse JSON
$data = $jsonInput | ConvertFrom-Json

# Extract values
$userName = $env:USERNAME
$computerName = $env:COMPUTERNAME
$currentDir = $data.workspace.current_dir
$modelName = $data.model.display_name
$outputStyle = $data.output_style.name
$contextWindow = $data.context_window

# Calculate context window percentage
$contextPct = "N/A"
if ($contextWindow.current_usage -ne $null) {
    $currentTokens = $contextWindow.current_usage.input_tokens +
                     $contextWindow.current_usage.cache_creation_input_tokens +
                     $contextWindow.current_usage.cache_read_input_tokens
    $totalSize = $contextWindow.context_window_size
    if ($totalSize -gt 0) {
        $pct = [math]::Floor(($currentTokens * 100) / $totalSize)
        $contextPct = "$pct%"
    }
}

# Get git information (skip optional locks)
$gitBranch = ""
$gitStatus = ""
$gitInfo = ""

Push-Location $currentDir
try {
    # Check if we're in a git repo
    $isGitRepo = git -c core.useBuiltinFSMonitor=false rev-parse --git-dir 2>$null
    if ($LASTEXITCODE -eq 0) {
        # Get current branch
        $gitBranch = git -c core.useBuiltinFSMonitor=false rev-parse --abbrev-ref HEAD 2>$null

        # Get short status
        $statusOutput = git -c core.useBuiltinFSMonitor=false status --porcelain 2>$null
        if ($statusOutput) {
            $modified = ($statusOutput | Where-Object { $_ -match '^\s*M|^M' }).Count
            $added = ($statusOutput | Where-Object { $_ -match '^\s*A|^A' }).Count
            $deleted = ($statusOutput | Where-Object { $_ -match '^\s*D|^D' }).Count
            $untracked = ($statusOutput | Where-Object { $_ -match '^\?\?' }).Count

            $statusParts = @()
            if ($modified -gt 0) { $statusParts += "~$modified" }
            if ($added -gt 0) { $statusParts += "+$added" }
            if ($deleted -gt 0) { $statusParts += "-$deleted" }
            if ($untracked -gt 0) { $statusParts += "?$untracked" }

            if ($statusParts.Count -gt 0) {
                $gitStatus = " [$($statusParts -join ' ')]"
            } else {
                $gitStatus = " [clean]"
            }
        } else {
            $gitStatus = " [clean]"
        }

        $gitInfo = " | git:$gitBranch$gitStatus"
    }
} catch {
    # Silently ignore git errors
} finally {
    Pop-Location
}

# Shorten directory path if needed (show last 2 segments)
$dirParts = $currentDir -split '[/\\]' | Where-Object { $_ -ne '' }
$shortDir = if ($dirParts.Count -gt 2) {
    $lastTwo = $dirParts[($dirParts.Count - 2)..($dirParts.Count - 1)]
    "...\" + ($lastTwo -join '\')
} else {
    $currentDir
}

# Build status line with model info
$modelInfo = if ($modelName) { " | model:$modelName" } else { "" }
$styleInfo = if ($outputStyle) { " | output:$outputStyle" } else { "" }
$statusLine = "$userName@$computerName | $shortDir$gitInfo$modelInfo$styleInfo | ctx:$contextPct"

# ANSI escape codes for deep blue background (RGB: 13, 71, 161 - Material Design Blue 900)
# Background: ESC[48;2;R;G;Bm, Foreground: ESC[97m (bright white text), Reset: ESC[0m
$bgColor = "$([char]27)[48;2;13;71;161m"
$fgColor = "$([char]27)[97m"
$reset = "$([char]27)[0m"

# Get terminal width
$termWidth = $Host.UI.RawUI.WindowSize.Width
if ($termWidth -le 0) { $termWidth = 120 } # Fallback width

# Build the complete statusline as a single string with embedded newlines
$output = New-Object System.Text.StringBuilder

# Content line(s) - handle wrapping if needed
$contentLength = $statusLine.Length

if ($contentLength -le $termWidth) {
    # Single line - pad to full width
    $padding = " " * ($termWidth - $contentLength)
    [void]$output.Append($bgColor)
    [void]$output.Append($fgColor)
    [void]$output.Append($statusLine)
    [void]$output.Append($padding)
    [void]$output.Append($reset)
} else {
    # Multiple lines due to wrapping
    $offset = 0
    while ($offset -lt $contentLength) {
        $chunkLength = [Math]::Min($termWidth, $contentLength - $offset)
        $chunk = $statusLine.Substring($offset, $chunkLength)
        $padding = " " * ($termWidth - $chunk.Length)

        # Add newline before subsequent lines
        if ($offset -gt 0) {
            [void]$output.AppendLine()
        }

        [void]$output.Append($bgColor)
        [void]$output.Append($fgColor)
        [void]$output.Append($chunk)
        [void]$output.Append($padding)
        [void]$output.Append($reset)
        $offset += $chunkLength
    }
}

# Output everything at once as a single write operation
Write-Host $output.ToString()
