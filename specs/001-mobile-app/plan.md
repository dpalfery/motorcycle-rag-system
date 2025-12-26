# Implementation Plan: .NET MAUI Mobile App

**Branch**: `001-mobile-app` | **Date**: 2025-12-26 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-mobile-app/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Build a cross-platform (.NET MAUI) mobile chat application for iOS, Android, and Windows that provides a ChatGPT-style interface for asking motorcycle-related questions. The app integrates with the existing MotorcycleRAG.API backend, requires Microsoft Entra External ID / B2C authentication, persists conversations locally with intelligent user memory extraction, and presents answers with tappable source citations. Users manage conversations via a chronological list with search and swipe-to-delete functionality.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Framework**: .NET MAUI (Multi-platform App UI)
**Primary Dependencies**:
- .NET MAUI (cross-platform UI framework)
- Microsoft.Identity.Client (MSAL for Entra External ID / B2C authentication)
- System.Net.Http + System.Text.Json (HTTP client and JSON serialization)
- SQLite or LiteDB (local conversation and user memory persistence)
- CommunityToolkit.Mvvm (MVVM helpers, commands, observables)

**Storage**:
- Local SQLite/LiteDB database for conversation history, user memory, and custom instructions
- 100MB storage limit with auto-pruning of oldest conversations
- Secure storage for authentication tokens (platform-specific: Keychain on iOS, EncryptedSharedPreferences on Android, Credential Locker on Windows)

**Testing**:
- xUnit for shared business logic and services
- Platform-specific UI tests (XCTest for iOS, Espresso for Android, Appium for Windows)
- Integration tests against MotorcycleRAG.API test endpoints

**Target Platform**:
- iOS 15+ (iPhone, iPad)
- Android 10+ (API Level 29+)
- Windows 10+ (desktop and tablet modes)

**Project Type**: Mobile (cross-platform .NET MAUI application)

**Performance Goals**:
- App launch to chat interface: < 2 seconds on mid-range devices
- API response display: < 3 seconds for 95th percentile queries
- Conversation list search: Results displayed within 1 second of input
- Smooth scrolling: 60 FPS for conversation history with 50+ messages

**Constraints**:
- 100MB total storage for conversation history with auto-pruning
- Must work offline for viewing cached conversations (no new queries)
- Platform-specific UI conventions must be respected (iOS HIG, Material Design, Fluent Design)
- Authentication required before any app functionality
- User memory extraction must happen before conversation pruning

**Scale/Scope**:
- Support for unlimited conversations within 100MB storage limit
- User memory: Persistent key-value pairs extracted from conversations
- Conversation list with search and delete functionality
- Tappable citations opening web URLs or PDF viewer (post-MVP)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Security (NON-NEGOTIABLE) - ✅ PASS

- **Secrets Management**:
  - ✅ Authentication tokens stored in platform-specific secure storage (Keychain/EncryptedSharedPreferences/Credential Locker)
  - ✅ API endpoint URLs configured via app settings, not hardcoded
  - ✅ No secrets in source control

- **Input Validation**:
  - ✅ User input (questions) validated before sending to API
  - ✅ API responses validated before display
  - ✅ Conversation search input sanitized

- **Secure Communication**:
  - ✅ HTTPS enforced for all API calls to MotorcycleRAG.API
  - ✅ Certificate pinning recommended for production

- **Authorization**:
  - ✅ Microsoft Entra External ID / B2C authentication required before app access
  - ✅ Bearer token included in all API requests
  - ✅ Token refresh handled automatically

- **Compliance Marker**: [Security Rule: Active]

### II. Clean Architecture - ⚠️ ADAPTED FOR MOBILE

**Mobile MVVM Architecture** (adapted from Clean Architecture principles):

- **Presentation Layer** (`Views/`, `Pages/`):
  - XAML pages and controls
  - Platform-specific UI implementations
  - Navigation logic

- **ViewModel Layer** (`ViewModels/`):
  - Commands and observable properties
  - UI state management
  - View-agnostic business logic

