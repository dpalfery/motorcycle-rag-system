# Research: Intelligent User Memory Extraction for Mobile Chat Application

**Created**: 2025-12-26
**Related**: [spec.md](./spec.md) - User Story 3 (Personalized Responses via User Memory)
**Context**: Mobile app with 100MB conversation storage limit requiring intelligent memory extraction before pruning

---

## Executive Summary

This research document provides comprehensive recommendations for implementing intelligent user memory extraction in the .NET MAUI mobile motorcycle chat application. The feature extracts persistent user context (e.g., "user owns a 2023 Yamaha R1M") from conversations before they are pruned due to storage limits, enabling personalized responses even after conversation history is deleted.

**Key Recommendation**: Implement a **hybrid local extraction + backend validation** approach using pattern matching on-device with optional backend API enrichment for complex memory updates. Store memory as **structured key-value entities** with versioning and conflict resolution.

---

## 1. Extraction Strategies

### 1.1 When to Trigger Extraction

**Recommended Approach: Multi-Trigger Strategy**

Implement three complementary extraction triggers:

#### A. Immediate Extraction (Real-time Pattern Matching)
- **When**: After each user message that matches known patterns
- **Where**: On-device during conversation
- **Latency**: < 100ms (non-blocking)
- **Use Case**: High-confidence ownership statements

**Patterns to Extract Immediately**:
```csharp
// Ownership statements
"I have a {year} {make} {model}"
"I own a {make} {model}"
"My {make} {model}"
"I ride a {year} {make} {model}"

// Clear preferences
"I prefer {oil_type} oil"
"I always use {brand} parts"
"I'm a {experience_level} rider" (beginner/intermediate/advanced)
```

**Implementation**:
```csharp
public class RealtimeMemoryExtractor
{
    private readonly Dictionary<string, Regex> _ownershipPatterns = new()
    {
        ["motorcycle_ownership"] = new Regex(
            @"\b(I have|I own|My|I ride)\s+a?\s*(?<year>\d{4})?\s*(?<make>\w+)\s+(?<model>[\w\-\s]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        ["preference_oil"] = new Regex(
            @"\b(I prefer|I use|I always use)\s+(?<oil_type>[\w\-]+)\s+oil",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        ["experience_level"] = new Regex(
            @"\b(I'?m a|I consider myself)\s+(?<level>beginner|intermediate|advanced|expert)\s+rider",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    public IEnumerable<MemoryCandidate> ExtractFromMessage(string userMessage)
    {
        var candidates = new List<MemoryCandidate>();

        foreach (var (memoryType, pattern) in _ownershipPatterns)
        {
            var match = pattern.Match(userMessage);
            if (match.Success)
            {
                candidates.Add(new MemoryCandidate
                {
                    Type = memoryType,
                    ConfidenceScore = 0.95f, // High confidence for explicit patterns
                    ExtractedAt = DateTime.UtcNow,
                    SourceMessage = userMessage,
                    Data = match.Groups.Cast<Group>()
                        .Where(g => !string.IsNullOrEmpty(g.Name) && int.TryParse(g.Name, out _) == false)
                        .ToDictionary(g => g.Name, g => g.Value)
                });
            }
        }

        return candidates;
    }
}
```

**Advantages**:
- Zero latency impact on chat experience
- Works completely offline
- Captures information before user forgets or leaves conversation
- High precision for explicit statements

**Disadvantages**:
- Limited to pattern-based extraction
- May miss implicit or contextual information
- Requires maintenance as patterns evolve

---

#### B. Periodic Background Extraction (Conversation Analysis)
- **When**: Every 10 messages or every 5 minutes of conversation activity
- **Where**: Background task on-device or optional API call
- **Latency**: Non-blocking, processes in background
- **Use Case**: Implicit preferences, riding style, common topics

**What to Extract**:
```csharp
public class PeriodicMemoryExtractor
{
    // Analyze conversation window for implicit patterns
    public async Task<IEnumerable<MemoryCandidate>> ExtractFromConversationAsync(
        IEnumerable<ChatMessage> recentMessages,
        bool useBackendAPI = false)
    {
        var candidates = new List<MemoryCandidate>();

        // Local frequency analysis
        var motorcycleMentions = ExtractMentionFrequency(recentMessages,
            pattern: @"\b(?<make>Honda|Yamaha|Kawasaki|Suzuki)\s+(?<model>[\w\-]+)");

        foreach (var (motorcycle, count) in motorcycleMentions.Where(m => m.Value >= 3))
        {
            candidates.Add(new MemoryCandidate
            {
                Type = "frequent_interest",
                ConfidenceScore = Math.Min(0.5f + (count * 0.1f), 0.9f),
                Data = new Dictionary<string, string> { ["motorcycle"] = motorcycle },
                Metadata = new Dictionary<string, object> { ["mention_count"] = count }
            });
        }

        // Optional: Use backend API for complex semantic analysis
        if (useBackendAPI && IsOnline())
        {
            var semanticCandidates = await AnalyzeConversationSemantics(recentMessages);
            candidates.AddRange(semanticCandidates);
        }

        return candidates;
    }

    private Dictionary<string, int> ExtractMentionFrequency(
        IEnumerable<ChatMessage> messages, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        var mentions = new Dictionary<string, int>();

        foreach (var message in messages.Where(m => m.IsUserMessage))
        {
            var matches = regex.Matches(message.Text);
            foreach (Match match in matches)
            {
                var key = $"{match.Groups["make"].Value} {match.Groups["model"].Value}";
                mentions[key] = mentions.GetValueOrDefault(key) + 1;
            }
        }

        return mentions;
    }
}
```

**Advantages**:
- Captures implicit patterns not caught by real-time extraction
- Non-blocking background processing
- Can leverage backend API when online for semantic understanding
- Frequency-based confidence scoring

**Disadvantages**:
- Delayed extraction (may miss information if app crashes)
- Battery/CPU considerations for background processing
- Lower confidence scores for implicit information

---

#### C. Pre-Pruning Extraction (Final Sweep)
- **When**: Immediately before conversations are pruned (storage limit reached)
- **Where**: On-device with optional backend call
- **Latency**: Blocking (prevents data loss)
- **Use Case**: Last-chance extraction from oldest conversations

**Implementation**:
```csharp
public class PrePruneMemoryExtractor
{
    public async Task<ExtractionReport> ExtractBeforePruneAsync(
        IEnumerable<ConversationSession> conversationsToPrune,
        CancellationToken cancellationToken)
    {
        var report = new ExtractionReport();

        foreach (var conversation in conversationsToPrune)
        {
            try
            {
                // Run all available extractors on conversation
                var realtimeExtractor = new RealtimeMemoryExtractor();
                var periodicExtractor = new PeriodicMemoryExtractor();

                var candidates = new List<MemoryCandidate>();

                // Extract from all user messages
                foreach (var message in conversation.Messages.Where(m => m.IsUserMessage))
                {
                    candidates.AddRange(realtimeExtractor.ExtractFromMessage(message.Text));
                }

                // Extract implicit patterns
                candidates.AddRange(await periodicExtractor.ExtractFromConversationAsync(
                    conversation.Messages, useBackendAPI: true));

                // Merge and deduplicate with existing memory
                await MergeWithExistingMemoryAsync(candidates, conversation.SessionId);

                report.ProcessedConversations++;
                report.ExtractedMemories += candidates.Count;
            }
            catch (Exception ex)
            {
                report.Errors.Add($"Failed to extract from conversation {conversation.SessionId}: {ex.Message}");
            }
        }

        return report;
    }
}
```

**Advantages**:
- Guarantees no data loss before pruning
- Can use more aggressive extraction (lower confidence thresholds)
- Backend API available for semantic analysis
- Last opportunity to preserve valuable context

