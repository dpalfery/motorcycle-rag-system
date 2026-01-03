#!/usr/bin/env pwsh
# Fix safe violations: unused usings and naming conventions

# Fix unused using System.Collections.ObjectModel in Citation.cs
$file = ".\3-Domain\MotorcycleRAG.Domain\DTOs\Citation.cs"
$content = Get-Content $file -Raw -Encoding UTF8
$content = $content -replace "using System.Collections.ObjectModel;\r\n", ""
Set-Content $file -Value $content -Encoding UTF8
Write-Host "Fixed unused usings in Citation.cs"

# Fix enumeration naming: PDFDocumentType -> PdfDocumentType
$file = ".\3-Domain\MotorcycleRAG.Domain\Enums\PDFDocumentType.cs"
$content = Get-Content $file -Raw -Encoding UTF8
$content = $content -replace "public enum PDFDocumentType", "public enum PdfDocumentType"
Set-Content $file -Value $content -Encoding UTF8
Write-Host "Fixed enum naming: PDFDocumentType -> PdfDocumentType"

# Update references to PDFDocumentType in test files  
Get-ChildItem -Recurse -Filter "*.cs" -Path ".\5-Test" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw -Encoding UTF8
    if ($content -match "PDFDocumentType") {
        $content = $content -replace "PDFDocumentType", "PdfDocumentType"
        Set-Content $_.FullName -Value $content -Encoding UTF8
        Write-Host "Updated references in: $($_.Name)"
    }
}

Write-Host "Done!"
