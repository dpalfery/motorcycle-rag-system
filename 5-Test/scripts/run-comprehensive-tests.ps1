# Comprehensive Test Execution Script
# This script runs the complete test suite locally for development and validation

param(
    [switch]$UnitTests = $true,
    [switch]$IntegrationTests = $true,
    [switch]$EndToEndTests = $true,
    [switch]$LoadTests = $false,
    [switch]$AzureIntegrationTests = $false,
    [switch]$GenerateReports = $true,
    [switch]$OpenReports = $false,
    [switch]$UnitCoverage = $false,
    [switch]$GenerateCoverageTests = $false,
    [string]$Configuration = "Release",
    [string]$TestFilter = "",
    [int]$MaxDegreeOfParallelism = 4,
    [int]$GenerationBudget = 5,
    [double]$CoverageThreshold = 0
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Colors for output
$Green = "Green"
$Red = "Red"
$Yellow = "Yellow"
$Cyan = "Cyan"

function Write-ColorOutput($ForegroundColor) {
    $fc = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    if ($args) {
        Write-Output $args
    } else {
        $input | Write-Output
    }
    $host.UI.RawUI.ForegroundColor = $fc
}

function Write-Header($Message) {
    Write-ColorOutput $Cyan "============================================"
    Write-ColorOutput $Cyan $Message
    Write-ColorOutput $Cyan "============================================"
}

function Write-Success($Message) {
    Write-ColorOutput $Green "✓ $Message"
}

function Write-Error($Message) {
    Write-ColorOutput $Red "✗ $Message"
}

function Write-Warning($Message) {
    Write-ColorOutput $Yellow "⚠ $Message"
}

function Get-PythonCommand {
    if (Get-Command py -ErrorAction SilentlyContinue) {
        return @("py", "-3")
    }
    if (Get-Command python -ErrorAction SilentlyContinue) {
        return @("python")
    }
    throw "Python runtime not found on PATH"
}

# Initialize
$StartTime = Get-Date
$TestResults = @()
$FailedTests = @()

Write-Header "Motorcycle RAG System - Comprehensive Test Suite"
Write-Output "Started at: $StartTime"
Write-Output "Configuration: $Configuration"
Write-Output "Max Parallelism: $MaxDegreeOfParallelism"
Write-Output ""

# Create results directory
$ResultsDir = "TestResults"
if (Test-Path $ResultsDir) {
    Remove-Item $ResultsDir -Recurse -Force
}
New-Item -ItemType Directory -Path $ResultsDir -Force | Out-Null

if ($UnitCoverage) {
    Write-Header "Running Unified Unit Coverage"
    $pythonCommand = Get-PythonCommand
    $pythonArgs = @()
    if ($pythonCommand.Length -gt 1) {
        $pythonArgs = $pythonCommand[1..($pythonCommand.Length - 1)]
    }
    $coverageArgs = @(
        "5-Test/scripts/run_unit_coverage.py",
        "--results-dir", "$ResultsDir/UnitCoverage",
        "--configuration", $Configuration
    )

    if ($CoverageThreshold -gt 0) {
        $coverageArgs += "--threshold", "$CoverageThreshold"
    }

    & $pythonCommand[0] @pythonArgs $coverageArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Unified unit coverage failed"
    }

    if ($GenerateCoverageTests) {
        Write-Header "Running Coverage-Driven Test Generation"
        $autogenArgs = @(
            "5-Test/scripts/auto_generate_coverage_tests.py",
            "--results-dir", "$ResultsDir/UnitCoverage",
            "--configuration", $Configuration,
            "--budget", "$GenerationBudget"
        )
        if ($CoverageThreshold -gt 0) {
            $autogenArgs += "--threshold", "$CoverageThreshold"
        }

        & $pythonCommand[0] @pythonArgs $autogenArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Coverage-driven test generation failed"
        }

        & $pythonCommand[0] @pythonArgs $coverageArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Post-generation coverage run failed"
        }
    }

    if ($OpenReports) {
        $reportPath = Join-Path $ResultsDir "UnitCoverage/CoverageReport/coverage-summary.html"
        if (Test-Path $reportPath) {
            Start-Process $reportPath
        }
    }

    exit 0
}

