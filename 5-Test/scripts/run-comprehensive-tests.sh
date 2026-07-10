#!/bin/bash

# Comprehensive Test Execution Script for Linux/macOS
# This script runs the complete test suite locally for development and validation

set -e

# Default parameters
UNIT_TESTS=true
INTEGRATION_TESTS=true
END_TO_END_TESTS=true
LOAD_TESTS=false
AZURE_INTEGRATION_TESTS=false
GENERATE_REPORTS=true
OPEN_REPORTS=false
UNIT_COVERAGE_MODE=false
GENERATE_COVERAGE_TESTS=false
CONFIGURATION="Release"
TEST_FILTER=""
MAX_PARALLELISM=4
GENERATION_BUDGET=5
UNIT_COVERAGE_THRESHOLD=""

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Helper functions
print_header() {
    echo -e "${CYAN}============================================${NC}"
    echo -e "${CYAN}$1${NC}"
    echo -e "${CYAN}============================================${NC}"
}

print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

print_error() {
    echo -e "${RED}✗ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠ $1${NC}"
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --no-unit-tests)
            UNIT_TESTS=false
            shift
            ;;
        --no-integration-tests)
            INTEGRATION_TESTS=false
            shift
            ;;
        --no-e2e-tests)
            END_TO_END_TESTS=false
            shift
            ;;
        --load-tests)
            LOAD_TESTS=true
            shift
            ;;
        --azure-integration-tests)
            AZURE_INTEGRATION_TESTS=true
            shift
            ;;
        --no-reports)
            GENERATE_REPORTS=false
            shift
            ;;
        --unit-coverage)
            UNIT_COVERAGE_MODE=true
            shift
            ;;
        --generate-coverage-tests)
            GENERATE_COVERAGE_TESTS=true
            shift
            ;;
        --open-reports)
            OPEN_REPORTS=true
            shift
            ;;
        --coverage-threshold)
            UNIT_COVERAGE_THRESHOLD="$2"
            shift 2
            ;;
        --generation-budget)
            GENERATION_BUDGET="$2"
            shift 2
            ;;
        --configuration)
            CONFIGURATION="$2"
            shift 2
            ;;
        --filter)
            TEST_FILTER="$2"
            shift 2
            ;;
        --help)
            echo "Usage: $0 [OPTIONS]"
            echo "Options:"
            echo "  --no-unit-tests              Skip unit tests"
            echo "  --no-integration-tests       Skip integration tests"
            echo "  --no-e2e-tests              Skip end-to-end tests"
            echo "  --load-tests                 Run load tests"
            echo "  --azure-integration-tests    Run Azure integration tests"
            echo "  --no-reports                 Skip report generation"
            echo "  --unit-coverage              Run the repo unit coverage pipeline"
            echo "  --generate-coverage-tests    Auto-generate unit tests for weak spots"
            echo "  --open-reports              Open reports after generation"
            echo "  --coverage-threshold VALUE   Override the unit coverage threshold"
            echo "  --generation-budget COUNT    Limit auto-generation attempts"
            echo "  --configuration CONFIG       Build configuration (Debug/Release)"
            echo "  --filter FILTER             Test filter expression"
            echo "  --help                      Show this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Initialize
START_TIME=$(date +%s)
FAILED_TESTS=()
RESULTS_DIR="TestResults"

print_header "Motorcycle RAG System - Comprehensive Test Suite"
echo "Started at: $(date)"
echo "Configuration: $CONFIGURATION"
echo "Max Parallelism: $MAX_PARALLELISM"
echo ""

# Create results directory
if [ -d "$RESULTS_DIR" ]; then
    rm -rf "$RESULTS_DIR"
fi
mkdir -p "$RESULTS_DIR"

if [ "$UNIT_COVERAGE_MODE" = true ]; then
    print_header "Running Unified Unit Coverage"
    COVERAGE_ARGS=(
        "5-Test/scripts/run_unit_coverage.py"
        "--results-dir" "$RESULTS_DIR/UnitCoverage"
        "--configuration" "$CONFIGURATION"
    )

    if [ -n "$UNIT_COVERAGE_THRESHOLD" ]; then
        COVERAGE_ARGS+=("--threshold" "$UNIT_COVERAGE_THRESHOLD")
    fi

    if python3 "${COVERAGE_ARGS[@]}"; then
        print_success "Unified unit coverage completed"
    else
        print_error "Unified unit coverage failed"
        exit 1
    fi

    if [ "$GENERATE_COVERAGE_TESTS" = true ]; then
        print_header "Running Coverage-Driven Test Generation"
        AUTOGEN_ARGS=(
            "5-Test/scripts/auto_generate_coverage_tests.py"
            "--results-dir" "$RESULTS_DIR/UnitCoverage"
            "--configuration" "$CONFIGURATION"
            "--budget" "$GENERATION_BUDGET"
        )
        if [ -n "$UNIT_COVERAGE_THRESHOLD" ]; then
            AUTOGEN_ARGS+=("--threshold" "$UNIT_COVERAGE_THRESHOLD")
        fi

        if python3 "${AUTOGEN_ARGS[@]}"; then
            print_success "Coverage-driven test generation completed"
        else
            print_error "Coverage-driven test generation failed"
            exit 1
        fi

        if python3 "${COVERAGE_ARGS[@]}"; then
            print_success "Post-generation coverage run completed"
        else
            print_error "Post-generation coverage run failed"
            exit 1
        fi
    fi

    if [ "$OPEN_REPORTS" = true ]; then
        REPORT_PATH="$RESULTS_DIR/UnitCoverage/CoverageReport/coverage-summary.html"
        if [ -f "$REPORT_PATH" ]; then
            if command -v xdg-open > /dev/null; then
                xdg-open "$REPORT_PATH"
            elif command -v open > /dev/null; then
                open "$REPORT_PATH"
            fi
        fi
    fi

    exit 0
