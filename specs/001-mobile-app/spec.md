# Feature Specification: .NET MAUI Mobile App

**Feature Branch**: `001-mobile-app`
**Created**: 2025-12-26
**Status**: Draft
**Input**: User description: "I would like a mobile app built in .NET MAUI. It should run on iOS and Android and Windows. It should be a mobile version of the webUI for phones"

## Clarifications

### Session 2025-12-26

- Q: Should the MVP include user authentication (Microsoft Entra External ID / B2C with social sign-in) or operate anonymously? → A: Full authentication required from MVP launch (Microsoft Entra External ID / B2C with social sign-in) for all users
- Q: Should conversation sessions persist across app restarts or are they session-only? → A: Full persistence: conversations survive app restart and can be continued where left off
- Q: Are source citations interactive (tappable) or static text only? → A: Tappable citations: web URLs open in browser, PDF citations show "view in app" option (or message if post-MVP feature not yet available)
- Q: What are the storage limits for persisted conversations and when should old conversations be pruned? → A: Storage-based limit up to 100MB total with auto-pruning of oldest conversations when limit reached; system should extract and persist user information (memory/custom instructions) from conversations before pruning to maintain personalization context
- Q: How should users navigate between and manage multiple persisted conversations? → A: Chronological list with swipe-to-delete and search: home screen shows list of conversations sorted by recency, tap to open, swipe to delete, search bar to filter

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ask Motorcycle Questions via Chat (Priority: P1)

As a motorcycle enthusiast using my phone, I want to ask natural language questions about motorcycles and receive conversational answers (like ChatGPT), so I can get quick, accurate information while on-the-go without needing to search multiple sources myself.

**Why this priority**: This is the core value proposition - a mobile chat interface for getting motorcycle information through natural conversation. Without this, the app has no purpose. This represents the minimum viable product.

**Independent Test**: Can be fully tested by launching the app, typing a natural language question (e.g., "What's the horsepower of a 2023 Yamaha R1?"), and receiving a conversational answer with source citations. Delivers immediate value by providing mobile access to the RAG system.

**Acceptance Scenarios**:

1. **Given** the app is launched on a phone, **When** I type a natural language question about a motorcycle, **Then** I receive a conversational answer in natural language within 3 seconds
2. **Given** I receive an answer, **When** I view the response, **Then** factual claims include tappable source citations (e.g., manual references, web sources, dataset names)
3. **Given** an answer includes a web URL citation, **When** I tap on it, **Then** the URL opens in my device's default browser
4. **Given** an answer includes a PDF manual citation, **When** I tap on it, **Then** I see an option to view it in-app (or a message that the feature is coming soon if not yet implemented)
5. **Given** I'm asking a question, **When** the system is processing my query, **Then** the app displays a loading indicator
6. **Given** the system cannot find relevant information, **When** I submit a query, **Then** I receive a clear "no results" message with suggestions for refining my question

---

### User Story 2 - Multi-Turn Conversations (Priority: P2)

As a user engaging with the chat interface, I want to have multi-turn conversations where the system remembers context from my previous questions in the session, so I can ask follow-up questions naturally without repeating information.

**Why this priority**: Enhances the conversational experience by enabling natural follow-up questions, making the chat feel more like a conversation. Valuable but not essential for initial launch.

**Independent Test**: Can be tested by asking an initial question, receiving an answer, then asking a follow-up question that references the previous context (e.g., "What about the 2024 model?" after asking about a 2023 model). Delivers value by reducing friction in multi-step information gathering.

**Acceptance Scenarios**:

1. **Given** I've asked a question and received an answer, **When** I ask a follow-up question that references previous context, **Then** the system understands the context and provides a relevant answer
2. **Given** I'm in an active chat session, **When** I scroll up to view previous questions and answers, **Then** I can see my conversation history for the current session
3. **Given** I want to start a fresh conversation, **When** I start a new chat session, **Then** the previous context is cleared and I begin with no conversation history

---

### User Story 3 - Personalized Responses via User Memory (Priority: P2)

As a user who frequently asks questions about specific motorcycles I own, I want the app to remember information about me (like which motorcycles I own) and use that context in future answers, so I don't have to repeatedly provide the same background information.

**Why this priority**: Significantly improves user experience by reducing repetitive context-setting. As conversation history is pruned for storage management, user memory ensures personalization context is never lost. This differentiates the app from basic chat interfaces.

**Independent Test**: Can be tested by having a conversation where the user mentions owning a specific motorcycle (e.g., "I have a 2023 Yamaha R1M"), then starting a new conversation later and asking a question where that context is relevant (e.g., "What oil should I use?"). The system should reference the user's R1M in the answer without being told again.

**Acceptance Scenarios**:

