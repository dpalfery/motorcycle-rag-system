name: "Process-General-Rule"
description: "Enforces development process guidelines for Hotshot Logistics, including task tracking and command-line operations."
when-to-apply:
"Apply when managing tasks, creating status files, or executing development workflows in .NET projects."
rule: |

### Task Tracking
- Create todo lists and status tracking markdown files in the 6-docs/status folder.
- Use these files to maintain state and memory during complex tasks.
- After task completion, ask the user (or assigning agent) if they want to delete the created files.

### Development Workflows
- Use appropriate CLI commands for development, building, testing, database management, package management, publishing, and containerization.
- Execute commands from the project root directory.
- Ensure proper configuration (e.g., connection strings, SDK versions) before running database or deployment commands.

### Code Review Checklists

#### Architecture Compliance
- [ ] Files placed in correct numbered folder structure
- [ ] Dependencies flow downward (Domain → Application → Infrastructure)
- [ ] Repository pattern implemented for data access
- [ ] CQRS pattern followed for commands/queries
- [ ] Clean Architecture principles maintained

#### Code Quality
- [ ] StyleCop rules pass without warnings
- [ ] No build errors or warnings
- [ ] Async/await used for all I/O operations
- [ ] Proper error handling with meaningful exceptions
- [ ] XML documentation on public APIs

#### Security
- [ ] No secrets committed to source control
- [ ] Input validation implemented
- [ ] SQL injection prevention (parameterized queries)
- [ ] Authentication/authorization properly configured
- [ ] CORS and other security headers set

#### Testing
- [ ] Unit tests cover new functionality
- [ ] Integration tests for data access
- [ ] Test coverage meets minimum requirements
- [ ] Mock external dependencies appropriately
- [ ] Test data properly managed

#### Documentation
- [ ] Code changes documented in commit messages
- [ ] API changes reflected in OpenAPI specs
- [ ] README updated for new features
- [ ] Migration notes included for breaking changes

### Task Management Standards

#### When to Create Todo Lists
- Complex tasks involving multiple steps or files
- Tasks requiring coordination across architectural layers
- Multi-day development efforts
- Tasks with unclear requirements needing iterative refinement
- When working with unfamiliar codebase sections
#### Todo List Format
- Use markdown checklist format with [ ] for pending, [x] for completed, [-] for in progress
- List tasks in logical execution order
- Include specific, actionable descriptions
- Break down large tasks into smaller, verifiable steps
- Update status immediately after completing each item



#### Integration with Memory Bank
- Update memory bank context.md after significant task completion
- Document new patterns or procedures discovered during tasks
- Add repetitive tasks to tasks.md for future reference
- Maintain project knowledge continuity across sessions

## References
See 6-Docs/process-examples.md for detailed command examples and usage patterns.
See 6-Docs/ado-net-examples.md for data access implementation examples.
See 6-Docs/architecture-examples.md for architectural pattern implementations.
See 6-Docs/code-quality-examples.md for code quality and testing examples.
See 6-Docs/security-examples.md for security implementation patterns.