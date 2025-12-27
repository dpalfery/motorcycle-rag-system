# Tasks: .NET MAUI Mobile App

**Input**: Design documents from `/specs/001-mobile-app/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/api-client.md

**Tests**: Constitution Principle IV MANDATES 80% coverage for ViewModels and Services with Test-First Mindset (Red-Green-Refactor). Test tasks included per constitution requirement, which supersedes spec.md.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

This is a .NET MAUI mobile app following Clean Architecture patterns adapted for mobile MVVM:
- **Mobile App**: `1-Presentation/MotorcycleRAG.MobileApp/`
- **Tests**: `5-Test/MotorcycleRAG.MobileApp.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [x] T001 Create MAUI project structure at 1-Presentation/MotorcycleRAG.MobileApp
- [x] T002 Initialize .NET MAUI project with required NuGet packages (Microsoft.Maui.Controls, Microsoft.Identity.Client, CommunityToolkit.Mvvm, sqlite-net-pcl, Microsoft.Extensions.Http.Polly)
- [x] T003 [P] Create appsettings.json and appsettings.Development.json for API and authentication configuration
- [x] T004 [P] Configure platform-specific settings in Platforms/iOS/Info.plist, Platforms/Android/AndroidManifest.xml, Platforms/Windows/Package.appxmanifest
- [x] T005 Create test project at 5-Test/MotorcycleRAG.MobileApp.Tests with xUnit and Moq packages
- [x] T006 [P] Setup MauiProgram.cs with dependency injection configuration

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T007 [P] Create domain models in 1-Presentation/MotorcycleRAG.MobileApp/Models/ChatMessage.cs
- [x] T008 [P] Create domain models in 1-Presentation/MotorcycleRAG.MobileApp/Models/ConversationSession.cs
- [x] T009 [P] Create domain models in 1-Presentation/MotorcycleRAG.MobileApp/Models/SourceCitation.cs
- [x] T010 [P] Create domain models in 1-Presentation/MotorcycleRAG.MobileApp/Models/UserMemory.cs
- [x] T011 [P] Create domain models in 1-Presentation/MotorcycleRAG.MobileApp/Models/UserProfile.cs
- [x] T012 [P] Create API request/response models in 1-Presentation/MotorcycleRAG.MobileApp/Models/QueryRequest.cs and QueryResponse.cs
- [x] T013 [P] Create SQLite persistence entities in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/Entities/ConversationEntity.cs
- [x] T014 [P] Create SQLite persistence entities in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/Entities/MessageEntity.cs
- [x] T015 [P] Create SQLite persistence entities in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/Entities/CitationEntity.cs
- [x] T016 [P] Create SQLite persistence entities in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/Entities/UserMemoryEntity.cs
- [x] T017 Setup SQLite database schema and migrations in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/DatabaseContext.cs
- [x] T018 [P] Implement IConversationRepository interface in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/Repositories/IConversationRepository.cs
- [x] T019 [P] Implement IMessageRepository interface in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/Repositories/IMessageRepository.cs
- [x] T020 [P] Implement ICitationRepository interface in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/Repositories/ICitationRepository.cs
- [x] T021 [P] Implement IUserMemoryRepository interface in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/Repositories/IUserMemoryRepository.cs
- [x] T022 Implement ConversationRepository with CRUD operations in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/Repositories/ConversationRepository.cs
- [x] T023 [P] Implement MessageRepository with CRUD operations in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/Repositories/MessageRepository.cs
- [x] T024 [P] Implement CitationRepository with CRUD operations in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/Repositories/CitationRepository.cs
- [x] T025 [P] Implement UserMemoryRepository with CRUD operations in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/Repositories/UserMemoryRepository.cs
- [x] T026 [P] Create IAuthenticationService interface in 1-Presentation/MotorcycleRAG.MobileApp/Services/IAuthenticationService.cs
- [x] T027 Implement AuthenticationService with MSAL integration in 1-Presentation/MotorcycleRAG.MobileApp/Services/AuthenticationService.cs
- [x] T028 [P] Create IApiClient interface in 1-Presentation/MotorcycleRAG.MobileApp/Services/IApiClient.cs
- [x] T029 Implement MotorcycleRagApiClient with HttpClient and Polly resilience policies in 1-Presentation/MotorcycleRAG.MobileApp/Services/MotorcycleRagApiClient.cs
- [x] T030 [P] Create IStorageService interface in 1-Presentation/MotorcycleRAG.MobileApp/Services/IStorageService.cs
- [x] T031 Implement StorageService for managing total storage size and pruning in 1-Presentation/MotorcycleRAG.MobileApp/Services/StorageService.cs
- [x] T032 [P] Create data mapping utilities in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/Mappers/ConversationMapper.cs
- [x] T033 [P] Create data mapping utilities in 1-Presentation/MotorcycleRAG.MobileApp/Persistence/Mappers/MessageMapper.cs
- [x] T034 [P] Create custom exception classes in 1-Presentation/MotorcycleRAG.MobileApp/Exceptions/ApiException.cs (AuthenticationException, RateLimitException, ValidationException)
- [x] T035 Register all services in MauiProgram.cs dependency injection container