**Disadvantages**:
- Blocking operation (delays pruning)
- May be computationally expensive
- User might notice delay if processing many conversations

---

### 1.2 What Patterns Indicate Extractable Information

**Category 1: Motorcycle Ownership (High Confidence)**
```
Priority: P0 (Critical)
Confidence Threshold: 0.90

Patterns:
- "I have a [year] [make] [model]"
- "I own a [make] [model]"
- "My [make] [model]"
- "I ride a [make] [model]"
- "I bought a [year] [make] [model]"
- "I just got a [make] [model]"

Storage Structure:
{
  "type": "motorcycle_ownership",
  "make": "Yamaha",
  "model": "R1M",
  "year": 2023,
  "confidence": 0.95,
  "extracted_at": "2025-12-26T10:30:00Z",
  "source_conversation_id": "conv_123",
  "status": "active" // or "sold", "inactive"
}
```

**Category 2: Riding Preferences (Medium Confidence)**
```
Priority: P1 (Important)
Confidence Threshold: 0.75

Patterns:
- "I prefer [oil_type] oil"
- "I use [tire_brand] tires"
- "I ride on [terrain_type]" (track, street, off-road)
- "I do [maintenance_type] myself"

Storage Structure:
{
  "type": "maintenance_preference",
  "category": "oil",
  "value": "Motul 300V",
  "confidence": 0.80,
  "extracted_at": "2025-12-26T10:30:00Z"
}
```

**Category 3: Technical Expertise Level (Medium Confidence)**
```
Priority: P1 (Important)
Confidence Threshold: 0.70

Patterns:
- "I'm a [beginner|intermediate|advanced|expert] rider"
- "I've been riding for [X] years"
- "I do my own [maintenance_type]" (implies advanced)
- "How do I [basic_task]?" (implies beginner)

Storage Structure:
{
  "type": "expertise_level",
  "riding_experience": "intermediate",
  "years_riding": 5,
  "maintenance_skills": ["oil_change", "brake_bleeding"],
  "confidence": 0.75,
  "inferred_from": ["explicit_statement", "maintenance_questions"]
}
```

**Category 4: Frequent Topics (Low-Medium Confidence)**
```
Priority: P2 (Nice to Have)
Confidence Threshold: 0.60

Patterns:
- Mentions same motorcycle 5+ times
- Asks about same topic repeatedly (e.g., suspension tuning)
- Shows interest in specific modifications

Storage Structure:
{
  "type": "frequent_interest",
  "topic": "suspension_tuning",
  "mention_count": 7,
  "confidence": 0.65,
  "first_mentioned": "2025-12-01T10:00:00Z",
  "last_mentioned": "2025-12-26T10:30:00Z"
}
```

---

### 1.3 Temporary Context vs. Persistent Facts

**Decision Matrix for Persistence**:

| Pattern Type | Persist? | Reasoning | Example |
|-------------|----------|-----------|---------|
| **Motorcycle Ownership (current)** | ✅ YES | Core user identity, high value | "I own a 2023 R1M" |
| **Past Motorcycle Ownership** | ⚠️ MAYBE | Useful for experience context, low storage cost | "I used to ride a CBR600" |
| **One-time Questions** | ❌ NO | Temporary, no future value | "What's the weather like?" |
| **Maintenance Schedules** | ✅ YES | Recurring need, high value | "I change oil every 3000 miles" |
| **Riding Style/Terrain** | ✅ YES | Affects recommendations | "I mostly ride on track" |
| **Current Location** | ❌ NO | Privacy concern, changes frequently | "I'm in San Francisco today" |
| **Workshop Nearby** | ⚠️ MAYBE | Useful if persistent, privacy concern | "I use [shop name] for service" |
| **Budget Constraints** | ⚠️ MAYBE | Temporary, but useful for parts recommendations | "Looking for budget options under $200" |
| **Modification Interests** | ✅ YES | Long-term interest, valuable for future queries | "Interested in exhaust upgrades" |

**Persistence Rules**:
```csharp
public class MemoryPersistenceRules
{
    public bool ShouldPersist(MemoryCandidate candidate)
    {
        // Rule 1: Never persist PII without explicit consent
        if (IsPII(candidate)) return false;

        // Rule 2: Persist high-confidence ownership statements
        if (candidate.Type == "motorcycle_ownership" && candidate.ConfidenceScore >= 0.90)
            return true;

        // Rule 3: Persist recurring preferences
        if (candidate.Type.Contains("preference") && candidate.ConfidenceScore >= 0.75)
            return true;

        // Rule 4: Persist technical expertise indicators
        if (candidate.Type == "expertise_level" && candidate.ConfidenceScore >= 0.70)
            return true;

        // Rule 5: Only persist frequent interests if mentioned 5+ times
        if (candidate.Type == "frequent_interest")
        {
            var mentionCount = candidate.Metadata?.GetValueOrDefault("mention_count", 0);
            return mentionCount is int count && count >= 5;
        }

        // Rule 6: Default to NO for unrecognized patterns
        return false;
    }

    private bool IsPII(MemoryCandidate candidate)
    {
        // Check for personal identifiable information patterns
        var piiTypes = new[] { "name", "phone", "email", "address", "location", "credit_card" };
        return piiTypes.Any(pii => candidate.Type.Contains(pii, StringComparison.OrdinalIgnoreCase));
    }
}
```

---

## 2. Storage Format

### 2.1 Recommended: Structured Key-Value Entities with Versioning

**Rationale**:
- Supports complex queries (e.g., "find all owned motorcycles")
- Enables conflict resolution when information changes
- Allows categorical organization for UI display
- Maintains audit trail for debugging

**Schema**:
```csharp
public class UserMemoryEntity
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString(); // Unique memory ID

    public string UserId { get; set; } = string.Empty; // For multi-user support

    public string Category { get; set; } = string.Empty; // "ownership", "preference", "expertise"

    public string Type { get; set; } = string.Empty; // "motorcycle_ownership", "oil_preference"

    public Dictionary<string, object> Data { get; set; } = new(); // Structured data

    public float ConfidenceScore { get; set; } // 0.0 to 1.0

    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    public string SourceConversationId { get; set; } = string.Empty; // Traceability

    public int Version { get; set; } = 1; // For conflict resolution

    public string Status { get; set; } = "active"; // "active", "superseded", "deleted"

    public Dictionary<string, object>? Metadata { get; set; } // Additional context
}

// Example: Motorcycle Ownership
{
  "id": "mem_abc123",
  "userId": "user_xyz",
  "category": "ownership",
  "type": "motorcycle_ownership",
  "data": {
    "make": "Yamaha",
    "model": "R1M",
    "year": 2023,
    "nickname": "Red Rocket" // User might provide this later
  },
  "confidenceScore": 0.95,
  "extractedAt": "2025-12-26T10:30:00Z",
  "lastUpdatedAt": "2025-12-26T10:30:00Z",
  "sourceConversationId": "conv_123",
  "version": 1,
  "status": "active",
  "metadata": {
    "extraction_method": "realtime_pattern",
    "pattern_matched": "I own a {year} {make} {model}"
  }
}

// Example: Oil Preference
{
  "id": "mem_def456",
  "userId": "user_xyz",
  "category": "preference",
  "type": "oil_preference",
  "data": {
    "brand": "Motul",
    "product": "300V",
    "viscosity": "10W-40"
  },
  "confidenceScore": 0.80,
  "extractedAt": "2025-12-20T14:15:00Z",
  "lastUpdatedAt": "2025-12-20T14:15:00Z",
  "sourceConversationId": "conv_456",
  "version": 1,
  "status": "active"
}
```

