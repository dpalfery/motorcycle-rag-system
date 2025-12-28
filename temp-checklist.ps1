$files = Get-ChildItem 'specs/001-system-spec/checklists/*.md'
foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $total = ([regex]::Matches($content, '^\- \[[ Xx]\]')).Count
    $completed = ([regex]::Matches($content, '^\- \[[Xx]\]')).Count
    $incomplete = $total - $completed
    Write-Output "$($file.Name)|$total|$completed|$incomplete"
}
