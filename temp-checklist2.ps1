$files = Get-ChildItem 'specs/001-system-spec/checklists/*.md'
foreach ($file in $files) {
    $content = Get-Content $file.FullName
    $total = ($content | Where-Object { $_ -match '^\- \[[ Xx]\]' }).Count
    $completed = ($content | Where-Object { $_ -match '^\- \[[Xx]\]' }).Count
    $incomplete = ($content | Where-Object { $_ -match '^\- \[ \]' }).Count
    Write-Output "$($file.Name)|$total|$completed|$incomplete"
}
