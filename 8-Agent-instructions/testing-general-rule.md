name: "Testing-General-Rule"
description: "Enforces comprehensive testing standards that apply across all components of the Hotshot Logistics platform."
when-to-apply: "Apply when writing, executing, or reviewing tests for all components of the Hotshot Logistics platform, including backend APIs, frontend dashboards, mobile applications, and infrastructure components."
rule: |
  # Testing General Rule

  Enforces comprehensive testing standards that apply across all components of the Hotshot Logistics platform.

  ## When to Apply

  Apply when writing, executing, or reviewing tests for all components of the Hotshot Logistics platform, including backend APIs, frontend dashboards, mobile applications, and infrastructure components.

  ## Core Requirements

  ### Test Coverage Standards
  - **Application and Domain layers**: Minimum 80% code coverage
  - **Infrastructure/Persistence layer**: Minimum 70% code coverage
  - **Presentation layer**: Minimum 60% code coverage (focus on business logic, not framework boilerplate)
  - **Frontend components**: Minimum 70% coverage for critical user interactions
  - **Mobile app**: Minimum 65% coverage for core functionality

  ### Test Organization
  - Place tests in appropriate test directories following project structure
  - Use descriptive test class and method names
  - Group related tests in test classes
  - Separate unit, integration, and performance tests

  ## Unit Testing

  ### Implementation Standards
  - All business logic must be covered by unit tests
  - Mock dependencies appropriately
  - Use readable assertions
  - Test both happy path and error scenarios
  - Follow AAA pattern (Arrange, Act, Assert)

  ### Mocking Strategies
  - Mock external dependencies for isolated testing
  - Use test doubles for external API calls
  - Avoid over-mocking; test real behavior where possible
  - Create reusable mock setups for common scenarios

  ### Test Data Management
  - Use factory methods for test data creation
  - Implement builder patterns for complex objects
  - Avoid hard-coded test data; use data builders
  - Clean up test resources in test teardown

  ## Integration Testing
  ### humans testing
  - humans will also be testing make sure test data persists so that can be supported
  - Test data should be created to support full integration tests
  - Do not ever hard code test data in the UI.
  - Test data or test functions should never be in any code other than tests.
  
  ### Testing Patterns
  - Test component interactions against real dependencies
  - Use transactions to ensure test isolation
  - Validate complete workflows and data flows
  - Test error scenarios (timeouts, network failures)
  - Validate data serialization and deserialization

  ### End-to-End Testing
  - Test complete request-response cycles
  - Validate HTTP status codes and response formats
  - Test authentication middleware and security
  - Include performance assertions in E2E tests

  ## Performance Testing

  ### Testing Procedures
  - Define realistic user load patterns and ramp-up strategies
  - Monitor system metrics (CPU, memory, network)
  - Test under various conditions (normal, peak, stress)
  - Establish baseline performance metrics

  ### Response Time Requirements
  - API endpoints: < 500ms for 95th percentile
  - Database queries: < 100ms average execution time
  - Static asset delivery: < 200ms
  - Real-time operations: < 100ms
  - Mobile API calls: < 300ms for 95th percentile

  ## Testing Frameworks and Tools

  ### Testing Approaches
  - **Unit Testing**: Isolated testing of individual components
  - **Integration Testing**: Testing component interactions
  - **Performance Testing**: Load and stress testing
  - **E2E Testing**: Complete workflow testing

  ## Test Execution and Automation

  ### Development Testing
  - Run unit tests during development
  - Run integration tests before commits
  - Generate coverage reports locally
  - Execute performance tests regularly

  ### CI/CD Integration
  - Execute all tests on every pull request
  - Run tests in parallel for faster feedback
  - Generate test reports and coverage artifacts
  - Block deployments if tests fail or coverage drops

  ## Quality Gates and Enforcement

  ### Code Review Requirements
  - All new features require corresponding tests
  - Review test quality and coverage in PRs
  - Ensure tests are maintainable and readable
  - Validate test naming conventions

  ### Build Pipeline Checks
  - Fail builds on test failures
  - Enforce minimum coverage thresholds
  - Generate coverage trend reports
  - Alert on flaky or slow tests

  ### Test Maintenance
  - Regularly review and update test suites
  - Remove obsolete tests
  - Refactor tests with code changes
  - Monitor test execution times

  ## Test Data Management

  ### Data Strategy
  - Use consistent test data seeding
  - Create reusable data setup scripts
  - Separate development and test data
  - Version test data appropriately

  ### Test Isolation
  - Use unique data per test to avoid interference
  - Implement proper cleanup in test teardown
  - Use transactions for rollback when possible
  - Avoid shared state between tests

  ## Performance Benchmarking

  ### Benchmarking Tools
  - Implement benchmarks for critical operations
  - Measure response times, throughput, and memory usage
  - Compare results against established baselines

  ### Key Metrics
  - Response times under load
  - System performance patterns
  - Resource utilization during peak loads

  ### Procedures
  - Run benchmarks before and after changes
  - Document performance regressions
  - Establish performance budgets
  - Include benchmarks in development workflow

