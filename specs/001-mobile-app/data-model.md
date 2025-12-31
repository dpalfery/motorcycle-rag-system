# Data Model: .NET MAUI Mobile App

**Feature**: 001-mobile-app
**Date**: 2025-12-26
**Purpose**: Define domain entities, database schema, and data relationships

## Overview

This document defines the data model for the mobile chat application, including domain entities (Models), database entities (Persistence), and their relationships.

## Domain Models (Application Layer)

### ChatMessage

Represents a single message in a conversation (user question or system answer).

```csharp
public class ChatMessage
{
    public string Id { get; set; }                    // Unique message identifier
    public string ConversationId { get; set; }        // Parent conversation
    public MessageSender Sender { get; set; }         // User or System
    public string Content { get; set; }               // Message text
    public DateTime Timestamp { get; set; }           // When message was created
    public List<SourceCitation> Citations { get; set; }  // Sources (if system message)
    public string QueryId { get; set; }               // Backend query ID (for system messages)
    public MessageStatus Status { get; set; }         // Sending, Sent, Failed
}

public enum MessageSender
{
    User,
    System
}

public enum MessageStatus
{
    Sending,
    Sent,
    Failed
}
```

**Validation Rules**:
- `Id`: Required, GUID format
- `Content`: Required, max length 10,000 characters (user questions), unlimited (system answers)
- `Timestamp`: Required, UTC
- `Citations`: Empty for user messages, populated for system messages with factual claims

**State Transitions**:
- User sends question: `Sending` → `Sent` (success) or `Failed` (error)
- System answer received: Created with `Sent` status

---

### ConversationSession

Represents a chat conversation session.

```csharp
public class ConversationSession
{
    public string Id { get; set; }                    // Unique conversation identifier
    public string Title { get; set; }                 // Auto-generated from first question
    public DateTime CreatedAt { get; set; }           // When conversation started
    public DateTime UpdatedAt { get; set; }           // Last message timestamp
    public List<ChatMessage> Messages { get; set; }   // All messages in conversation
    public ConversationStatus Status { get; set; }    // Active, Archived, Deleted
    public long SizeBytes { get; set; }               // Approximate size for pruning
}

public enum ConversationStatus
{
    Active,     // Currently accessible
    Archived,   // User archived, still accessible
    Deleted     // Marked for deletion (soft delete)
}
```

**Validation Rules**:
- `Id`: Required, GUID format
- `Title`: Auto-generated from first 50 characters of first question
- `CreatedAt`, `UpdatedAt`: Required, UTC
- `Messages`: At least 1 message (the initial user question)
- `SizeBytes`: Calculated as sum of message content lengths + metadata

**Relationships**:
- One `ConversationSession` has many `ChatMessage`s (one-to-many)
- Messages are deleted when conversation is deleted (cascade)

---

### SourceCitation

Represents a source reference in a system answer.

```csharp
public class SourceCitation
{
    public string Id { get; set; }                    // Unique citation identifier
    public SourceType Type { get; set; }              // PDF, Web, Dataset
    public string Title { get; set; }                 // Source name/title
    public string Url { get; set; }                   // Web URL (for web sources)
    public string DocumentId { get; set; }            // Document identifier (for PDF/dataset)
    public int? PageNumber { get; set; }              // PDF page number (if applicable)
    public string Section { get; set; }               // PDF section/chapter (if applicable)
    public bool IsTappable { get; set; }              // Can user tap to open? (web=true, PDF=post-MVP)
}

public enum SourceType
{
    PdfManual,
    WebSource,
    Dataset
}
```

**Validation Rules**:
- `Type`: Required
- `Title`: Required, max 200 characters
- `Url`: Required for WebSource, optional for others
- `DocumentId`: Required for PdfManual and Dataset
- `PageNumber`, `Section`: Optional, only for PdfManual

**Business Logic**:
- Web citations: `IsTappable = true`, tapping opens `Url` in browser
- PDF citations: `IsTappable = true`, tapping shows "View in app" or "Coming soon" message
- Dataset citations: `IsTappable = false`, display only

---

### UserMemory

Represents a piece of learned user information.