- **Application Layer** (`Services/`, `Managers/`):
  - API client service (MotorcycleRAG.API integration)
  - Authentication service (MSAL wrapper)
  - Conversation manager (CRUD operations)
  - User memory extraction service
  - Local storage service (SQLite/LiteDB wrapper)

- **Domain Layer** (`Models/`, `Entities/`):
  - ChatMessage, ConversationSession, UserMemory, SourceCitation
  - No dependencies on UI or platform code

- **Infrastructure Layer** (`Persistence/`, `Platform/`):
  - SQLite/LiteDB repository implementations
  - Platform-specific secure storage
  - HTTP client configuration

**Dependency Direction**: Views → ViewModels → Services/Managers → Models → Persistence
- ViewModels have NO direct platform dependencies
- Services use interfaces for testability

**Justification for Mobile Adaptation**:
.NET MAUI follows MVVM pattern which is a mobile-appropriate adaptation of Clean Architecture. The numbered folder structure from the constitution applies to backend services; mobile apps require platform-specific structure (Views, ViewModels, Services, Models) that MAUI enforces.

### III. Code Quality - ✅ PASS

- **Build Quality**: Zero warnings policy enforced
- **No Placeholder Code**: All implementations fully functional
- **Single Responsibility**: One view per file, one ViewModel per view, one service per concern
- **Async Patterns**: Async/await for all I/O (API calls, database, file system)
- **Naming & Style**: Follow C# and .NET MAUI conventions
- **Error Handling**: User-friendly error messages, structured logging for diagnostics

### IV. Testing - ✅ PASS

- **Coverage Target**: 80% for ViewModels, Services, and business logic
- **Test Organization**:
  - Unit tests: ViewModels, Services (with mocked dependencies)
  - Integration tests: API client against test endpoint
  - UI tests: Critical user flows (authentication, sending question, viewing conversation list)
- **Test-First Mindset**: Write tests alongside ViewModel and Service implementation
- **Performance Targets**:
  - API response display < 3 seconds
  - App launch < 2 seconds
  - Search results < 1 second

### V. Observability - ⚠️ MOBILE ADAPTATION

- **Structured Logging**:
  - Use platform logging (ILogger) for diagnostic events
  - Include correlation IDs in API requests
  - Log conversation operations (create, delete, prune)

- **Telemetry**:
  - Application Insights SDK for mobile (if enabled)
  - Track user actions (query sent, citation tapped, conversation deleted)
  - Performance metrics (API latency, app launch time)

- **Crash Reporting**:
  - Platform-specific crash analytics (AppCenter, Firebase Crashlytics)
  - Automatic crash logs with sanitized data

- **Metrics**:
  - Conversation count, storage usage, user memory entries
  - API success/failure rates

**Justification**: Mobile apps have different observability needs than backend services. Crash reporting and user analytics replace traditional health checks.

### VI. Resilience - ✅ PASS

- **Retry Policies**:
  - Polly or manual retry logic for API calls (3 retries with exponential backoff)
  - Handle transient network failures

- **Timeouts**:
  - 30-second timeout for API queries
  - 10-second timeout for authentication

- **Graceful Degradation**:
  - Offline mode: Display cached conversations, block new queries with clear message
  - API failure: Display error message with retry option

- **Rate Limiting**:
  - Display quota remaining after each query
  - Block new queries when daily limit reached with clear message

- **Error Messages**:
  - User-friendly messages ("Unable to connect. Check your internet connection.")
  - Detailed logs for diagnostics

### VII. Process & Workflow - ✅ PASS

- **Task Tracking**: Use todo lists for complex features
- **Code Review Checklist**:
  - [ ] Files organized by MVVM structure
  - [ ] No secrets in source control
  - [ ] Input validation implemented
  - [ ] Unit tests cover ViewModels and Services
  - [ ] Build passes with zero warnings
  - [ ] Platform-specific code documented

- **Commit Discipline**: Small, self-contained changes; build and test before commit
- **Windows Environment**: Development on Windows using Visual Studio 2022
- **Documentation**: Update README with setup instructions, authentication configuration