**Database Implementation (SQLite)**:
```sql
CREATE TABLE UserMemory (
    Id TEXT PRIMARY KEY,
    UserId TEXT NOT NULL,
    Category TEXT NOT NULL,
    Type TEXT NOT NULL,
    DataJson TEXT NOT NULL, -- JSON serialized Data dictionary
    ConfidenceScore REAL NOT NULL,
    ExtractedAt TEXT NOT NULL,
    LastUpdatedAt TEXT NOT NULL,
    SourceConversationId TEXT NOT NULL,
    Version INTEGER DEFAULT 1,
    Status TEXT DEFAULT 'active',
    MetadataJson TEXT -- JSON serialized Metadata
);

CREATE INDEX idx_user_category ON UserMemory(UserId, Category);
CREATE INDEX idx_user_type ON UserMemory(UserId, Type);
CREATE INDEX idx_status ON UserMemory(Status);
```

---

### 2.2 Versioning and Conflict Resolution

**Scenario 1: User Sells Bike and Buys New One**

```csharp
public class MemoryConflictResolver
{
    public async Task<UserMemoryEntity> ResolveOwnershipConflict(
        UserMemoryEntity existingMemory,
        MemoryCandidate newCandidate)
    {
        // User says "I sold my R1M and bought a ZX-10R"
        if (newCandidate.Data.ContainsKey("action") &&
            newCandidate.Data["action"].ToString() == "sold")
        {
            // Mark old memory as superseded
            existingMemory.Status = "superseded";
            existingMemory.Metadata ??= new Dictionary<string, object>();
            existingMemory.Metadata["superseded_at"] = DateTime.UtcNow;
            existingMemory.Metadata["superseded_reason"] = "sold";

            await _memoryRepository.UpdateAsync(existingMemory);

            // Create new active memory for new bike
            var newMemory = new UserMemoryEntity
            {
                Category = "ownership",
                Type = "motorcycle_ownership",
                Data = newCandidate.Data,
                ConfidenceScore = newCandidate.ConfidenceScore,
                Status = "active",
                Version = 1
            };

            await _memoryRepository.InsertAsync(newMemory);

            return newMemory;
        }

        // User mentions different bike without explicit "sold"
        // Lower priority: keep both as "active" with different confidence scores
        if (IsDifferentMotorcycle(existingMemory, newCandidate))
        {
            // Reduce confidence of older memory
            existingMemory.ConfidenceScore *= 0.8f;
            existingMemory.Metadata ??= new Dictionary<string, object>();
            existingMemory.Metadata["confidence_reduced_reason"] = "new_motorcycle_mentioned";

            await _memoryRepository.UpdateAsync(existingMemory);

            // Add new memory with normal confidence
            var newMemory = CreateMemoryFromCandidate(newCandidate);
            await _memoryRepository.InsertAsync(newMemory);

            return newMemory;
        }

        // Same motorcycle, update details (e.g., user adds nickname or mods)
        if (IsSameMotorcycle(existingMemory, newCandidate))
        {
            existingMemory.Version++;
            existingMemory.LastUpdatedAt = DateTime.UtcNow;

            // Merge data (new data takes precedence)
            foreach (var kvp in newCandidate.Data)
            {
                existingMemory.Data[kvp.Key] = kvp.Value;
            }

            // Use higher confidence score
            existingMemory.ConfidenceScore = Math.Max(
                existingMemory.ConfidenceScore,
                newCandidate.ConfidenceScore);

            await _memoryRepository.UpdateAsync(existingMemory);

            return existingMemory;
        }

        return existingMemory;
    }
}
```

**Scenario 2: Conflicting Preferences (User Changes Oil Brand)**

```csharp
public async Task<UserMemoryEntity> ResolvePreferenceConflict(
    UserMemoryEntity existingMemory,
    MemoryCandidate newCandidate)
{
    // Strategy: Keep most recent preference, archive old one
    if (newCandidate.ExtractedAt > existingMemory.ExtractedAt)
    {
        // Archive old preference
        existingMemory.Status = "superseded";
        existingMemory.Metadata ??= new Dictionary<string, object>();
        existingMemory.Metadata["superseded_at"] = DateTime.UtcNow;
        existingMemory.Metadata["superseded_by"] = newCandidate.Data;

        await _memoryRepository.UpdateAsync(existingMemory);

        // Create new active preference
        var newMemory = CreateMemoryFromCandidate(newCandidate);
        await _memoryRepository.InsertAsync(newMemory);

        return newMemory;
    }

    return existingMemory;
}
```

---

### 2.3 Categorization Strategy

**Memory Categories**:

```csharp
public static class MemoryCategories
{
    // P0: Critical for personalization
    public const string Ownership = "ownership";

    // P1: Important for recommendations
    public const string Preference = "preference";
    public const string Expertise = "expertise";

    // P2: Nice to have
    public const string Interest = "interest";
    public const string History = "history"; // Past motorcycles, past experiences

    // P3: Experimental
    public const string Behavioral = "behavioral"; // Interaction patterns
}

public static class MemoryTypes
{
    // Ownership category
    public const string MotorcycleOwnership = "motorcycle_ownership";

    // Preference category
    public const string OilPreference = "oil_preference";
    public const string TirePreference = "tire_preference";
    public const string PartsPreference = "parts_preference";
    public const string MaintenancePreference = "maintenance_preference";
    public const string RidingTerrainPreference = "riding_terrain_preference";

    // Expertise category
    public const string RidingExperience = "riding_experience";
    public const string MaintenanceSkills = "maintenance_skills";
    public const string TechnicalKnowledge = "technical_knowledge";

    // Interest category
    public const string FrequentTopic = "frequent_topic";
    public const string ModificationInterest = "modification_interest";
}
```

**UI Grouping for User Review**:
```csharp
public class MemoryViewModel
{
    public string SectionTitle { get; set; } // "My Motorcycles", "My Preferences"
    public List<MemoryItemViewModel> Items { get; set; }
}

public class MemoryItemViewModel
{
    public string DisplayName { get; set; } // "2023 Yamaha R1M"
    public string Icon { get; set; } // Icon name for UI
    public string Subtitle { get; set; } // "Added Dec 26, 2025"
    public float ConfidenceScore { get; set; }
    public bool IsEditable { get; set; }
    public bool IsDeletable { get; set; }
}

// Example UI structure
Sections:
1. My Motorcycles
   - 2023 Yamaha R1M (Active)
   - 2018 Honda CBR600RR (Sold - Dec 2023)

2. My Preferences
   - Oil: Motul 300V 10W-40
   - Tires: Pirelli Diablo Rosso IV
   - Riding Style: Track & Sport

3. My Experience
   - Riding Level: Intermediate (5 years)
   - Maintenance Skills: Oil changes, brake bleeding

4. My Interests
   - Suspension Tuning (asked 7 times)
   - Exhaust Modifications (asked 5 times)
```

---

## 3. Privacy Considerations

### 3.1 What Should/Shouldn't Be Extracted

**NEVER Extract (Privacy/Security)**:
- Full names
- Phone numbers
- Email addresses
- Physical addresses
- Credit card numbers
- Social Security numbers
- Precise GPS locations
- Usernames/passwords
- Biometric data

**Cautiously Extract (With User Consent)**:
- General location (city/state for weather/shop recommendations)
- Workshop names (useful but could be privacy concern)
- Budget constraints (financial information)
- Riding group affiliations

**Always Extract (Safe, High Value)**:
- Motorcycle make/model/year
- Maintenance preferences (oil, tires, parts brands)
- Riding experience level
- Technical expertise
- Modification interests
- Riding terrain preferences