fi

# Build solution
print_header "Building Solution"
if dotnet build --configuration "$CONFIGURATION" --verbosity minimal; then
    print_success "Solution built successfully"
else
    print_error "Build failed"
    exit 1
fi

# Unit Tests
if [ "$UNIT_TESTS" = true ]; then
    print_header "Running Unit Tests"
    UNIT_TEST_PATH="5-Test/tests/MotorcycleRAG.UnitTests/MotorcycleRAG.UnitTests.csproj"
    
    UNIT_TEST_ARGS=(
        "test" "$UNIT_TEST_PATH"
        "--no-build"
        "--configuration" "$CONFIGURATION"
        "--logger" "trx"
        "--logger" "console;verbosity=normal"
        "--results-directory" "$RESULTS_DIR/UnitTests"
        "--collect:XPlat Code Coverage"
        "--settings" "coverlet.runsettings"
    )
    
    if [ -n "$TEST_FILTER" ]; then
        UNIT_TEST_ARGS+=("--filter" "$TEST_FILTER")
    fi
    
    if dotnet "${UNIT_TEST_ARGS[@]}"; then
        print_success "Unit tests passed"
    else
        print_error "Unit tests failed"
        FAILED_TESTS+=("Unit Tests")
    fi
fi

# Integration Tests
if [ "$INTEGRATION_TESTS" = true ]; then
    print_header "Running Integration Tests"
    INTEGRATION_TEST_PATH="5-Test/tests/MotorcycleRAG.IntegrationTests/MotorcycleRAG.IntegrationTests.csproj"
    
    INTEGRATION_TEST_ARGS=(
        "test" "$INTEGRATION_TEST_PATH"
        "--no-build"
        "--configuration" "$CONFIGURATION"
        "--logger" "trx"
        "--logger" "console;verbosity=normal"
        "--results-directory" "$RESULTS_DIR/IntegrationTests"
        "--filter" "Category!=AzureIntegration"
    )
    
    if [ -n "$TEST_FILTER" ]; then
        INTEGRATION_TEST_ARGS+=("--filter" "$TEST_FILTER&Category!=AzureIntegration")
    fi
    
    if dotnet "${INTEGRATION_TEST_ARGS[@]}"; then
        print_success "Integration tests passed"
    else
        print_error "Integration tests failed"
        FAILED_TESTS+=("Integration Tests")
    fi
fi

# End-to-End Tests
if [ "$END_TO_END_TESTS" = true ]; then
    print_header "Running End-to-End Tests"
    E2E_TEST_PATH="5-Test/tests/MotorcycleRAG.EndToEndTests/MotorcycleRAG.EndToEndTests.csproj"
    
    # Set environment variables for E2E tests
    export TestConfiguration__UseRealAzureServices="false"
    export TestConfiguration__TestDataPath="TestData"
    
    E2E_TEST_ARGS=(
        "test" "$E2E_TEST_PATH"
        "--no-build"
        "--configuration" "$CONFIGURATION"
        "--logger" "trx"
        "--logger" "console;verbosity=normal"
        "--results-directory" "$RESULTS_DIR/EndToEndTests"
        "--filter" "Category!=Integration"
    )
    
    if [ -n "$TEST_FILTER" ]; then
        E2E_TEST_ARGS+=("--filter" "$TEST_FILTER&Category!=Integration")
    fi
    
    if dotnet "${E2E_TEST_ARGS[@]}"; then
        print_success "End-to-end tests passed"
    else
        print_error "End-to-end tests failed"
        FAILED_TESTS+=("End-to-End Tests")
    fi
fi