### GATE RESULT: ✅ PASS WITH JUSTIFICATIONS

**Justified Adaptations**:
1. **Mobile MVVM vs. Numbered Layers**: .NET MAUI enforces MVVM pattern; numbered layer structure applies to backend services, not mobile apps
2. **Observability**: Mobile apps use crash reporting and analytics instead of health check endpoints

## Project Structure

### Documentation (this feature)

```text
specs/001-mobile-app/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   └── api-client.md    # MotorcycleRAG.API integration contract
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
1-Presentation/MotorcycleRAG.MobileApp/
├── Platforms/
│   ├── Android/
│   ├── iOS/
│   ├── Windows/
│   └── MacCatalyst/
├── Views/
│   ├── AuthenticationPage.xaml
│   ├── ConversationListPage.xaml
│   ├── ChatPage.xaml
│   └── ProfilePage.xaml
├── ViewModels/
│   ├── AuthenticationViewModel.cs
│   ├── ConversationListViewModel.cs
│   ├── ChatViewModel.cs
│   └── ProfileViewModel.cs
├── Services/
│   ├── IAuthenticationService.cs
│   ├── AuthenticationService.cs
│   ├── IApiClient.cs
│   ├── MotorcycleRagApiClient.cs
│   ├── IConversationService.cs
│   ├── ConversationService.cs
│   ├── IUserMemoryService.cs
│   ├── UserMemoryService.cs
│   └── IStorageService.cs
│       └── StorageService.cs (SQLite/LiteDB wrapper)
├── Models/
│   ├── ChatMessage.cs
│   ├── ConversationSession.cs
│   ├── UserMemory.cs
│   ├── SourceCitation.cs
│   ├── ApiRequest.cs
│   └── ApiResponse.cs
├── Persistence/
│   ├── Entities/
│   │   ├── ConversationEntity.cs
│   │   ├── MessageEntity.cs
│   │   └── UserMemoryEntity.cs
│   └── Repositories/
│       ├── IConversationRepository.cs
│       ├── ConversationRepository.cs
│       ├── IUserMemoryRepository.cs
│       └── UserMemoryRepository.cs
├── Converters/
│   └── (XAML value converters)
├── Resources/
│   ├── Styles/
│   ├── Images/
│   └── Fonts/
├── MauiProgram.cs
└── App.xaml

5-Test/MotorcycleRAG.MobileApp.Tests/
├── ViewModels/
│   ├── ChatViewModelTests.cs
│   ├── ConversationListViewModelTests.cs
│   └── AuthenticationViewModelTests.cs
├── Services/
│   ├── ConversationServiceTests.cs
│   ├── UserMemoryServiceTests.cs
│   └── ApiClientTests.cs
└── Integration/
    └── ApiIntegrationTests.cs

5-Test/MotorcycleRAG.MobileApp.UITests/
├── iOS/
├── Android/
└── Windows/
```

**Structure Decision**: .NET MAUI enforces a specific project structure combining all platforms into a single project with platform-specific folders. The MVVM pattern organizes code into Views (XAML UI), ViewModels (presentation logic), Services (business logic and API integration), Models (domain entities), and Persistence (data access). This aligns with MAUI conventions while preserving Clean Architecture principles through dependency direction (Views → ViewModels → Services → Models → Persistence).

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Mobile MVVM vs. Numbered Layers | .NET MAUI enforces MVVM pattern with platform-specific structure; mobile apps require different organization than backend services | Backend numbered layer structure (1-Presentation, 2-Application, etc.) is designed for web APIs and services, not cross-platform mobile apps. MAUI's platform folders (Android/, iOS/, Windows/) and MVVM structure are framework requirements. |
| Observability without Health Checks | Mobile apps cannot expose HTTP health check endpoints; crash reporting and analytics provide equivalent visibility | Traditional health check endpoints (/health) don't apply to mobile client apps. Platform-specific crash analytics (AppCenter, Firebase) and Application Insights Mobile SDK provide appropriate mobile observability. |