1. **Given** I mention owning a specific motorcycle in a conversation, **When** I ask a question in a later session about maintenance or parts, **Then** the system incorporates my motorcycle ownership into the response context
2. **Given** the app has learned information about me from past conversations, **When** those old conversations are auto-pruned due to storage limits, **Then** the learned information persists in my user memory
3. **Given** I have user memory stored, **When** I ask a generic question that could apply to many motorcycles, **Then** the system prioritizes answers relevant to motorcycles I own
4. **Given** I want to review or edit my user memory, **When** I access my profile or settings, **Then** I can view what the system has learned about me

---

### User Story 5 - View PDF Page References (Priority: P3 - Post-MVP Enhancement)

As a user who receives an answer that cites a specific page in a PDF manual, I want to optionally view that exact PDF page in the app, so I can see the original source document without leaving the app.

**Why this priority**: This is a nice-to-have enhancement that improves user experience when answers reference PDF manuals, but is NOT essential for MVP. The core value is delivering the answer in natural language with citations - viewing the actual PDF page is secondary.

**Independent Test**: Can be tested by asking a question that results in a PDF citation, tapping on the citation, and viewing the referenced PDF page. Delivers value by providing visual confirmation of sources, but the app is fully functional without this feature.

**Acceptance Scenarios**:

1. **Given** an answer includes a citation to a PDF manual page, **When** I tap on the citation, **Then** I can view that specific PDF page in the app
2. **Given** I'm viewing a PDF page, **When** I use pinch-to-zoom gestures, **Then** the PDF content scales appropriately for readability
3. **Given** I'm viewing a PDF page, **When** I want to return to the chat, **Then** I can easily navigate back to the conversation

---

### User Story 6 - Conversation Management and Navigation (Priority: P1)

As a user, I want all my conversations to be saved locally and easily managed through a chronological list, so I can quickly find, resume, or delete conversations as needed.

**Why this priority**: Essential for a practical chat app experience. Users expect conversations to persist across app sessions and need an intuitive way to navigate between multiple conversations, search for specific topics, and remove unwanted chats. This is core functionality for any modern chat application.

**Independent Test**: Can be tested by creating multiple conversations, closing the app, reopening it, and verifying all conversations appear in a chronological list sorted by recency. Can be tested by searching for specific content, tapping to resume conversations, and swiping to delete unwanted conversations.

**Acceptance Scenarios**:

1. **Given** I have multiple saved conversations, **When** I open the app, **Then** I see a home screen with a chronological list of conversations sorted by most recent activity first
2. **Given** I'm viewing the conversation list, **When** I tap on a conversation, **Then** the conversation opens and I can continue where I left off
3. **Given** I want to remove a conversation, **When** I swipe left/right on a conversation in the list, **Then** I see a delete option and can remove it from the list
4. **Given** I have many conversations, **When** I use the search bar, **Then** I can filter conversations by typing keywords or content from the messages
5. **Given** I'm offline, **When** I open the app, **Then** I can view and scroll through all my previously saved conversations in the list
6. **Given** I'm viewing a saved conversation offline, **When** I attempt to ask a new question, **Then** I see a clear message indicating that connectivity is required for new queries

---

### User Story 7 - Cross-Platform Experience (Priority: P1)

As a user who owns multiple devices (iPhone, Android phone, Windows tablet), I want a consistent chat experience across all platforms so I can use the app on any device I have available.

**Why this priority**: This is a core requirement since the feature explicitly requires iOS, Android, and Windows support. Essential for meeting the stated requirements.

**Independent Test**: Can be tested by installing the app on iOS, Android, and Windows devices and verifying that the chat interface works identically. Delivers value by maximizing user accessibility.

**Acceptance Scenarios**:

1. **Given** I install the app on an iPhone, **When** I ask a question via the chat interface, **Then** the experience matches the Android and Windows versions
2. **Given** I'm using the app on any supported platform, **When** I interact with touch gestures, **Then** the gestures work according to platform conventions (iOS swipes, Android back button, etc.)
3. **Given** I'm using the app on a Windows tablet, **When** I use both touch and keyboard input, **Then** both input methods work appropriately for the chat interface

---

### Edge Cases