# Azure Integration Tests
if [ "$AZURE_INTEGRATION_TESTS" = true ]; then
    print_header "Running Azure Integration Tests"
    print_warning "These tests require real Azure service credentials"
    
    AZURE_TEST_PATH="5-Test/tests/MotorcycleRAG.EndToEndTests/MotorcycleRAG.EndToEndTests.csproj"
    
    # Check for Azure credentials
    if [ -z "$AZURE_CLIENT_ID" ] || [ -z "$AZURE_CLIENT_SECRET" ] || [ -z "$AZURE_TENANT_ID" ]; then
        print_warning "Azure credentials not found. Skipping Azure integration tests."
        print_warning "Set AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, and AZURE_TENANT_ID environment variables."
    else
        export TestConfiguration__UseRealAzureServices="true"
        
        AZURE_TEST_ARGS=(
            "test" "$AZURE_TEST_PATH"
            "--no-build"
            "--configuration" "$CONFIGURATION"
            "--logger" "trx"
            "--logger" "console;verbosity=normal"
            "--results-directory" "$RESULTS_DIR/AzureIntegrationTests"
            "--filter" "Category=Integration"
        )
        
        if dotnet "${AZURE_TEST_ARGS[@]}"; then
            print_success "Azure integration tests passed"
        else
            print_error "Azure integration tests failed"
            FAILED_TESTS+=("Azure Integration Tests")
        fi
    fi
fi

# Load Tests
if [ "$LOAD_TESTS" = true ]; then
    print_header "Running Load Tests"
    print_warning "Load tests require the application to be running"
    
    LOAD_TEST_PATH="5-Test/tests/MotorcycleRAG.LoadTests/MotorcycleRAG.LoadTests.csproj"
    
    # Check if application is running
    if curl -f -s "http://localhost:5000/api/motorcycle/health" > /dev/null 2>&1; then
        print_success "Application is running and accessible"
    else
        print_warning "Application not accessible at http://localhost:5000"
        print_warning "Please start the application before running load tests"
        print_warning "Run: dotnet run --project 1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj"
    fi
    
    export LoadTest__BaseUrl="http://localhost:5000"
    export LoadTest__Duration="00:02:00"  # 2 minutes for local testing
    export LoadTest__ConcurrentUsers="10"  # Reduced for local testing
    
    LOAD_TEST_ARGS=(
        "test" "$LOAD_TEST_PATH"
        "--no-build"
        "--configuration" "$CONFIGURATION"
        "--logger" "trx"
        "--logger" "console;verbosity=normal"
        "--results-directory" "$RESULTS_DIR/LoadTests"
    )
    
    if dotnet "${LOAD_TEST_ARGS[@]}"; then
        print_success "Load tests passed"
    else
        print_error "Load tests failed"
        FAILED_TESTS+=("Load Tests")
    fi
fi

# Generate Reports
if [ "$GENERATE_REPORTS" = true ]; then
    print_header "Generating Test Reports"
    
    # Generate coverage report
    if [ -d "$RESULTS_DIR/UnitTests" ]; then
        echo "Generating code coverage report..."
        COVERAGE_FILE=$(find "$RESULTS_DIR/UnitTests" -name "coverage.cobertura.xml" | head -1)
        if [ -n "$COVERAGE_FILE" ]; then
            # Install reportgenerator tool if not present
            dotnet tool install -g dotnet-reportgenerator-globaltool 2>/dev/null || true
            
            COVERAGE_REPORT="$RESULTS_DIR/CoverageReport"
            reportgenerator -reports:"$COVERAGE_FILE" -targetdir:"$COVERAGE_REPORT" -reporttypes:Html
            print_success "Coverage report generated at: $COVERAGE_REPORT/index.html"
            
            if [ "$OPEN_REPORTS" = true ]; then
                if command -v xdg-open > /dev/null; then
                    xdg-open "$COVERAGE_REPORT/index.html"
                elif command -v open > /dev/null; then
                    open "$COVERAGE_REPORT/index.html"
                fi
            fi
        fi
    fi
    
    # Generate test summary
    SUMMARY_PATH="$RESULTS_DIR/TestSummary.html"
    cat > "$SUMMARY_PATH" << EOF
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
        <p>Generated: $(date)</p>
        <p>Configuration: $CONFIGURATION</p>
    </div>
    
    <h2>Test Summary</h2>
    <p>Test execution completed with the following results:</p>
    
    <h2>Overall Result</h2>
EOF

    if [ ${#FAILED_TESTS[@]} -eq 0 ]; then
        echo '<p class="success">ALL TESTS PASSED</p>' >> "$SUMMARY_PATH"
    else
        echo '<p class="failure">FAILED TESTS: '"${FAILED_TESTS[*]}"'</p>' >> "$SUMMARY_PATH"
    fi

    cat >> "$SUMMARY_PATH" << EOF
</body>
</html>
EOF
    
    print_success "Test summary generated at: $SUMMARY_PATH"
    
    if [ "$OPEN_REPORTS" = true ]; then
        if command -v xdg-open > /dev/null; then
            xdg-open "$SUMMARY_PATH"
        elif command -v open > /dev/null; then
            open "$SUMMARY_PATH"
        fi
    fi
fi

# Final summary
END_TIME=$(date +%s)
TOTAL_DURATION=$((END_TIME - START_TIME))

print_header "Test Execution Complete"
echo "Total Duration: ${TOTAL_DURATION}s"
echo "Results Directory: $RESULTS_DIR"

if [ ${#FAILED_TESTS[@]} -eq 0 ]; then
    print_success "All tests passed successfully!"
    exit 0
else
    print_error "Some tests failed: ${FAILED_TESTS[*]}"
    exit 1
fi