```csharp
public class UserMemory
{
    public string Id { get; set; }                    // Unique memory identifier
    public string Category { get; set; }              // Type of information (motorcycles_owned, etc.)
    public string Value { get; set; }                 // The actual information
    public string SourceConversationId { get; set; }  // Where this was learned
    public DateTime ExtractedAt { get; set; }         // When extracted
    public DateTime? UpdatedAt { get; set; }          // Last updated (if value changed)
    public bool IsActive { get; set; }                // Active or superseded/deleted
}
```

**Validation Rules**:
- `Category`: Required, one of predefined categories (see extraction patterns in research.md)
- `Value`: Required, max 500 characters
- `IsActive`: Defaults to `true`

**Categories** (predefined):
- `motorcycles_owned`: Motorcycles the user owns
- `riding_style`: User's riding preferences (sport, touring, off-road, etc.)
- `expertise_level`: User's technical knowledge (beginner, intermediate, advanced)
- `maintenance_preference`: Whether user does own maintenance
- `custom`: User-defined or edge case memories

**Lifecycle**:
- Created when pattern match found in conversation
- Updated when conflicting information appears (old value deactivated, new value created)
- Deleted/deactivated by user in Profile page
- Persists even after source conversation is pruned

---

### UserProfile

Represents the authenticated user's profile data.

```csharp
public class UserProfile
{
    public string UserId { get; set; }                // From Entra External ID (sub claim)
    public string Email { get; set; }                 // User email
    public string DisplayName { get; set; }           // User display name
    public SubscriptionPlan Plan { get; set; }        // Free, Plus, Pro
    public int DailyRequestLimit { get; set; }        // Requests allowed per day
    public int RequestsUsedToday { get; set; }        // Current day usage
    public DateTime LimitResetAt { get; set; }        // When daily limit resets (UTC midnight)
}

public enum SubscriptionPlan
{
    Free,   // 10 requests/day
    Plus,   // 100 requests/day
    Pro     // Unlimited
}
```

**Validation Rules**:
- `UserId`: Required, from authentication token
- `Email`: Required, valid email format
- `DailyRequestLimit`: Set based on `Plan` (10, 100, or int.MaxValue)
- `RequestsUsedToday`: Resets to 0 at `LimitResetAt`

**Business Logic**:
- Fetch from backend API on first app launch after authentication
- Cache locally, refresh on each app launch
- Increment `RequestsUsedToday` after each successful query
- Block new queries when `RequestsUsedToday >= DailyRequestLimit`

---

## Persistence Entities (Database Schema)

### ConversationEntity (SQLite Table)

```sql
CREATE TABLE Conversations (
    Id TEXT PRIMARY KEY NOT NULL,
    Title TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,              -- ISO 8601 format
    UpdatedAt TEXT NOT NULL,              -- ISO 8601 format
    Status INTEGER NOT NULL DEFAULT 0,    -- ConversationStatus enum
    SizeBytes INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX idx_conversations_updated ON Conversations(UpdatedAt DESC);
CREATE INDEX idx_conversations_status ON Conversations(Status);
```

**C# Entity**:
```csharp
[Table("Conversations")]
public class ConversationEntity
{
    [PrimaryKey]
    public string Id { get; set; }

    public string Title { get; set; }

    public string CreatedAt { get; set; }  // Stored as ISO 8601 string

    public string UpdatedAt { get; set; }  // Stored as ISO 8601 string

    public int Status { get; set; }        // Cast to/from ConversationStatus

    public long SizeBytes { get; set; }

    // Navigation: Messages loaded separately
}
```

---

### MessageEntity (SQLite Table)

```sql
CREATE TABLE Messages (
    Id TEXT PRIMARY KEY NOT NULL,
    ConversationId TEXT NOT NULL,
    Sender INTEGER NOT NULL,              -- MessageSender enum
    Content TEXT NOT NULL,
    Timestamp TEXT NOT NULL,              -- ISO 8601 format
    QueryId TEXT,                         -- Nullable
    Status INTEGER NOT NULL DEFAULT 0,    -- MessageStatus enum
    FOREIGN KEY (ConversationId) REFERENCES Conversations(Id) ON DELETE CASCADE
);

CREATE INDEX idx_messages_conversation ON Messages(ConversationId);
CREATE INDEX idx_messages_timestamp ON Messages(Timestamp);
CREATE VIRTUAL TABLE Messages_FTS USING fts5(Content, ConversationId UNINDEXED);
```