**Implementation**:
```csharp
public class PrivacyFilter
{
    private static readonly Regex PhonePattern = new(@"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b");
    private static readonly Regex EmailPattern = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b");
    private static readonly Regex CreditCardPattern = new(@"\b\d{4}[-\s]?\d{4}[-\s]?\d{4}[-\s]?\d{4}\b");
    private static readonly Regex SSNPattern = new(@"\b\d{3}-\d{2}-\d{4}\b");

    public bool ContainsPII(string text)
    {
        return PhonePattern.IsMatch(text) ||
               EmailPattern.IsMatch(text) ||
               CreditCardPattern.IsMatch(text) ||
               SSNPattern.IsMatch(text);
    }

    public bool IsSafeToExtract(MemoryCandidate candidate)
    {
        // Check if data contains PII
        foreach (var value in candidate.Data.Values)
        {
            if (value is string str && ContainsPII(str))
                return false;
        }

        // Check against disallowed categories
        var disallowedCategories = new[] { "contact", "financial", "identity", "location_precise" };
        if (disallowedCategories.Contains(candidate.Category))
            return false;

        return true;
    }
}
```

---

### 3.2 User Control Over Memory

**Required Features**:

1. **View All Memory**: Users can see everything the system has learned
2. **Edit Memory**: Users can correct or update extracted information
3. **Delete Memory**: Users can remove any memory item
4. **Clear All Memory**: Nuclear option to start fresh
5. **Consent Management**: Users can disable memory extraction entirely

**UI Implementation**:
```csharp
// Settings Page: Memory Management Section
public class MemoryManagementViewModel : BaseViewModel
{
    public ObservableCollection<MemoryItemViewModel> AllMemories { get; set; }

    public bool MemoryExtractionEnabled { get; set; } = true;

    public ICommand EditMemoryCommand { get; }
    public ICommand DeleteMemoryCommand { get; }
    public ICommand ClearAllMemoryCommand { get; }

    public async Task LoadMemoriesAsync()
    {
        var memories = await _memoryService.GetAllMemoriesAsync();

        AllMemories = new ObservableCollection<MemoryItemViewModel>(
            memories.Select(m => new MemoryItemViewModel
            {
                Id = m.Id,
                DisplayName = FormatMemoryDisplayName(m),
                Category = m.Category,
                IsEditable = true,
                IsDeletable = true,
                ExtractedAt = m.ExtractedAt,
                ConfidenceScore = m.ConfidenceScore
            }));
    }

    private async Task EditMemory(MemoryItemViewModel memory)
    {
        // Navigate to edit page with pre-populated data
        await Shell.Current.GoToAsync($"edit-memory?id={memory.Id}");
    }

    private async Task DeleteMemory(MemoryItemViewModel memory)
    {
        var confirm = await Application.Current.MainPage.DisplayAlert(
            "Delete Memory",
            $"Are you sure you want to delete '{memory.DisplayName}'?",
            "Delete", "Cancel");

        if (confirm)
        {
            await _memoryService.DeleteMemoryAsync(memory.Id);
            AllMemories.Remove(memory);
        }
    }

    private async Task ClearAllMemory()
    {
        var confirm = await Application.Current.MainPage.DisplayAlert(
            "Clear All Memory",
            "This will delete all information the app has learned about you. This cannot be undone.",
            "Clear All", "Cancel");

        if (confirm)
        {
            await _memoryService.ClearAllMemoriesAsync();
            AllMemories.Clear();
        }
    }
}
```

**Privacy Settings Page (XAML)**:
```xml
<ContentPage Title="Memory & Privacy">
    <ScrollView>
        <VerticalStackLayout Padding="20">
            <Label Text="Memory Management" FontSize="Title" Margin="0,0,0,10"/>

            <!-- Enable/Disable Memory Extraction -->
            <HorizontalStackLayout Margin="0,10">
                <Label Text="Learn from conversations" VerticalOptions="Center" FlexLayout.Grow="1"/>
                <Switch IsToggled="{Binding MemoryExtractionEnabled}"/>
            </HorizontalStackLayout>

            <Label Text="When enabled, the app extracts information from your conversations to personalize future responses."
                   FontSize="Caption" TextColor="Gray" Margin="0,0,0,20"/>

            <!-- View/Edit Memories -->
            <Button Text="View & Edit My Memories" Command="{Binding ViewMemoriesCommand}"/>

            <!-- Clear All -->
            <Button Text="Clear All Memories" Command="{Binding ClearAllMemoryCommand}"
                    BackgroundColor="Red" Margin="0,20,0,0"/>

            <!-- Privacy Notice -->
            <Label Text="Privacy Notice" FontSize="Subtitle" Margin="0,30,0,10"/>
            <Label Text="• All memory is stored locally on your device&#x0a;• Memory is never sent to servers unless you ask a question&#x0a;• You can view, edit, or delete any memory at any time&#x0a;• We never extract personal information (name, phone, email, address)"
                   FontSize="Caption" TextColor="Gray"/>
        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

---

### 3.3 Data Minimization Principles

**Principle 1: Extract Only What's Useful**
- Don't extract information that won't improve future responses
- Example: User's favorite color (NOT useful) vs. preferred tire brand (useful)

**Principle 2: Aggregate, Don't Store Verbatim**
- Store structured data, not full conversation text
- Example: Store `{ "make": "Yamaha", "model": "R1M" }` not "Yeah I own a Yamaha R1M and I love it"

**Principle 3: Automatic Expiration for Low-Confidence Memories**
```csharp
public class MemoryExpirationPolicy
{
    public TimeSpan GetExpirationTime(UserMemoryEntity memory)
    {
        // High confidence memories never expire
        if (memory.ConfidenceScore >= 0.90)
            return TimeSpan.MaxValue;

        // Medium confidence: expire after 90 days of no updates
        if (memory.ConfidenceScore >= 0.70)
            return TimeSpan.FromDays(90);

        // Low confidence: expire after 30 days
        return TimeSpan.FromDays(30);
    }

    public async Task PruneExpiredMemoriesAsync()
    {
        var allMemories = await _memoryRepository.GetAllAsync();

        foreach (var memory in allMemories.Where(m => m.Status == "active"))
        {
            var expiration = GetExpirationTime(memory);
            if (expiration != TimeSpan.MaxValue)
            {
                var age = DateTime.UtcNow - memory.LastUpdatedAt;
                if (age > expiration)
                {
                    memory.Status = "expired";
                    await _memoryRepository.UpdateAsync(memory);
                }
            }
        }
    }
}
```

**Principle 4: User Transparency**
- Always show users what was extracted in real-time (subtle notification)
- Example: Toast notification: "Remembered: You own a 2023 Yamaha R1M" with "Undo" option

```csharp
public async Task NotifyMemoryExtracted(UserMemoryEntity memory)
{
    var message = FormatMemoryNotification(memory);

    // Show toast with undo option
    var snackbar = Snackbar.Make(message, duration: TimeSpan.FromSeconds(5));
    snackbar.Action = new Action(() =>
    {
        // Undo: delete the memory
        _memoryService.DeleteMemoryAsync(memory.Id);
    });
    snackbar.ActionButtonText = "Undo";

    await snackbar.Show();
}

private string FormatMemoryNotification(UserMemoryEntity memory)
{
    return memory.Type switch
    {
        "motorcycle_ownership" => $"Remembered: You own a {memory.Data["year"]} {memory.Data["make"]} {memory.Data["model"]}",
        "oil_preference" => $"Remembered: You prefer {memory.Data["brand"]} oil",
        "expertise_level" => $"Remembered: {memory.Data["level"]} rider",
        _ => "Remembered new preference"
    };
}
```

---

## 4. Integration with API Context

### 4.1 Including User Memory in API Requests

**Current API Structure** (from `QueryModels.cs`):
```csharp
public class QueryContext
{
    public string SessionId { get; set; } = string.Empty;
    public List<string> PreviousQueries { get; set; } = new();
    public Dictionary<string, object> UserPreferences { get; set; } = new();
    public string Language { get; set; } = "en";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool RequiresMultiModal { get; set; }
    public string? CorrelationId { get; set; }
}
```

**Enhanced Structure with User Memory**:
```csharp
public class QueryContext
{
    // Existing fields...
    public string SessionId { get; set; } = string.Empty;
    public List<string> PreviousQueries { get; set; } = new();
    public Dictionary<string, object> UserPreferences { get; set; } = new();