**⚠️ Test Infrastructure (Constitution Principle IV - Test-First Mindset)**:

- [x] T035a [P] Create test infrastructure with xUnit, Moq, and FluentAssertions in 5-Test/MotorcycleRAG.MobileApp.Tests
- [x] T035b [P] Create MockConversationRepository for unit testing in 5-Test/MotorcycleRAG.MobileApp.Tests/Mocks/MockConversationRepository.cs
- [x] T035c [P] Create MockApiClient for unit testing in 5-Test/MotorcycleRAG.MobileApp.Tests/Mocks/MockApiClient.cs
- [x] T035d [P] Create MockAuthenticationService for unit testing in 5-Test/MotorcycleRAG.MobileApp.Tests/Mocks/MockAuthenticationService.cs

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 7 - Cross-Platform Experience (Priority: P1) 🎯 MVP FOUNDATION

**Goal**: Ensure core app structure works on iOS, Android, and Windows

**Independent Test**: Can build and run the app on iOS simulator, Android emulator, and Windows; authentication flow works on all platforms

- [x] T036 [P] [US7] Configure iOS-specific settings for authentication redirect in Platforms/iOS/Info.plist
- [x] T037 [P] [US7] Configure Android-specific settings for authentication redirect in Platforms/Android/AndroidManifest.xml
- [x] T038 [P] [US7] Configure Windows-specific settings in Platforms/Windows/Package.appxmanifest
- [x] T039 [P] [US7] Create platform-specific styles in Resources/Styles/Styles.xaml with OnPlatform for iOS, Android, Windows
- [x] T040 [P] [US7] Create AuthenticationViewModel with MVVM toolkit in ViewModels/AuthenticationViewModel.cs
- [x] T041 [US7] Create AuthenticationPage XAML UI in Views/AuthenticationPage.xaml with platform-specific styling
- [x] T042 [US7] Implement sign-in/sign-out commands in AuthenticationViewModel
- [x] T043 [US7] Add navigation logic to App.xaml.cs to route authenticated users to ConversationListPage
- [x] T044 [US7] Test authentication flow on iOS simulator, Android emulator, and Windows desktop

**Unit Tests for US7 (Test-First: Write BEFORE/ALONGSIDE implementation)**:

- [x] T044a [P] [US7] Write AuthenticationViewModelTests for sign-in/sign-out commands in 5-Test/MotorcycleRAG.MobileApp.Tests/ViewModels/AuthenticationViewModelTests.cs
- [x] T044b [P] [US7] Write AuthenticationServiceTests for MSAL token acquisition in 5-Test/MotorcycleRAG.MobileApp.Tests/Services/AuthenticationServiceTests.cs

**Checkpoint**: At this point, authentication works across all platforms and app can launch

---

## Phase 4: User Story 1 - Ask Motorcycle Questions via Chat (Priority: P1) 🎯 MVP CORE

**Goal**: Mobile chat interface for asking natural language questions and receiving conversational answers with tappable citations

**Independent Test**: Launch app, authenticate, start new conversation, type "What's the horsepower of a 2023 Yamaha R1?", receive answer within 3 seconds with tappable citations; web citations open browser, PDF citations show message