**C# Entity**:
```csharp
[Table("Messages")]
public class MessageEntity
{
    [PrimaryKey]
    public string Id { get; set; }

    [Indexed]
    public string ConversationId { get; set; }

    public int Sender { get; set; }        // Cast to/from MessageSender

    public string Content { get; set; }

    public string Timestamp { get; set; }  // Stored as ISO 8601 string

    public string QueryId { get; set; }

    public int Status { get; set; }        // Cast to/from MessageStatus

    // Navigation: Citations loaded separately
}
```

---

### CitationEntity (SQLite Table)

```sql
CREATE TABLE Citations (
    Id TEXT PRIMARY KEY NOT NULL,
    MessageId TEXT NOT NULL,
    Type INTEGER NOT NULL,                -- SourceType enum
    Title TEXT NOT NULL,
    Url TEXT,
    DocumentId TEXT,
    PageNumber INTEGER,
    Section TEXT,
    IsTappable INTEGER NOT NULL DEFAULT 0, -- Boolean (0/1)
    FOREIGN KEY (MessageId) REFERENCES Messages(Id) ON DELETE CASCADE
);

CREATE INDEX idx_citations_message ON Citations(MessageId);
```

**C# Entity**:
```csharp
[Table("Citations")]
public class CitationEntity
{
    [PrimaryKey]
    public string Id { get; set; }

    [Indexed]
    public string MessageId { get; set; }

    public int Type { get; set; }          // Cast to/from SourceType

    public string Title { get; set; }

    public string Url { get; set; }

    public string DocumentId { get; set; }

    public int? PageNumber { get; set; }

    public string Section { get; set; }

    public int IsTappable { get; set; }    // Boolean (0=false, 1=true)
}
```

---

### UserMemoryEntity (SQLite Table)

```sql
CREATE TABLE UserMemory (
    Id TEXT PRIMARY KEY NOT NULL,
    Category TEXT NOT NULL,
    Value TEXT NOT NULL,
    SourceConversationId TEXT,
    ExtractedAt TEXT NOT NULL,            -- ISO 8601 format
    UpdatedAt TEXT,                       -- ISO 8601 format, nullable
    IsActive INTEGER NOT NULL DEFAULT 1   -- Boolean (0/1)
);

CREATE INDEX idx_memory_category ON UserMemory(Category);
CREATE INDEX idx_memory_active ON UserMemory(IsActive);
```

**C# Entity**:
```csharp
[Table("UserMemory")]
public class UserMemoryEntity
{
    [PrimaryKey]
    public string Id { get; set; }

    [Indexed]
    public string Category { get; set; }

    public string Value { get; set; }

    public string SourceConversationId { get; set; }

    public string ExtractedAt { get; set; }  // Stored as ISO 8601 string

    public string UpdatedAt { get; set; }    // Stored as ISO 8601 string, nullable

    [Indexed]
    public int IsActive { get; set; }        // Boolean (0=false, 1=true)
}
```

---

## Entity Relationships

```
UserProfile (from API)
  │
  └─> (Authenticated User)

ConversationEntity (1)
  ├──> MessageEntity (many)
  │      └──> CitationEntity (many)
  │
  └──> UserMemoryEntity (many, via SourceConversationId)
```

**Cascade Rules**:
- Delete Conversation → Delete all Messages (CASCADE)
- Delete Message → Delete all Citations (CASCADE)
- Delete Conversation → UserMemory remains (SourceConversationId becomes orphaned but preserved)

**Loading Patterns**:
- **Conversation List**: Load ConversationEntity only (no messages), sorted by UpdatedAt DESC
- **Conversation Detail**: Load ConversationEntity + all MessageEntities + all CitationEntities
- **Search**: Use FTS5 on Messages_FTS, return matching ConversationEntities
- **User Memory**: Load all active UserMemoryEntity records (IsActive = 1)

---

## Data Access Patterns

### Repository Interfaces