- What happens when the user has no internet connection and no cached conversation history?
- How does the app handle API timeouts or service unavailability during a chat conversation?
- What happens when an answer is very long or contains complex formatting on small screens?
- How does the app handle empty or whitespace-only questions?
- What happens when the user submits extremely long questions?
- How does the app request and handle platform-specific permissions (e.g., storage access for caching conversations)?
- What happens when the backend API is updated with breaking changes?
- How does the app behave on phones with very small screens (< 5 inches) or very large screens (phablets)?
- What happens when the user switches between portrait and landscape orientations during an active chat?
- How does the app handle low memory situations when conversation history becomes very long?
- What happens when source citations include URLs that are no longer accessible?
- How does the app display citations for sources with very long titles or complex metadata?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: App MUST run natively on iOS (version 15+), Android (version 10+), and Windows (version 10+)
- **FR-002**: App MUST require user authentication via Microsoft Entra External ID / B2C with social identity providers (Google, GitHub, Microsoft, Facebook) before allowing access to chat functionality
- **FR-003**: App MUST display user's current plan/SKU (Free/Plus/Pro) and remaining daily request quota after authentication
- **FR-004**: App MUST provide a mobile-optimized chat interface with touch-friendly text input and conversational message display
- **FR-005**: App MUST integrate with the existing MotorcycleRAG.API backend (POST /api/motorcycles/query) to submit natural language questions and receive answers
- **FR-006**: App MUST display conversational answers in a mobile-friendly chat format with appropriate text sizing, layout, and message bubbles
- **FR-007**: App MUST display source citations within or alongside answers as tappable elements, showing source type (PDF manual, web, dataset) and relevant metadata
- **FR-007a**: App MUST open web URL citations in the device's default browser when tapped
- **FR-007b**: App MUST show a "view in app" option for PDF citations when tapped; if PDF viewing is not yet implemented (post-MVP), display a message indicating the feature is coming soon
- **FR-008**: App MUST persist all conversation sessions locally so they survive app restarts and can be continued where left off
- **FR-008a**: App MUST limit total conversation storage to 100MB and auto-prune oldest conversations when limit is reached
- **FR-008b**: App MUST extract and persist user-specific information (e.g., motorcycles owned, preferences, context) as "user memory" before pruning conversations
- **FR-008c**: App MUST maintain user memory separately from conversation history so learned information persists even after conversations are pruned
- **FR-009**: App MUST allow users to resume any previously saved conversation session
- **FR-009a**: App MUST include user memory and custom instructions when sending context to the backend API for personalized responses
- **FR-009b**: App MUST provide a user interface (in profile or settings) to view and edit stored user memory
- **FR-009c**: App MUST display a home screen with a chronological list of conversations sorted by most recent activity first
- **FR-009d**: App MUST allow users to tap a conversation in the list to open and resume it
- **FR-009e**: App MUST support swipe-to-delete gesture for removing conversations from the list
- **FR-009f**: App MUST provide a search bar to filter conversations by content or keywords
- **FR-010**: App MUST display clear error messages when connectivity is unavailable or API calls fail
- **FR-011**: App MUST display a clear message when user reaches their daily request limit
- **FR-012**: App MUST support touch gestures appropriate to each platform (swipe, tap, scroll through conversation history)
- **FR-013**: App MUST adapt chat UI layout for both portrait and landscape orientations
- **FR-014**: App MUST display a loading indicator while processing questions and waiting for answers
- **FR-015**: App MUST respect platform-specific UI conventions (navigation patterns, system fonts, status bars, keyboard behavior)
- **FR-016**: Users MUST be able to view their full conversation history across all sessions, not just the current session
- **FR-017**: App MUST handle platform permissions appropriately (storage for caching, network access)
- **FR-018**: App MUST focus exclusively on core end-user features (asking questions via chat, viewing answers with citations); administrative functions and data ingestion management remain web-only
- **FR-019**: App MUST support the same multi-agent RAG capabilities as the web UI (vector search, web augmentation, PDF search) by calling the backend API
- **FR-020**: App MUST support multi-turn conversations by maintaining conversation context within a session and sending context to the backend API
- **FR-021**: App SHOULD support optional PDF page viewing when citations reference specific PDF pages (post-MVP enhancement)
- **FR-022**: App MUST provide a way to start a new chat session while preserving previous conversations

### Key Entities