- [x] T045 [P] [US1] Create IConversationService interface in Services/IConversationService.cs
- [x] T046 [US1] Implement ConversationService with message save/load operations in Services/ConversationService.cs
- [x] T047 [P] [US1] Create ChatViewModel with CommunityToolkit.Mvvm in ViewModels/ChatViewModel.cs
- [x] T048 [US1] Implement QuestionText observable property and SendQuestionCommand in ChatViewModel
- [x] T049 [US1] Implement Messages observable collection and IsSending state in ChatViewModel
- [x] T050 [US1] Create ChatPage XAML UI with CollectionView for messages and Entry for input in Views/ChatPage.xaml
- [x] T051 [P] [US1] Create XAML value converter for message sender to bubble color in Converters/SenderToColorConverter.cs
- [x] T052 [P] [US1] Create XAML DataTemplate for user messages in Views/ChatPage.xaml
- [x] T053 [P] [US1] Create XAML DataTemplate for system messages with citations in Views/ChatPage.xaml
- [x] T054 [US1] Implement API call logic in ChatViewModel.SendQuestionAsync with error handling
- [x] T055 [US1] Add loading indicator binding to IsSending property in ChatPage.xaml
- [x] T056 [US1] Implement citation tap handler for web URLs to open in browser in ChatViewModel
- [x] T057 [US1] Implement citation tap handler for PDF citations to show "view in app" or "coming soon" message in ChatViewModel
- [x] T058 [US1] Add validation for empty/whitespace questions in ChatViewModel
- [x] T059 [US1] Implement error message display for API failures in ChatPage.xaml
- [x] T060 [US1] Add "no results" message handling in ChatViewModel when system cannot find information
- [x] T061 [US1] Implement conversation persistence logic to save messages locally after each send in ChatViewModel
- [x] T062 [US1] Add platform-specific touch gesture support for citation tapping in ChatPage.xaml

**Unit Tests for US1 (Test-First: Write BEFORE/ALONGSIDE implementation)**:

- [x] T062a [P] [US1] Write ChatViewModelTests for SendQuestionCommand with successful API response in 5-Test/MotorcycleRAG.MobileApp.Tests/ViewModels/ChatViewModelTests.cs
- [x] T062b [P] [US1] Write ChatViewModelTests for SendQuestionCommand with API failure/timeout in 5-Test/MotorcycleRAG.MobileApp.Tests/ViewModels/ChatViewModelTests.cs
- [x] T062c [P] [US1] Write ChatViewModelTests for citation tap handling (web and PDF) in 5-Test/MotorcycleRAG.MobileApp.Tests/ViewModels/ChatViewModelTests.cs
- [x] T062d [P] [US1] Write ConversationServiceTests for message save/load operations in 5-Test/MotorcycleRAG.MobileApp.Tests/Services/ConversationServiceTests.cs
- [x] T062e [P] [US1] Write ConversationRepositoryTests for CRUD operations and storage calculation in 5-Test/MotorcycleRAG.MobileApp.Tests/Persistence/ConversationRepositoryTests.cs

**Integration Tests for US1**:

- [ ] T062f [US1] Write ApiClientIntegrationTests for POST /api/motorcycles/query with real test endpoint in 5-Test/MotorcycleRAG.MobileApp.Tests/Integration/ApiClientIntegrationTests.cs

**Checkpoint**: At this point, User Story 1 should be fully functional - users can ask questions and receive answers with tappable citations

---

## Phase 5: User Story 6 - Conversation Management and Navigation (Priority: P1) 🎯 MVP

**Goal**: Chronological conversation list with search, tap-to-resume, and swipe-to-delete

**Independent Test**: Create multiple conversations, close app, reopen, verify all appear sorted by recency; search for content, tap to resume, swipe to delete

