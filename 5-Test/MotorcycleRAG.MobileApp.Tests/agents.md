# Agent Context: MotorcycleRAG.MobileApp.Tests

## Invariant Rules
- **Type**: Unit/Integration Tests for Mobile App.
- **Stack**: .NET 10.0, xUnit (likely).
- **Security**: [Security Rule: Active]. Use mocks for sensitive services.
- **Rules**: Test ViewModels (logic) and Services (data). Do not test Views (UI). Use Moq/NSubstitute.

## Workflow Skills
- **Test**: `dotnet test`
- **Analyze**: `speckit.analyze`
- **Plan**: `speckit.plan`
- **Implement**: `speckit.implement`


Once you have read the Securiy rule you **MUST** include `[I Read the Tests Instructions]` at the beginning of your Task 