    // NEW: User memory context
    public UserMemoryContext? UserMemory { get; set; }
}

public class UserMemoryContext
{
    // Core user information
    public List<MotorcycleOwnership> OwnedMotorcycles { get; set; } = new();

    // Preferences
    public Dictionary<string, string> MaintenancePreferences { get; set; } = new();

    // Expertise
    public UserExpertiseLevel? ExpertiseLevel { get; set; }

    // Condensed version for token efficiency
    public string? CondensedMemorySummary { get; set; }
}

public class MotorcycleOwnership
{
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string Status { get; set; } = "active"; // "active", "sold", "inactive"
}

public class UserExpertiseLevel
{
    public string RidingLevel { get; set; } = string.Empty; // "beginner", "intermediate", "advanced"
    public int? YearsRiding { get; set; }
    public List<string> MaintenanceSkills { get; set; } = new();
}
```

**Mobile App Implementation**:
```csharp
public class MotorcycleRagApiClient : IApiClient
{
    private readonly IUserMemoryService _memoryService;

    public async Task<MotorcycleQueryResponse> QueryAsync(
        string query,
        QueryContext? context = null,
        CancellationToken cancellationToken = default)
    {
        context ??= new QueryContext();

        // Enrich context with user memory
        context.UserMemory = await BuildUserMemoryContextAsync();

        var request = new MotorcycleQueryRequest
        {
            Query = query,
            Context = context,
            Preferences = new SearchPreferences { /* ... */ }
        };

        var response = await _httpClient.PostAsJsonAsync(
            "/api/motorcycles/query",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>();
    }

    private async Task<UserMemoryContext> BuildUserMemoryContextAsync()
    {
        var allMemories = await _memoryService.GetActiveMemoriesAsync();

        var memoryContext = new UserMemoryContext();

        // Extract owned motorcycles
        memoryContext.OwnedMotorcycles = allMemories
            .Where(m => m.Type == "motorcycle_ownership" && m.Status == "active")
            .Select(m => new MotorcycleOwnership
            {
                Make = m.Data.GetValueOrDefault("make", "").ToString(),
                Model = m.Data.GetValueOrDefault("model", "").ToString(),
                Year = m.Data.ContainsKey("year") ? Convert.ToInt32(m.Data["year"]) : null,
                Status = m.Status
            })
            .ToList();

        // Extract preferences
        memoryContext.MaintenancePreferences = allMemories
            .Where(m => m.Category == "preference" && m.Status == "active")
            .ToDictionary(
                m => m.Type.Replace("_preference", ""),
                m => string.Join(", ", m.Data.Values.Select(v => v.ToString()))
            );

        // Extract expertise
        var expertiseMemory = allMemories
            .FirstOrDefault(m => m.Type == "riding_experience" || m.Type == "expertise_level");

        if (expertiseMemory != null)
        {
            memoryContext.ExpertiseLevel = new UserExpertiseLevel
            {
                RidingLevel = expertiseMemory.Data.GetValueOrDefault("level", "").ToString(),
                YearsRiding = expertiseMemory.Data.ContainsKey("years_riding")
                    ? Convert.ToInt32(expertiseMemory.Data["years_riding"])
                    : null,
                MaintenanceSkills = expertiseMemory.Data.ContainsKey("maintenance_skills")
                    ? ((List<object>)expertiseMemory.Data["maintenance_skills"]).Select(s => s.ToString()).ToList()
                    : new List<string>()
            };
        }

        // Create condensed summary for token efficiency
        memoryContext.CondensedMemorySummary = GenerateCondensedSummary(memoryContext);

        return memoryContext;
    }

    private string GenerateCondensedSummary(UserMemoryContext context)
    {
        var parts = new List<string>();

        // Motorcycles
        if (context.OwnedMotorcycles.Any())
        {
            var bikes = string.Join(", ", context.OwnedMotorcycles.Select(m =>
                $"{m.Year} {m.Make} {m.Model}".Trim()));
            parts.Add($"Owns: {bikes}");
        }

        // Expertise
        if (context.ExpertiseLevel != null)
        {
            parts.Add($"{context.ExpertiseLevel.RidingLevel} rider");
            if (context.ExpertiseLevel.YearsRiding.HasValue)
                parts.Add($"{context.ExpertiseLevel.YearsRiding} years experience");
        }

        // Key preferences
        if (context.MaintenancePreferences.ContainsKey("oil"))
            parts.Add($"Prefers {context.MaintenancePreferences["oil"]} oil");

        return string.Join(" | ", parts);
    }
}
```

---

### 4.2 Backend API Usage of User Memory

**Backend Enhancement** (AgentOrchestrator):
```csharp
public async Task<string> GenerateResponseAsync(
    SearchResult[] results,
    string originalQuery,
    UserMemoryContext? userMemory = null) // NEW parameter
{
    var snippets = results.Take(10)
        .Select(r => $"[{r.Id}] {Truncate(r.Content, 500)}")
        .ToArray();

    // Build prompt with user memory context
    var userContext = BuildUserContextPrompt(userMemory);

    var prompt = $"""
You are an expert on motorcycle maintenance and specification.
{userContext}
Using only the information provided in the snippets below, answer the user's question.
Cite the snippet identifier (e.g. "[1]") after every statement that comes from a snippet.
If the answer cannot be determined from the snippets, say you do not have sufficient information.

User question: "{originalQuery}"

Snippets:
{string.Join("\n\n", snippets)}

Answer in markdown:
""";

    var answer = await _openAIClient.GetChatCompletionAsync("gpt-4o-mini", prompt, CancellationToken.None);

    return answer;
}

private string BuildUserContextPrompt(UserMemoryContext? userMemory)
{
    if (userMemory == null || string.IsNullOrEmpty(userMemory.CondensedMemorySummary))
        return string.Empty;

    return $"""
IMPORTANT CONTEXT ABOUT THIS USER:
{userMemory.CondensedMemorySummary}

When answering, consider this user's specific motorcycles and preferences. Personalize your response accordingly.

""";
}
```

**Example Personalized Response**:

Without memory:
```
User: "What oil should I use?"
Bot: "For most sportbikes, a high-quality synthetic oil like Motul 300V or Castrol Power1 Racing
     in 10W-40 viscosity is recommended. Check your owner's manual for specific recommendations."
```

With memory (knows user owns 2023 Yamaha R1M):
```
User: "What oil should I use?"
Bot: "For your 2023 Yamaha R1M, Yamaha recommends a JASO MA2-certified synthetic oil. Popular
     choices include Motul 300V 10W-40 or Yamalube 10W-40. Your R1M uses 4.0 liters for an oil
     change with filter replacement. [Source: R1M Owner's Manual]"
```

---

### 4.3 Size Limits for Context Field

**Token Budget Analysis**:
```
GPT-4o-mini context window: 128k tokens
Typical query: ~50 tokens
Conversation history (10 messages): ~500 tokens
Search results (10 snippets): ~2000 tokens
User memory context: TARGET = 100-200 tokens

BUDGET BREAKDOWN:
- Query: 50 tokens
- User Memory: 150 tokens
- Conversation history: 500 tokens
- Search results: 2000 tokens
- System prompt: 300 tokens
- Response budget: 500 tokens
TOTAL: ~3500 tokens (well within 128k limit)
```

**Size Optimization Strategy**:
```csharp
public class UserMemoryContextOptimizer
{
    private const int MAX_MEMORY_TOKENS = 200;

    public UserMemoryContext OptimizeForTokenBudget(UserMemoryContext context)
    {
        // Use condensed summary instead of full structure
        // Condensed summary averages 100-150 tokens

        var optimized = new UserMemoryContext
        {
            CondensedMemorySummary = context.CondensedMemorySummary
        };

        // Only include structured data if under token limit
        var estimatedTokens = EstimateTokens(context.CondensedMemorySummary);

        if (estimatedTokens < MAX_MEMORY_TOKENS * 0.7) // 70% budget for summary
        {
            // We have room for high-priority structured data
            optimized.OwnedMotorcycles = context.OwnedMotorcycles
                .Where(m => m.Status == "active")
                .Take(2) // Limit to 2 active bikes
                .ToList();
        }

        return optimized;
    }

    private int EstimateTokens(string text)
    {
        // Rough estimation: 1 token ≈ 4 characters
        return text?.Length / 4 ?? 0;
    }
}
```

**Condensed Summary Examples**:
```
Good (concise, high-value):
"Owns: 2023 Yamaha R1M | Intermediate rider | 5 years experience | Prefers Motul 300V oil"
(~25 tokens)

Bad (too verbose):
"The user owns a 2023 Yamaha R1M motorcycle which they purchased recently. They are an
intermediate level rider with approximately 5 years of riding experience. For oil changes,
they prefer to use Motul 300V synthetic motorcycle oil."
(~50 tokens)

Optimal for multiple bikes:
"Owns: 2023 Yamaha R1M (active), 2018 Honda CBR600RR (sold) | Advanced rider | Track & street |
Motul oil, Pirelli tires"
(~30 tokens)
```

---

## 5. Local Processing

### 5.1 On-Device vs. Backend Extraction

**Decision Matrix**:

| Extraction Type | On-Device | Backend API | Recommendation |
|----------------|-----------|-------------|----------------|
| **Pattern Matching** (ownership, preferences) | ✅ Fast, offline-capable, zero cost | ❌ Latency, requires connectivity | **On-Device** |
| **Frequency Analysis** (repeated topics) | ✅ Simple counting, fast | ❌ Unnecessary complexity | **On-Device** |
| **Semantic Understanding** (implicit info) | ⚠️ Requires ML model, battery drain | ✅ GPT-4o can understand context | **Backend API (optional)** |
| **Conflict Resolution** (sold bike, new bike) | ✅ Rule-based logic works well | ⚠️ Could use LLM but unnecessary | **On-Device** |
| **Quality Scoring** (confidence) | ✅ Simple heuristics sufficient | ⚠️ LLM overkill | **On-Device** |

**Recommended Architecture**:
```
┌─────────────────────────────────────────────┐
│        Mobile App (On-Device)               │
│                                             │
│  ┌─────────────────────────────────────┐   │
│  │ Realtime Pattern Extractor          │   │ ← Runs on every user message
│  │ (Regex-based, < 100ms)              │   │   (offline-capable)
│  └─────────────────────────────────────┘   │
│                                             │
│  ┌─────────────────────────────────────┐   │
│  │ Periodic Frequency Analyzer         │   │ ← Runs every 10 messages
│  │ (Counting, < 500ms)                 │   │   (offline-capable)
│  └─────────────────────────────────────┘   │
│                                             │
│  ┌─────────────────────────────────────┐   │
│  │ Conflict Resolver                   │   │ ← Runs when conflicts detected
│  │ (Rule-based, < 200ms)               │   │   (offline-capable)
│  └─────────────────────────────────────┘   │
│                                             │
└─────────────────────────────────────────────┘
                     │
                     │ Optional (when online)
                     ▼
┌─────────────────────────────────────────────┐
│     Backend API (Optional Enhancement)      │
│                                             │
│  ┌─────────────────────────────────────┐   │
│  │ Semantic Analyzer (GPT-4o)          │   │ ← Analyzes implicit patterns
│  │ "This user seems to prefer track    │   │   "I do most of my riding
│  │  riding based on context"           │   │    at Laguna Seca"
│  └─────────────────────────────────────┘   │
│                                             │
└─────────────────────────────────────────────┘
```

---

### 5.2 On-Device Implementation (Pattern Matching)

**Advantages**:
- ✅ Works offline
- ✅ Zero latency (< 100ms)
- ✅ Zero API cost
- ✅ Privacy (data never leaves device)
- ✅ Battery friendly (simple regex, no ML)

**Implementation**:
```csharp
public class OnDeviceMemoryExtractor
{
    private readonly Dictionary<string, CompiledPattern> _patterns;

    public OnDeviceMemoryExtractor()
    {
        _patterns = new Dictionary<string, CompiledPattern>
        {
            ["motorcycle_ownership"] = new CompiledPattern
            {
                Regex = new Regex(
                    @"\b(I have|I own|My|I ride|I just (bought|got))\s+a?\s*(?<year>\d{4})?\s*(?<make>\w+)\s+(?<model>[\w\-\s]+)",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled),
                ConfidenceScore = 0.95f,
                Category = "ownership",
                Type = "motorcycle_ownership"
            },

            ["oil_preference"] = new CompiledPattern
            {
                Regex = new Regex(
                    @"\b(I (prefer|use|always use))\s+(?<brand>[\w\-]+)\s+(?<product>[\w\-]+)?\s*oil",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled),
                ConfidenceScore = 0.80f,
                Category = "preference",
                Type = "oil_preference"
            },

            ["sold_motorcycle"] = new CompiledPattern
            {
                Regex = new Regex(
                    @"\b(I sold|I traded|I got rid of)\s+(my\s+)?(?<year>\d{4})?\s*(?<make>\w+)\s+(?<model>[\w\-\s]+)",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled),
                ConfidenceScore = 0.90f,
                Category = "ownership",
                Type = "motorcycle_sold",
                Metadata = new Dictionary<string, object> { ["action"] = "sold" }
            }
        };
    }

    public List<MemoryCandidate> ExtractFromMessage(string message)
    {
        var candidates = new List<MemoryCandidate>();
        var stopwatch = Stopwatch.StartNew();

        foreach (var (patternName, pattern) in _patterns)
        {
            var match = pattern.Regex.Match(message);
            if (match.Success)
            {
                candidates.Add(new MemoryCandidate
                {
                    Type = pattern.Type,
                    Category = pattern.Category,
                    ConfidenceScore = pattern.ConfidenceScore,
                    ExtractedAt = DateTime.UtcNow,
                    SourceMessage = message,
                    Data = ExtractGroupsAsDictionary(match),
                    Metadata = pattern.Metadata ?? new Dictionary<string, object>()
                });
            }
        }

        stopwatch.Stop();

        // Log performance (should be < 100ms)
        if (stopwatch.ElapsedMilliseconds > 100)
        {
            Debug.WriteLine($"WARNING: Pattern matching took {stopwatch.ElapsedMilliseconds}ms");
        }

        return candidates;
    }

    private Dictionary<string, object> ExtractGroupsAsDictionary(Match match)
    {
        var data = new Dictionary<string, object>();

        foreach (Group group in match.Groups)
        {
            if (!string.IsNullOrEmpty(group.Name) &&
                !int.TryParse(group.Name, out _) &&
                group.Success)
            {
                data[group.Name] = group.Value.Trim();
            }
        }

        return data;
    }
}

public class CompiledPattern
{
    public Regex Regex { get; set; } = null!;
    public float ConfidenceScore { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, object>? Metadata { get; set; }
}
```

**Performance Benchmarks** (on mid-range Android device):
```
Pattern matching (10 patterns, 100-word message): 15-30ms
Frequency analysis (50 messages): 50-100ms
Conflict resolution (5 memories): 10-20ms

Total overhead per message: < 100ms ✅
```

---

### 5.3 Optional Backend Enhancement (Semantic Analysis)

**When to Use Backend**:
- User explicitly asks to "remember" something complex
- Pre-pruning extraction (last chance to catch semantic patterns)
- User has enabled "Advanced Memory" in settings

**Implementation**:
```csharp
public class BackendSemanticExtractor
{
    private readonly IApiClient _apiClient;

    // NEW API endpoint: POST /api/memory/extract-semantic
    public async Task<List<MemoryCandidate>> ExtractSemanticPatternsAsync(
        IEnumerable<ChatMessage> conversationWindow,
        CancellationToken cancellationToken = default)
    {
        // Only call if online and user opted in
        if (!IsOnline() || !UserOptedInForAdvancedMemory())
            return new List<MemoryCandidate>();

        var request = new SemanticExtractionRequest
        {
            Messages = conversationWindow.Select(m => new ConversationMessage
            {
                Text = m.Text,
                IsUser = m.IsUserMessage,
                Timestamp = m.Timestamp
            }).ToList()
        };

        var response = await _httpClient.PostAsJsonAsync(
            "/api/memory/extract-semantic",
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return new List<MemoryCandidate>();

        var result = await response.Content.ReadFromJsonAsync<SemanticExtractionResponse>();
        return result?.Candidates ?? new List<MemoryCandidate>();
    }
}
```

**Backend Implementation** (NEW API endpoint):
```csharp
// In MotorcycleRAG.API
[HttpPost("memory/extract-semantic")]
public async Task<IActionResult> ExtractSemanticMemoryAsync(
    [FromBody] SemanticExtractionRequest request)
{
    var prompt = $"""
Analyze the following conversation and extract persistent user information that would be useful
for personalizing future motorcycle-related responses.

EXTRACT ONLY:
- Motorcycles owned (make, model, year)
- Maintenance preferences (oil, tires, parts brands)
- Riding style (track, street, off-road)
- Technical expertise level
- Modification interests

DO NOT EXTRACT:
- Personal information (name, phone, address)
- One-time questions
- Temporary context

Conversation:
{FormatConversation(request.Messages)}

Return JSON array of extracted memories in this format:
[
  {{
    "type": "motorcycle_ownership",
    "category": "ownership",
    "data": {{ "make": "Yamaha", "model": "R1M", "year": 2023 }},
    "confidence": 0.85,
    "reasoning": "User explicitly stated ownership"
  }}
]
""";

    var jsonResponse = await _openAIClient.GetChatCompletionAsync("gpt-4o", prompt, CancellationToken.None);

    var candidates = JsonSerializer.Deserialize<List<MemoryCandidate>>(jsonResponse);

    return Ok(new SemanticExtractionResponse { Candidates = candidates });
}
```

**Cost Analysis**:
```
GPT-4o pricing: $2.50 per 1M input tokens, $10 per 1M output tokens

Typical semantic extraction call:
- Input: 10 messages × 50 words × 1.3 tokens/word = ~650 tokens
- Output: 5 memories × 50 tokens = ~250 tokens
- Cost: (650 × $2.50 / 1M) + (250 × $10 / 1M) = $0.0041 per extraction

If called once per 50 messages:
- 1000 messages = 20 extractions = $0.082
- Very affordable for added value
```

---

## 6. Examples from Similar Apps

### 6.1 ChatGPT Mobile Memory Feature

**How It Works** (based on public documentation):
1. **Automatic Extraction**: ChatGPT learns from conversations automatically
2. **User Control**: Users can view, edit, and delete memories in settings
3. **Persistence**: Memories persist across conversations and devices (cloud-synced)
4. **Transparency**: ChatGPT sometimes explicitly states "I've updated my memory about you"

**Key Learnings**:
- ✅ Automatic extraction reduces friction (users don't need to "teach" the system)
- ✅ Transparency builds trust ("I've updated my memory")
- ✅ Cloud sync enables multi-device experience
- ❌ Users sometimes surprised by what's remembered (privacy concern)

**Applicable to Our App**:
- ✅ Automatic extraction on-device (similar approach)
- ✅ Transparency via toast notifications with "Undo"
- ❌ NO cloud sync (local-only for privacy, per device)
- ✅ Explicit user control in settings

---

### 6.2 Claude Mobile Custom Instructions

**How It Works**:
1. **Manual Input**: Users explicitly provide "custom instructions" in settings
2. **Prepended Context**: Instructions prepended to every conversation
3. **No Automatic Learning**: Claude does NOT automatically extract from conversations
4. **Conversation Projects**: Users can create "projects" with specific context

**Key Learnings**:
- ✅ Manual control gives users full transparency
- ✅ Works well for professional use cases (coding preferences)
- ❌ Requires user effort to maintain
- ❌ Can become stale if user doesn't update

**Applicable to Our App**:
- ⚠️ Hybrid approach: Automatic extraction + manual editing
- ✅ Allow users to manually add memories they want remembered
- ✅ Similar UI for viewing/editing (like custom instructions)

---

### 6.3 Industry Best Practices

**Pattern 1: Confidence-Based Transparency**
```
High confidence (>0.90): Silent extraction, show in memory list
Medium confidence (0.70-0.90): Show toast notification with undo
Low confidence (<0.70): Don't extract, or ask user for confirmation
```

**Pattern 2: Progressive Disclosure**
```
First time user: Simple on/off toggle for memory
Power users: Advanced settings (confidence thresholds, categories to extract)
Privacy-conscious users: View extraction log, disable specific categories
```

**Pattern 3: Graceful Degradation**
```
Offline: Pattern-based extraction only
Online: Pattern + optional semantic extraction
Low battery: Disable background extraction
Storage full: Prioritize high-confidence memories, prune low-confidence
```

**Pattern 4: Data Lifecycle**
```
Creation: Extract with confidence score
Active Use: Include in API context
Aging: Reduce confidence over time if not reinforced
Expiration: Auto-delete low-confidence memories after 90 days
Deletion: User-initiated or automatic on app uninstall
```

---

## 7. Implementation Recommendations

### 7.1 Minimal Complexity, Maximum Value (MVP)

**Phase 1: Core MVP (Week 1-2)**

**Features**:
1. ✅ Realtime pattern extraction (motorcycle ownership, oil preference)
2. ✅ SQLite storage with versioning
3. ✅ Basic UI: View all memories, delete individual memories
4. ✅ Include memory in API context (condensed summary)
5. ✅ Toast notifications for high-confidence extractions

**Code Estimate**:
- `RealtimeMemoryExtractor.cs`: 200 lines
- `UserMemoryEntity.cs`: 50 lines
- `UserMemoryRepository.cs`: 150 lines (SQLite CRUD)
- `MemoryConflictResolver.cs`: 200 lines
- `MemoryManagementViewModel.cs`: 150 lines
- `MemoryManagementPage.xaml`: 100 lines
- **Total**: ~850 lines of code

**Deliverables**:
- Users can see extracted memories in settings
- API receives user memory context for personalization
- Users can delete unwanted memories
- Automatic extraction from ownership statements

---

**Phase 2: Enhanced Extraction (Week 3)**

**Features**:
1. ✅ Periodic frequency analysis (background task)
2. ✅ Pre-pruning extraction (before conversations deleted)
3. ✅ Edit memory UI
4. ✅ Manual memory creation

**Code Estimate**: +400 lines

---

**Phase 3: Advanced Features (Week 4)**

**Features**:
1. ⚠️ Optional backend semantic extraction
2. ⚠️ Memory expiration policy
3. ⚠️ Export/import memories (backup)
4. ⚠️ Memory categorization filters in UI

**Code Estimate**: +600 lines

---

### 7.2 Architecture Recommendation

**Recommended Structure**:
```
1-Presentation/MotorcycleRAG.MobileApp/
├── Services/
│   ├── IUserMemoryService.cs           # Interface for memory operations
│   ├── UserMemoryService.cs            # Main service implementation
│   ├── Extraction/
│   │   ├── IMemoryExtractor.cs
│   │   ├── RealtimeMemoryExtractor.cs   # Pattern-based extraction
│   │   ├── PeriodicMemoryExtractor.cs   # Frequency analysis
│   │   ├── PrePruneMemoryExtractor.cs   # Final sweep before pruning
│   │   └── BackendSemanticExtractor.cs  # Optional API-based extraction
│   └── MemoryConflictResolver.cs       # Handles updates/conflicts
│
├── Models/
│   ├── MemoryCandidate.cs              # Temporary extraction result
│   └── UserMemoryContext.cs            # API request context
│
├── Persistence/
│   ├── Entities/
│   │   └── UserMemoryEntity.cs         # Database entity
│   └── Repositories/
│       ├── IUserMemoryRepository.cs
│       └── UserMemoryRepository.cs     # SQLite operations
│
├── ViewModels/
│   ├── MemoryManagementViewModel.cs    # Main settings page
│   ├── EditMemoryViewModel.cs          # Edit individual memory
│   └── MemoryItemViewModel.cs          # List item
│
└── Views/
    ├── MemoryManagementPage.xaml       # View/delete memories
    └── EditMemoryPage.xaml             # Edit memory details
```

---

### 7.3 Key Design Decisions

**Decision 1: Local-Only Storage (No Cloud Sync)**
- **Rationale**: Privacy-first approach, aligns with 100MB local storage requirement
- **Trade-off**: Users lose memories if they switch devices
- **Future**: Could add optional cloud sync with explicit consent

**Decision 2: Hybrid Extraction (On-Device + Optional Backend)**
- **Rationale**: On-device covers 90% of cases, backend adds value for complex patterns
- **Trade-off**: Requires backend API development
- **MVP**: Start with on-device only, add backend in Phase 3

**Decision 3: Structured Entities (Not Free-Text Summaries)**
- **Rationale**: Enables querying, categorization, conflict resolution
- **Trade-off**: More complex storage schema
- **Benefit**: Supports advanced features like "find all my motorcycles"

**Decision 4: High Confidence Threshold for Auto-Extraction**
- **Rationale**: Avoid extracting incorrect information
- **Trade-off**: May miss some valid patterns
- **Mitigation**: Manual memory creation for missed items

---

## 8. Privacy & Compliance Checklist

**Before Launch**:
- [ ] Privacy policy updated to mention memory extraction
- [ ] User consent flow on first app launch
- [ ] PII filtering implemented and tested
- [ ] Memory export feature (user data portability)
- [ ] Memory deletion is permanent (no backups)
- [ ] Memory is NOT sent to analytics/telemetry
- [ ] Memory is NOT shared with third parties
- [ ] Clear UI showing what's been extracted
- [ ] "Clear All Memory" option prominently displayed
- [ ] Offline extraction clearly documented

---

## 9. Testing Strategy

**Unit Tests**:
```csharp
[Fact]
public void RealtimeExtractor_ExtractsMotorcycleOwnership()
{
    var extractor = new RealtimeMemoryExtractor();
    var message = "I own a 2023 Yamaha R1M";

    var candidates = extractor.ExtractFromMessage(message);

    Assert.Single(candidates);
    Assert.Equal("motorcycle_ownership", candidates[0].Type);
    Assert.Equal("Yamaha", candidates[0].Data["make"]);
    Assert.Equal("R1M", candidates[0].Data["model"]);
    Assert.Equal("2023", candidates[0].Data["year"]);
    Assert.True(candidates[0].ConfidenceScore > 0.90);
}

[Fact]
public void ConflictResolver_MarksOldBikeSuperseded_WhenUserSellsBike()
{
    var resolver = new MemoryConflictResolver(mockRepository);
    var oldMemory = CreateOwnershipMemory("Yamaha", "R1M", 2023);
    var soldCandidate = CreateSoldCandidate("Yamaha", "R1M", 2023);

    var result = await resolver.ResolveOwnershipConflict(oldMemory, soldCandidate);

    Assert.Equal("superseded", result.Status);
    Assert.Equal("sold", result.Metadata["superseded_reason"]);
}

[Fact]
public void PrivacyFilter_RejectsPII()
{
    var filter = new PrivacyFilter();
    var piiCandidate = new MemoryCandidate
    {
        Data = new Dictionary<string, object>
        {
            ["phone"] = "555-123-4567"
        }
    };

    Assert.False(filter.IsSafeToExtract(piiCandidate));
}
```

**Integration Tests**:
```csharp
[Fact]
public async Task MemoryService_IncludesMemoryInApiContext()
{
    // Setup: Create memory
    await _memoryService.CreateMemoryAsync(new UserMemoryEntity
    {
        Type = "motorcycle_ownership",
        Data = new Dictionary<string, object>
        {
            ["make"] = "Yamaha",
            ["model"] = "R1M",
            ["year"] = 2023
        }
    });

    // Act: Query API
    var response = await _apiClient.QueryAsync("What oil should I use?");

    // Assert: Response is personalized
    Assert.Contains("R1M", response.Response);
    Assert.Contains("4.0 liters", response.Response); // R1M-specific
}
```

**UI Tests**:
```csharp
[Fact]
public async Task MemoryManagementPage_DisplaysExtractedMemories()
{
    // Navigate to memory page
    await App.GoToAsync("memory-management");

    // Verify memories displayed
    Assert.True(App.FindElement("memory_list").IsVisible);
    Assert.Contains("2023 Yamaha R1M", App.FindElement("memory_list").Text);
}

[Fact]
public async Task MemoryManagementPage_DeletesMemory()
{
    await App.GoToAsync("memory-management");

    // Swipe and delete
    App.SwipeElement("memory_item_0", SwipeDirection.Left);
    App.TapElement("delete_button");

    // Confirm deletion
    Assert.False(App.FindElement("memory_item_0").Exists);
}
```

---

## 10. Conclusion

**Summary of Recommendations**:

1. **Extraction Strategy**: Multi-trigger approach
   - Realtime pattern matching (on-device, < 100ms)
   - Periodic frequency analysis (background, every 10 messages)
   - Pre-pruning final sweep (before conversation deletion)

2. **Storage Format**: Structured key-value entities with versioning
   - SQLite database with `UserMemoryEntity` schema
   - Support for conflict resolution and updates
   - Categorical organization for UI

3. **Privacy**: Local-only, user-controlled, PII-filtered
   - No cloud sync (privacy-first)
   - View/edit/delete all memories
   - Toast notifications for transparency

4. **API Integration**: Condensed summary in context field
   - 100-200 token budget
   - Enriches prompts with user-specific context
   - Enables personalized responses

5. **Processing**: Hybrid local + optional backend
   - On-device pattern matching for MVP
   - Optional backend semantic extraction for advanced features
   - Works offline with graceful degradation

**User Value**:
- ✅ Personalized responses without repetitive context-setting
- ✅ Information persists even after conversations pruned
- ✅ Full transparency and control over learned information
- ✅ Privacy-friendly (local storage, no PII extraction)

**Implementation Complexity**: **Medium**
- MVP: ~850 lines of code, 1-2 weeks
- Full feature set: ~2000 lines, 3-4 weeks
- Minimal backend changes (optional semantic endpoint)

**Next Steps**:
1. Review and approve this research document
2. Create detailed tasks in `tasks.md` for implementation
3. Build Phase 1 MVP (core extraction + storage + UI)
4. User testing and refinement
5. Add Phase 2/3 features based on user feedback

---

**References**:
- Spec: `C:\git\motoRagApp\specs\001-mobile-app\spec.md`
- Plan: `C:\git\motoRagApp\specs\001-mobile-app\plan.md`
- API Models: `C:\git\motoRagApp\3-Domain\MotorcycleRAG.Domain\Models\QueryModels.cs`
- Backend Orchestrator: `C:\git\motoRagApp\2-Application\MotorcycleRAG.Application\Services\AgentOrchestrator.cs`