- [x] T063 [P] [US6] Create ConversationListViewModel with CommunityToolkit.Mvvm in ViewModels/ConversationListViewModel.cs
- [x] T064 [US6] Implement Conversations observable collection in ConversationListViewModel
- [x] T065 [US6] Implement LoadConversationsCommand to fetch all conversations sorted by UpdatedAt DESC in ConversationListViewModel
- [x] T066 [P] [US6] Implement SearchQuery observable property and SearchCommand in ConversationListViewModel
- [x] T067 [P] [US6] Implement DeleteConversationCommand with swipe-to-delete support in ConversationListViewModel
- [x] T068 [P] [US6] Implement NewConversationCommand to create new chat session in ConversationListViewModel
- [x] T069 [US6] Create ConversationListPage XAML UI with CollectionView in Views/ConversationListPage.xaml
- [x] T070 [US6] Add SearchBar with binding to SearchQuery in ConversationListPage.xaml
- [x] T071 [US6] Create conversation item DataTemplate with title and preview in ConversationListPage.xaml
- [x] T072 [US6] Implement SwipeView with delete option for each conversation item in ConversationListPage.xaml
- [x] T073 [US6] Add tap gesture to conversation items to navigate to ChatPage with conversation ID in ConversationListPage.xaml
- [x] T074 [US6] Implement SQLite FTS5 full-text search for conversation content in ConversationRepository
- [x] T075 [US6] Add debounced search (500ms delay) to avoid excessive queries in ConversationListViewModel
- [x] T076 [US6] Implement offline message display when no connectivity and user tries to ask new question in ChatViewModel
- [x] T077 [US6] Update App.xaml.cs navigation to show ConversationListPage as home screen after authentication
- [x] T078 [US6] Add "+" button for new conversation in ConversationListPage.xaml
- [x] T079 [US6] Implement conversation auto-titling from first 50 characters of first question in ConversationService

**Unit Tests for US6 (Test-First: Write BEFORE/ALONGSIDE implementation)**:

- [x] T079a [P] [US6] Write ConversationListViewModelTests for LoadConversationsCommand in 5-Test/MotorcycleRAG.MobileApp.Tests/ViewModels/ConversationListViewModelTests.cs
- [x] T079b [P] [US6] Write ConversationListViewModelTests for SearchCommand with FTS5 queries in 5-Test/MotorcycleRAG.MobileApp.Tests/ViewModels/ConversationListViewModelTests.cs
- [x] T079c [P] [US6] Write ConversationListViewModelTests for DeleteConversationCommand in 5-Test/MotorcycleRAG.MobileApp.Tests/ViewModels/ConversationListViewModelTests.cs

**Checkpoint**: At this point, User Story 6 should be fully functional - users can manage all their conversations with search and delete

---

## Phase 6: User Story 2 - Multi-Turn Conversations (Priority: P2)

**Goal**: Multi-turn conversations with context from previous questions in the session

**Independent Test**: Ask initial question, receive answer, ask follow-up that references previous context (e.g., "What about the 2024 model?"), verify system understands context

- [x] T080 [US2] Add SessionId and PreviousQueries properties to ChatViewModel
- [x] T081 [US2] Update SendQuestionCommand to include conversation context in API request in ChatViewModel
- [x] T082 [US2] Implement conversation history loading when ChatPage is opened with existing conversation ID in ChatViewModel
- [x] T083 [US2] Add scroll-to-bottom behavior when new messages are added in ChatPage.xaml
- [x] T084 [US2] Implement "Start New Chat" command to clear context and begin fresh conversation in ChatViewModel
- [x] T085 [US2] Update ConversationService to maintain session state (SessionId, PreviousQueries) for multi-turn support
- [x] T086 [US2] Add conversation context display indicator in ChatPage.xaml to show multi-turn mode is active

**Unit Tests for US2 (Test-First: Write BEFORE/ALONGSIDE implementation)**:

- [x] T086a [P] [US2] Write ChatViewModelTests for multi-turn context maintenance (SessionId, PreviousQueries) in 5-Test/MotorcycleRAG.MobileApp.Tests/ViewModels/ChatViewModelTests.cs
- [x] T086b [P] [US2] Write ConversationServiceTests for session state persistence in 5-Test/MotorcycleRAG.MobileApp.Tests/Services/ConversationServiceTests.cs

**Checkpoint**: At this point, User Stories 1, 2, and 6 should all work - users have multi-turn conversations that persist and can be managed

---

## Phase 7: User Story 3 - Personalized Responses via User Memory (Priority: P2)

**Goal**: App remembers user information from conversations and uses it for personalization; memory persists even after conversation pruning

**Independent Test**: Have conversation mentioning "I have a 2023 Yamaha R1M", later start new conversation asking "What oil should I use?", verify system references R1M without being told again