- **ChatMessage**: Represents a single message in the conversation, including message text, sender (user or system), timestamp, and associated citations
- **ConversationSession**: Represents a chat session with conversation history, context, session identifier, and persistence state; persisted locally to survive app restarts
- **Question**: User's natural language question text with timestamp and optional preferences
- **Answer**: System's natural language response with associated source citations, query ID, and metadata
- **SourceCitation**: Reference to a source supporting factual claims, including source type (PDF/web/dataset), title, URL/identifier, and specific location (page number, section)
- **PersistedConversation**: Locally stored conversation data including all messages, metadata, and session context, allowing resumption after app restart; subject to 100MB storage limit with auto-pruning
- **UserMemory**: Persistent user-specific information extracted from conversations (e.g., motorcycles owned, riding preferences, technical expertise level, frequently asked topics) that survives conversation pruning
- **CustomInstructions**: User-provided or system-learned preferences for how responses should be formatted or what context should always be included
- **UserPreferences**: App settings including theme preferences, source type preferences (enable/disable web/PDF sources)

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can ask a natural language question and receive a conversational answer in under 3 seconds on a standard mobile connection
- **SC-002**: App successfully runs on iOS 15+, Android 10+, and Windows 10+ devices without platform-specific crashes
- **SC-003**: App launches and displays the chat interface in under 2 seconds on mid-range devices
- **SC-004**: 95% of users can successfully ask their first question and receive an answer without assistance or errors
- **SC-005**: App maintains responsive scrolling performance (60 FPS) when viewing conversation history with up to 50 messages
- **SC-006**: Cached conversations remain accessible offline for at least the 10 most recent chat sessions
- **SC-007**: Chat UI adapts correctly to screen sizes ranging from 4.7 inches to 7 inches without layout breaking
- **SC-008**: Touch targets (send button, message input, citations) are at least 44x44 points (iOS) / 48x48 dp (Android) for comfortable interaction
- **SC-009**: Answers containing factual claims include at least one tappable source citation with visible source metadata; tapping web citations opens browser, tapping PDF citations shows view option
- **SC-010**: App consumes less than 100MB of storage for conversation history; oldest conversations auto-prune when limit reached
- **SC-011**: Users can successfully conduct multi-turn conversations where follow-up questions reference previous context
- **SC-012**: Chat interface displays both user questions and system answers in clearly distinguishable message formats
- **SC-013**: User-specific information (e.g., motorcycles owned, preferences) persists as user memory even after conversations containing that information are pruned
- **SC-014**: Responses incorporate user memory for personalization (e.g., if user owns an R1M, system references this in relevant answers)
- **SC-015**: Home screen displays conversation list sorted chronologically with most recent conversations at the top
- **SC-016**: Users can successfully delete conversations using swipe gesture; deleted conversations are immediately removed from the list
- **SC-017**: Search functionality filters conversations in real-time as user types; results display within 1 second of input

## Assumptions

- The existing MotorcycleRAG.API POST /api/motorcycles/query endpoint is accessible from mobile devices and has appropriate CORS/authentication configured
- The backend API accepts natural language questions and returns natural language answers with source citations in the response format
- Users MUST authenticate using Microsoft Entra External ID / B2C with social sign-in (Google, GitHub, Microsoft, Facebook) for usage tracking and plan enforcement (Free/Plus/Pro tiers)
- The backend API supports conversation context via the optional `context` field (sessionId, previousQueries) for multi-turn conversations
- The backend API can accept user memory and custom instructions in the context field to personalize responses based on learned user information
- The mobile app is responsible for extracting and managing user memory locally; memory extraction logic resides in the mobile app, not the backend
- Platform stores (Apple App Store, Google Play Store, Microsoft Store) will be the primary distribution channels
- Users have granted necessary platform permissions (network access, storage for caching conversations)
- The app will use standard HTTP/HTTPS communication; no special VPN or network requirements
- Offline functionality includes full local persistence of conversation history; conversations can be resumed offline but new questions require connectivity
- All RAG processing (vector search, web augmentation, PDF search) happens on the backend; the mobile app is purely a chat client with local conversation storage
- The app will follow platform-specific design guidelines (Material Design for Android, Human Interface Guidelines for iOS, Fluent Design for Windows)
- Chat interface will display user messages and system responses in a conversational format similar to popular chat applications

## Dependencies

- Existing MotorcycleRAG.API backend must be deployed and accessible
- Backend API must support mobile client requests (CORS, rate limiting considerations)
- .NET MAUI development environment and tooling
- Platform-specific development certificates and provisioning profiles (iOS)
- Access to platform stores for distribution (or enterprise distribution mechanisms)

## Out of Scope

The following are explicitly NOT included in this feature unless clarified otherwise:

- **MVP Exclusions**:
  - PDF page viewing in-app (post-MVP enhancement - User Story 3)
  - Cloud-synced conversation history across multiple devices (conversations persist locally per device only)
  - Voice input for questions
  - Voice output for answers (text-to-speech)

- **Administrative Functions**:
  - Data ingestion and index management (remains in desktop MAUI admin app)
  - Web source configuration
  - MCP tool configuration
  - User/plan administration

- **Advanced Features**:
  - Push notifications for updates or alerts
  - Social features (sharing conversations, favorites, bookmarks across devices)
  - In-app purchases or subscription management (plan changes handled via web/admin)
  - Offline AI/embedding models (all AI processing happens on backend)
  - Native platform integrations (widgets, Siri/Google Assistant shortcuts)
  - Image-based questions (uploading photos of motorcycles)
  - Real-time collaborative chat or shared conversations