```csharp
// Conversation CRUD
public interface IConversationRepository
{
    Task<List<ConversationEntity>> GetAllAsync();
    Task<ConversationEntity> GetByIdAsync(string id);
    Task<List<ConversationEntity>> SearchAsync(string query);  // FTS search
    Task<int> InsertAsync(ConversationEntity conversation);
    Task<int> UpdateAsync(ConversationEntity conversation);
    Task<int> DeleteAsync(string id);
    Task<long> GetTotalStorageSizeAsync();
    Task PruneOldestConversationsAsync(long bytesToFree);
}

// Message CRUD
public interface IMessageRepository
{
    Task<List<MessageEntity>> GetByConversationIdAsync(string conversationId);
    Task<int> InsertAsync(MessageEntity message);
    Task<int> UpdateAsync(MessageEntity message);
}

// Citation CRUD
public interface ICitationRepository
{
    Task<List<CitationEntity>> GetByMessageIdAsync(string messageId);
    Task<int> InsertManyAsync(List<CitationEntity> citations);
}

// User Memory CRUD
public interface IUserMemoryRepository
{
    Task<List<UserMemoryEntity>> GetActiveMemoriesAsync();
    Task<UserMemoryEntity> GetByCategoryAsync(string category);
    Task<int> InsertAsync(UserMemoryEntity memory);
    Task<int> UpdateAsync(UserMemoryEntity memory);
    Task<int> DeactivateAsync(string id);
    Task<int> DeleteAsync(string id);
}
```

---

## Storage Size Calculation

**Formula**:
```
ConversationSize = SUM(MessageContent.Length) + (MessageCount * 500 bytes metadata overhead)
TotalStorageUsed = SUM(AllConversations.SizeBytes)
```

**Pruning Strategy**:
1. When `TotalStorageUsed > 100MB`:
   - Order conversations by `UpdatedAt ASC` (oldest first)
   - Extract user memory from conversations to be pruned
   - Delete oldest conversations until `TotalStorageUsed < 90MB` (10MB buffer)
   - Update `TotalStorageUsed` metric

**Size Overhead** (approximate):
- ConversationEntity: 200 bytes
- MessageEntity: 500 bytes (without content)
- CitationEntity: 300 bytes
- UserMemoryEntity: 200 bytes

---

## Data Mapping (Entity ↔ Model)

ViewModels and Services work with **Domain Models**, Repositories work with **Persistence Entities**. Mapping layer converts between them.

```csharp
public static class ConversationMapper
{
    public static ConversationSession ToModel(ConversationEntity entity, List<MessageEntity> messages)
    {
        return new ConversationSession
        {
            Id = entity.Id,
            Title = entity.Title,
            CreatedAt = DateTime.Parse(entity.CreatedAt),
            UpdatedAt = DateTime.Parse(entity.UpdatedAt),
            Status = (ConversationStatus)entity.Status,
            SizeBytes = entity.SizeBytes,
            Messages = messages.Select(MessageMapper.ToModel).ToList()
        };
    }

    public static ConversationEntity ToEntity(ConversationSession model)
    {
        return new ConversationEntity
        {
            Id = model.Id,
            Title = model.Title,
            CreatedAt = model.CreatedAt.ToString("O"),  // ISO 8601
            UpdatedAt = model.UpdatedAt.ToString("O"),
            Status = (int)model.Status,
            SizeBytes = model.SizeBytes
        };
    }
}
```

---

## Summary

**Entities Defined**:
- 5 Domain Models (ChatMessage, ConversationSession, SourceCitation, UserMemory, UserProfile)
- 4 SQLite Tables (Conversations, Messages, Citations, UserMemory)
- 4 Repository Interfaces

**Key Relationships**:
- Conversation → Messages → Citations (1:N:N, cascade delete)
- Conversation → UserMemory (1:N, soft relationship via SourceConversationId)

**Performance Considerations**:
- Indexed fields for sorting (UpdatedAt) and filtering (Status, IsActive)
- FTS5 virtual table for full-text search
- Lazy loading of messages/citations (not loaded with conversation list)

**Storage Management**:
- 100MB total limit enforced
- Oldest conversations pruned first
- User memory extracted before pruning
- Size calculated and tracked per conversation