- [x] T087 [P] [US3] Create IUserMemoryService interface in Services/IUserMemoryService.cs
- [x] T088 [US3] Implement UserMemoryService with extraction patterns in Services/UserMemoryService.cs
- [x] T089 [US3] Create keyword pattern matching logic for memory extraction categories (motorcycles_owned, riding_style, expertise_level, maintenance_preference) in UserMemoryService
- [x] T090 [US3] Implement ExtractFromConversationAsync method using regex patterns in UserMemoryService
- [x] T091 [US3] Add user memory extraction trigger before conversation pruning in StorageService
- [x] T092 [US3] Update SendQuestionCommand to include user memory in API request context in ChatViewModel
- [x] T093 [US3] Implement GetActiveMemoriesAsync method to load all active user memory in UserMemoryService
- [x] T094 [P] [US3] Create ProfileViewModel with CommunityToolkit.Mvvm in ViewModels/ProfileViewModel.cs
- [x] T095 [US3] Implement UserMemories observable collection in ProfileViewModel
- [x] T096 [US3] Implement LoadMemoriesCommand and EditMemoryCommand in ProfileViewModel
- [x] T097 [US3] Implement DeactivateMemoryCommand and ClearAllMemoriesCommand in ProfileViewModel
- [x] T098 [US3] Create ProfilePage XAML UI to display user memory items in Views/ProfilePage.xaml
- [x] T099 [US3] Add CollectionView with user memory items (category, value, extracted date) in ProfilePage.xaml
- [x] T100 [US3] Implement edit/delete gestures for memory items in ProfilePage.xaml
- [x] T101 [US3] Add navigation to ProfilePage from ConversationListPage (settings/profile button) in ConversationListPage.xaml
- [x] T102 [US3] Implement conflict resolution for duplicate memory categories (update existing, deactivate old) in UserMemoryService
- [x] T103 [US3] Update StorageService pruning logic to extract and persist user memory before deleting conversations

**Unit Tests for US3 (Test-First: Write BEFORE/ALONGSIDE implementation)**:

- [x] T103a [P] [US3] Write UserMemoryServiceTests for ExtractFromConversationAsync with regex patterns in 5-Test/MotorcycleRAG.MobileApp.Tests/Services/UserMemoryServiceTests.cs
- [x] T103b [P] [US3] Write UserMemoryServiceTests for conflict resolution (duplicate categories) in 5-Test/MotorcycleRAG.MobileApp.Tests/Services/UserMemoryServiceTests.cs
- [x] T103c [P] [US3] Write ProfileViewModelTests for LoadMemoriesCommand and EditMemoryCommand in 5-Test/MotorcycleRAG.MobileApp.Tests/ViewModels/ProfileViewModelTests.cs
- [x] T103d [P] [US3] Write StorageServiceTests for pruning logic with memory extraction in 5-Test/MotorcycleRAG.MobileApp.Tests/Services/StorageServiceTests.cs

**Checkpoint**: At this point, User Stories 1, 2, 3, and 6 work - app learns from conversations and personalizes responses

---

## Phase 8: User Story 5 - View PDF Page References (Priority: P3 - Post-MVP Enhancement)

**Goal**: Optionally view PDF page in-app when citation is tapped

**Independent Test**: Ask question resulting in PDF citation, tap citation, view referenced PDF page with pinch-to-zoom; navigate back to chat

**Note**: This is a post-MVP enhancement feature

- [x] T104 [P] [US5] Create IPdfViewerService interface in Services/IPdfViewerService.cs
- [x] T105 [US5] Implement PdfViewerService with platform-specific PDF rendering in Services/PdfViewerService.cs
- [x] T106 [P] [US5] Create PdfViewerViewModel with CommunityToolkit.Mvvm in ViewModels/PdfViewerViewModel.cs
- [x] T107 [US5] Implement PDF page loading logic in PdfViewerViewModel
- [x] T108 [US5] Create PdfViewerPage XAML UI with pinch-to-zoom support in Views/PdfViewerPage.xaml
- [x] T109 [US5] Update citation tap handler in ChatViewModel to navigate to PdfViewerPage with document ID and page number
- [x] T110 [US5] Implement navigation back to ChatPage from PdfViewerPage
- [x] T111 [US5] Add platform-specific PDF rendering for iOS using PDFKit in Platforms/iOS/PdfRenderer.cs
- [x] T112 [US5] Add platform-specific PDF rendering for Android using PdfRenderer in Platforms/Android/PdfRenderer.cs
- [x] T113 [US5] Add platform-specific PDF rendering for Windows using PDF rendering APIs in Platforms/Windows/PdfRenderer.cs
- [x] T114 [US5] Implement PDF caching to avoid re-downloading for previously viewed pages in PdfViewerService