try {
    # Build solution
    Write-Header "Building Solution"
    dotnet build --configuration $Configuration --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    Write-Success "Solution built successfully"

    # Unit Tests
    if ($UnitTests) {
        Write-Header "Running Unit Tests"
        $unitTestPath = "5-Test/tests/MotorcycleRAG.UnitTests/MotorcycleRAG.UnitTests.csproj"
        
        $unitTestArgs = @(
            "test", $unitTestPath,
            "--no-build",
            "--configuration", $Configuration,
            "--logger", "trx",
            "--logger", "console;verbosity=normal",
            "--results-directory", "$ResultsDir/UnitTests",
            "--collect:XPlat Code Coverage",
            "--settings", "coverlet.runsettings"
        )
        
        if ($TestFilter) {
            $unitTestArgs += "--filter", $TestFilter
        }
        
        $unitTestResult = & dotnet @unitTestArgs
        $unitTestExitCode = $LASTEXITCODE
        
        $TestResults += @{
            Name = "Unit Tests"
            ExitCode = $unitTestExitCode
            Duration = (Get-Date) - $StartTime
        }
        
        if ($unitTestExitCode -eq 0) {
            Write-Success "Unit tests passed"
        } else {
            Write-Error "Unit tests failed"
            $FailedTests += "Unit Tests"
        }
    }

    # Integration Tests
    if ($IntegrationTests) {
        Write-Header "Running Integration Tests"
        $integrationTestPath = "5-Test/MotorcycleRAG.IntegrationTests/MotorcycleRAG.IntegrationTests.csproj"
        
        $integrationTestArgs = @(
            "test", $integrationTestPath,
            "--no-build",
            "--configuration", $Configuration,
            "--logger", "trx",
            "--logger", "console;verbosity=normal",
            "--results-directory", "$ResultsDir/IntegrationTests",
            "--filter", "Category!=AzureIntegration"
        )
        
        if ($TestFilter) {
            $integrationTestArgs += "--filter", "$TestFilter&Category!=AzureIntegration"
        }
        
        $integrationTestResult = & dotnet @integrationTestArgs
        $integrationTestExitCode = $LASTEXITCODE
        
        $TestResults += @{
            Name = "Integration Tests"
            ExitCode = $integrationTestExitCode
            Duration = (Get-Date) - $StartTime
        }
        
        if ($integrationTestExitCode -eq 0) {
            Write-Success "Integration tests passed"
        } else {
            Write-Error "Integration tests failed"
            $FailedTests += "Integration Tests"
        }
    }

    # End-to-End Tests
    if ($EndToEndTests) {
        Write-Header "Running End-to-End Tests"
        $e2eTestPath = "5-Test/MotorcycleRAG.EndToEndTests/MotorcycleRAG.EndToEndTests.csproj"
        
        # Set environment variables for E2E tests
        $env:TestConfiguration__UseRealAzureServices = "false"
        $env:TestConfiguration__TestDataPath = "TestData"
        
        $e2eTestArgs = @(
            "test", $e2eTestPath,
            "--no-build",
            "--configuration", $Configuration,
            "--logger", "trx",
            "--logger", "console;verbosity=normal",
            "--results-directory", "$ResultsDir/EndToEndTests",
            "--filter", "Category!=Integration"
        )
        
        if ($TestFilter) {
            $e2eTestArgs += "--filter", "$TestFilter&Category!=Integration"
        }
        
        $e2eTestResult = & dotnet @e2eTestArgs
        $e2eTestExitCode = $LASTEXITCODE
        
        $TestResults += @{
            Name = "End-to-End Tests"
            ExitCode = $e2eTestExitCode
            Duration = (Get-Date) - $StartTime
        }
        
        if ($e2eTestExitCode -eq 0) {
            Write-Success "End-to-end tests passed"
        } else {
            Write-Error "End-to-end tests failed"
            $FailedTests += "End-to-End Tests"
        }
    }

    # Azure Integration Tests
    if ($AzureIntegrationTests) {
        Write-Header "Running Azure Integration Tests"
        Write-Warning "These tests require real Azure service credentials"
        
        $azureTestPath = "5-Test/MotorcycleRAG.EndToEndTests/MotorcycleRAG.EndToEndTests.csproj"
        
        # Check for Azure credentials
        if (-not $env:AZURE_CLIENT_ID -or -not $env:AZURE_CLIENT_SECRET -or -not $env:AZURE_TENANT_ID) {
            Write-Warning "Azure credentials not found. Skipping Azure integration tests."
            Write-Warning "Set AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, and AZURE_TENANT_ID environment variables."
        } else {
            $env:TestConfiguration__UseRealAzureServices = "true"
            
            $azureTestArgs = @(
                "test", $azureTestPath,
                "--no-build",
                "--configuration", $Configuration,
                "--logger", "trx",
                "--logger", "console;verbosity=normal",
                "--results-directory", "$ResultsDir/AzureIntegrationTests",
                "--filter", "Category=Integration"
            )
            
            $azureTestResult = & dotnet @azureTestArgs
            $azureTestExitCode = $LASTEXITCODE
            
            $TestResults += @{
                Name = "Azure Integration Tests"
                ExitCode = $azureTestExitCode
                Duration = (Get-Date) - $StartTime
            }
            
            if ($azureTestExitCode -eq 0) {
                Write-Success "Azure integration tests passed"
            } else {
                Write-Error "Azure integration tests failed"
                $FailedTests += "Azure Integration Tests"
            }
        }
    }

    # Load Tests
    if ($LoadTests) {
        Write-Header "Running Load Tests"
        Write-Warning "Load tests require the application to be running"
        
        $loadTestPath = "5-Test/MotorcycleRAG.LoadTests/MotorcycleRAG.LoadTests.csproj"
        
        # Check if application is running
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:5000/api/motorcycle/health" -TimeoutSec 5 -ErrorAction Stop
            Write-Success "Application is running and accessible"
        } catch {
            Write-Warning "Application not accessible at http://localhost:5000"
            Write-Warning "Please start the application before running load tests"
            Write-Warning "Run: dotnet run --project 1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj"
        }
        
        $env:LoadTest__BaseUrl = "http://localhost:5000"
        $env:LoadTest__Duration = "00:02:00"  # 2 minutes for local testing
        $env:LoadTest__ConcurrentUsers = "10"  # Reduced for local testing
        
        $loadTestArgs = @(
            "test", $loadTestPath,
            "--no-build",
            "--configuration", $Configuration,
            "--logger", "trx",
            "--logger", "console;verbosity=normal",
            "--results-directory", "$ResultsDir/LoadTests"
        )
        
        $loadTestResult = & dotnet @loadTestArgs
        $loadTestExitCode = $LASTEXITCODE
        
        $TestResults += @{
            Name = "Load Tests"
            ExitCode = $loadTestExitCode
            Duration = (Get-Date) - $StartTime
        }
        
        if ($loadTestExitCode -eq 0) {
            Write-Success "Load tests passed"
        } else {
            Write-Error "Load tests failed"
            $FailedTests += "Load Tests"
        }
    }

    # Generate Reports
    if ($GenerateReports) {
        Write-Header "Generating Test Reports"
        
        # Generate coverage report
        if (Test-Path "$ResultsDir/UnitTests") {
            Write-Output "Generating code coverage report..."
            $coverageFiles = Get-ChildItem -Path "$ResultsDir/UnitTests" -Filter "coverage.cobertura.xml" -Recurse
            if ($coverageFiles) {
                # Install reportgenerator tool if not present
                dotnet tool install -g dotnet-reportgenerator-globaltool 2>$null
                
                $coverageReport = "$ResultsDir/CoverageReport"
                reportgenerator -reports:"$($coverageFiles[0].FullName)" -targetdir:$coverageReport -reporttypes:Html
                Write-Success "Coverage report generated at: $coverageReport/index.html"
                
                if ($OpenReports) {
                    Start-Process "$coverageReport/index.html"
                }
            }
        }
        
        # Generate test summary
        $summaryPath = "$ResultsDir/TestSummary.html"
        $summaryContent = @"
<!DOCTYPE html>
<html>
<head>
    <title>Motorcycle RAG System - Test Results</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; }
        .header { background-color: #f0f0f0; padding: 20px; border-radius: 5px; }
        .success { color: green; }
        .failure { color: red; }
        .warning { color: orange; }
        table { border-collapse: collapse; width: 100%; margin-top: 20px; }
        th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
        th { background-color: #f2f2f2; }
    </style>
</head>
<body>
    <div class="header">
        <h1>Motorcycle RAG System - Test Results</h1>
        <p>Generated: $(Get-Date)</p>
        <p>Configuration: $Configuration</p>
    </div>
    
    <h2>Test Summary</h2>
    <table>
        <tr><th>Test Suite</th><th>Status</th><th>Duration</th></tr>
"@
        
        foreach ($result in $TestResults) {
            $status = if ($result.ExitCode -eq 0) { "PASSED" } else { "FAILED" }
            $statusClass = if ($result.ExitCode -eq 0) { "success" } else { "failure" }
            $summaryContent += "<tr><td>$($result.Name)</td><td class='$statusClass'>$status</td><td>$($result.Duration)</td></tr>"
        }
        
        $summaryContent += @"
    </table>
    
    <h2>Overall Result</h2>
    <p class="$(if ($FailedTests.Count -eq 0) { 'success' } else { 'failure' })">
        $(if ($FailedTests.Count -eq 0) { 'ALL TESTS PASSED' } else { "FAILED TESTS: $($FailedTests -join ', ')" })
    </p>
</body>
</html>
"@
        
        $summaryContent | Out-File -FilePath $summaryPath -Encoding UTF8
        Write-Success "Test summary generated at: $summaryPath"
        
        if ($OpenReports) {
            Start-Process $summaryPath
        }
    }

} catch {
    Write-Error "Test execution failed: $_"
    exit 1
} finally {
    $EndTime = Get-Date
    $TotalDuration = $EndTime - $StartTime
    
    Write-Header "Test Execution Complete"
    Write-Output "Total Duration: $TotalDuration"
    Write-Output "Results Directory: $ResultsDir"
    
    if ($FailedTests.Count -eq 0) {
        Write-Success "All tests passed successfully!"
        exit 0
    } else {
        Write-Error "Some tests failed: $($FailedTests -join ', ')"
        exit 1
    }
}
