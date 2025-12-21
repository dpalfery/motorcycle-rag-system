name: "Code-Quality-Rule"
description: "Enforces comprehensive code quality practices that apply across all development activities in Hotshot Logistics."
when-to-apply:
"Apply to all development activities, regardless of technology stack or architectural layer."
rule: |

## Build Quality

### Zero Tolerance Policy
- Fix all build errors immediately. No exceptions.
- Resolve all warnings before merging or deploying.
- Treat warnings as errors in CI/CD to prevent technical debt.
- Never Ever hard code mock testing data in production code.
- NO PLACEHOLDER CODE: We are enterprise developers. Never create "not implemented" stubs, placeholder returns, or partial implementations. All code must be fully working, tested, and ready for deployment before it is written to the file.
- Only one object, class, interface, enum per file. never add two objects to a file, yuck!

### Configuration & Monitoring
- Set warning and error policies in project configuration files.
- Document the justification for any suppressed warnings.
- Implement CI/CD quality gates that fail on errors or warnings.
- Generate build reports that track warning trends and raise alerts.

### Development Workflow
- Configure IDEs to surface violations as warnings or errors.
- Enable auto-format on save and enforce formatting via pre-commit hooks.

### Enforcement
- Fail local builds on errors or violations.
- Archive CI/CD quality reports and block PRs that introduce new warnings.
- Require zero warnings at code review and document any accepted suppressions.

## Validation

### Input Validation
- Validate all user inputs on both client and server.
- Sanitize and normalize inputs before use.
- Enforce server-side validation for all public endpoints.

### Error Handling
- Return consistent error responses across all APIs.
- Use appropriate status codes for error conditions.
- Keep error messages useful but do not expose sensitive system details.
- Handle exceptions gracefully and log sanitized diagnostic information.

### Security Cross-Reference
- For security-specific validation, input-escaping, secrets handling, authentication, authorization, rate limiting, and incident logging, follow the authoritative security rules in [`security-general-rule.md`](.kilocode/rules/security-general-rule.md:1).

## Observability

### Structured Logging
- Use structured logging with semantic properties.
- Configure logging providers per environment.
- Log at appropriate levels: Debug, Information, Warning, Error, Critical.
- Use message templates and properties when logging structured data.
- For incident-response logging requirements (correlation IDs, retention, and forensic detail), follow [`security-general-rule.md`](.kilocode/rules/security-general-rule.md:1).

### Metrics
- Implement metrics collection for key KPIs: response times, error rates, throughput.
- Configure exporters for monitoring dashboards.

### Tracing
- Implement distributed tracing for request flows.
- Include contextual metadata in trace spans.

### Implementation Guidelines
- Configure observability during application startup.
- Use consistent naming for metrics and traces.
- Implement health checks that surface observability status.

## Testing Standards and Coverage Requirements

### Test Coverage Standards
- Application and Domain layers: Minimum 80% code coverage.
- Infrastructure/Persistence layer: Minimum 70% code coverage.
- Presentation layer: Minimum 60% code coverage.
- Frontend components: Minimum 70% coverage for critical user interactions.
- Mobile app: Minimum 65% coverage for core functionality.

### Test Organization
- Place tests in appropriate test directories following project structure.
- Use descriptive test class and method names.
- Group related tests in test classes.
- Separate unit, integration, and performance tests.

### Test Data Management
- Use factory methods for test data creation.
- Implement builder patterns for complex objects.
- Avoid hard-coded test data; use data builders.
- Clean up test resources in test teardown.

## Code Review Quality Gates

### Automated Checks
- Ensure builds pass without warnings.
- Ensure all tests pass.
- Ensure code coverage meets requirements.
- Ensure static analysis tools pass.