**Unit Tests for US5 (Test-First: Write BEFORE/ALONGSIDE implementation)**:

- [x] T114a [P] [US5] Write PdfViewerViewModelTests for PDF page loading in 5-Test/MotorcycleRAG.MobileApp.Tests/ViewModels/PdfViewerViewModelTests.cs
- [x] T114b [P] [US5] Write PdfViewerServiceTests for platform-specific rendering mocks in 5-Test/MotorcycleRAG.MobileApp.Tests/Services/PdfViewerServiceTests.cs

**Checkpoint**: At this point, all user stories including post-MVP PDF viewing work fully

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [x] T115 [P] Implement 100MB storage limit enforcement with auto-pruning oldest conversations in StorageService
- [x] T116 [P] Add storage usage display in ProfilePage showing current usage vs 100MB limit
- [x] T117 [P] Implement rate limit display showing plan (Free/Plus/Pro) and remaining daily requests in ProfilePage
- [x] T118 [P] Add rate limit error handling to display clear message when daily limit reached in ChatViewModel
- [x] T119 [P] Optimize CollectionView performance with virtualization and data template caching in ChatPage.xaml and ConversationListPage.xaml
- [x] T120 [P] Add platform-specific UI refinements for iOS (SF Pro font, swipe gestures, bottom padding) in Resources/Styles/Styles.xaml
- [x] T121 [P] Add platform-specific UI refinements for Android (Roboto font, Material Design FAB) in Resources/Styles/Styles.xaml
- [x] T122 [P] Add platform-specific UI refinements for Windows (Segoe UI font, Fluent Design) in Resources/Styles/Styles.xaml
- [x] T123 [P] Implement orientation change handling for portrait and landscape in all pages
- [x] T124 [P] Add structured logging with correlation IDs for all API calls in MotorcycleRagApiClient
- [x] T125 [P] Implement Application Insights SDK integration for mobile telemetry (optional) in MauiProgram.cs
- [x] T126 [P] Add crash reporting configuration (AppCenter or Firebase Crashlytics) in MauiProgram.cs
- [x] T127 [P] Create README.md with setup instructions and authentication configuration
- [x] T128 Validate quickstart.md steps by following setup instructions on clean machine
- [x] T129 [P] Code cleanup and refactoring for consistent naming and style
- [x] T130 [P] Verify 80% code coverage target for ViewModels and Services using coverage reports; performance testing for 60 FPS scrolling with 50+ messages in conversation
- [x] T131 [P] Security review: verify no secrets in appsettings.json templates, input sanitization for search queries (FTS5 injection prevention), HTTPS enforcement in HttpClient config, MSAL SecureStorage audit
- [x] T132 Run build with zero warnings policy enforcement
- [x] T133 Final validation of all user stories on iOS, Android, and Windows

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Story 7 - Cross-Platform (Phase 3)**: Depends on Foundational completion - Foundation for all other stories
- **User Story 1 - Chat (Phase 4)**: Depends on Foundational and US7 completion - Core MVP functionality
- **User Story 6 - Conversation Management (Phase 5)**: Depends on Foundational and US1 completion - Completes MVP
- **User Story 2 - Multi-Turn (Phase 6)**: Depends on Foundational, US1, and US6 completion - Can start after MVP
- **User Story 3 - User Memory (Phase 7)**: Depends on Foundational, US1, US2, and US6 completion - Requires conversation history
- **User Story 5 - PDF Viewer (Phase 8)**: Depends on Foundational and US1 completion - Post-MVP enhancement
- **Polish (Phase 9)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 7 (P1 - Cross-Platform)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 1 (P1 - Chat)**: Depends on US7 completion - Core chat functionality
- **User Story 6 (P1 - Conversation Management)**: Depends on US1 completion - Requires chat to be working
- **User Story 2 (P2 - Multi-Turn)**: Depends on US1 and US6 completion - Enhances chat with context
- **User Story 3 (P2 - User Memory)**: Depends on US1, US2, and US6 completion - Requires conversation history and context
- **User Story 5 (P3 - PDF Viewer)**: Depends on US1 completion - Post-MVP enhancement to citation tapping

### Within Each User Story

- Models and interfaces before implementations
- Repositories before services
- Services before ViewModels
- ViewModels before Views
- Core functionality before UI refinements
- Story complete before moving to next priority

### Parallel Opportunities

- **Phase 1 (Setup)**: T001-T006 can run in parallel
- **Phase 2 (Foundational)**:
  - T007-T012 (domain models) can run in parallel
  - T013-T016 (persistence entities) can run in parallel
  - T018-T021 (repository interfaces) can run in parallel
  - T023-T025 (repository implementations) can run in parallel after T022 completes
  - T026, T028, T030 (service interfaces) can run in parallel
  - T032-T033 (mappers) can run in parallel
  - T035a-T035d (test mocks) can run in parallel
- **Phase 3 (US7)**: T036-T039 (platform configs) can run in parallel, T040-T041 can run in parallel, T044a-T044b (tests) can run in parallel
- **Phase 4 (US1)**: T045, T047, T051, T052, T053 can run in parallel initially; T062a-T062e (tests) can run in parallel
- **Phase 5 (US6)**: T063, T066, T067, T068 can run in parallel initially; T079a-T079c (tests) can run in parallel
- **Phase 6 (US2)**: T086a-T086b (tests) can run in parallel
- **Phase 7 (US3)**: T087, T094 can run in parallel initially; T103a-T103d (tests) can run in parallel
- **Phase 8 (US5)**: T114a-T114b (tests) can run in parallel
- **Phase 9 (Polish)**: Most polish tasks (T115-T132) can run in parallel

---

## Parallel Example: User Story 1 (Chat)

```bash
# Launch models and interfaces in parallel:
Task: "Create IConversationService interface in Services/IConversationService.cs"
Task: "Create ChatViewModel with CommunityToolkit.Mvvm in ViewModels/ChatViewModel.cs"
Task: "Create XAML value converter for message sender to bubble color in Converters/SenderToColorConverter.cs"
Task: "Create XAML DataTemplate for user messages in Views/ChatPage.xaml"
Task: "Create XAML DataTemplate for system messages with citations in Views/ChatPage.xaml"
```

---

## Implementation Strategy

### MVP First (User Stories 1, 6, 7 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 7 (Cross-Platform Experience)
4. Complete Phase 4: User Story 1 (Ask Questions via Chat)
5. Complete Phase 5: User Story 6 (Conversation Management)
6. **STOP and VALIDATE**: Test MVP independently on all platforms
7. Deploy/demo if ready

**MVP Scope**: Authentication + Chat + Conversation Management + Cross-Platform = Full P1 functionality

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 7 (Cross-Platform) → Test on all platforms → Platform foundation ready
3. Add User Story 1 (Chat) → Test independently → Core chat works
4. Add User Story 6 (Conversation Management) → Test independently → Deploy/Demo (MVP!)
5. Add User Story 2 (Multi-Turn) → Test independently → Deploy/Demo
6. Add User Story 3 (User Memory) → Test independently → Deploy/Demo
7. Add User Story 5 (PDF Viewer) → Test independently → Deploy/Demo (Post-MVP)
8. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 7 (Cross-Platform)
   - Once US7 is done:
     - Developer A: User Story 1 (Chat)
     - Developer B: User Story 6 (Conversation Management) - can start some prep
3. Once US1 + US6 done:
   - Developer A: User Story 2 (Multi-Turn)
   - Developer B: User Story 3 (User Memory)
   - Developer C: User Story 5 (PDF Viewer) - can proceed in parallel
4. Stories complete and integrate independently

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- MVP = US1 + US6 + US7 (authentication, chat, conversation management, cross-platform)
- US2 and US3 are P2 enhancements, US5 is P3 post-MVP
- Tests are REQUIRED per Constitution Principle IV (80% coverage for ViewModels/Services, Test-First Mindset)
- Total tasks: 157 (133 implementation + 24 test tasks)
- Avoid: vague tasks, same file conflicts, cross-story dependencies that break independence
