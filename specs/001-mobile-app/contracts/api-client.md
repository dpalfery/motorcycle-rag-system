# API Client Contract: MotorcycleRAG.API Integration

**Feature**: 001-mobile-app
**Date**: 2025-12-26
**Purpose**: Define the contract between the mobile app and the MotorcycleRAG.API backend

## Overview

This document specifies the HTTP API contract for the mobile app's integration with the existing MotorcycleRAG.API backend. The mobile app acts as an HTTP client consuming the `/api/motorcycles/query` endpoint.

## Base Configuration

**Base URL**: Configured via app settings (e.g., `https://api.motorcyclerag.com` or `https://localhost:5001` for development)

**Authentication**:
- **Type**: Bearer token (OAuth 2.0)
- **Header**: `Authorization: Bearer {access_token}`
- **Token Source**: Microsoft Entra External ID / B2C via MSAL
- **Token Refresh**: Automatic via MSAL (silent token acquisition)

**Headers** (all requests):
```http
Authorization: Bearer {access_token}
Content-Type: application/json
Accept: application/json
User-Agent: MotorcycleRAG-Mobile/1.0.0 ({Platform}/{Version})
X-Client-Version: 1.0.0
```

**Timeout**: 30 seconds per request

**Retry Policy**:
- 3 attempts with exponential backoff (1s, 2s, 4s)
- Retry on: 408, 429, 500, 502, 503, 504
- No retry on: 400, 401, 403, 404

---

## Endpoints

### POST /api/motorcycles/query

Send a motorcycle-related question and receive a conversational answer with sources.

**Request**:
```json
POST /api/motorcycles/query HTTP/1.1
Host: api.motorcyclerag.com
Authorization: Bearer eyJ0eXAiOiJKV1QiLCJhbGc...
Content-Type: application/json

{
  "query": "What's the horsepower of a 2023 Yamaha R1?",
  "userId": "auth0|123456",              // Optional, from token if not provided
  "preferences": {                        // Optional
    "includeWebSources": true,
    "includePDFSources": true,
    "maxResults": 5,
    "minRelevanceScore": 0.7
  },
  "context": {                            // Optional, for multi-turn and personalization
    "sessionId": "conv-abc-123",
    "previousQueries": [
      "What motorcycles does Yamaha make?"
    ],
    "userMemory": {                       // User memory from mobile app
      "motorcycles_owned": ["2023 Yamaha R1M"],
      "riding_style": "sport riding",
      "expertise_level": "intermediate"
    },
    "language": "en-US",
    "timestamp": "2025-12-26T12:34:56Z",
    "correlationId": "mobile-query-xyz-789"
  }
}
```

**Request Schema**:
```csharp
public class QueryRequest
{
    [Required]
    [StringLength(10000, MinimumLength = 1)]
    public string Query { get; set; }

    public string UserId { get; set; }  // Overridden by authenticated user ID

    public QueryPreferences Preferences { get; set; }

    public QueryContext Context { get; set; }
}

public class QueryPreferences
{
    public bool? IncludeWebSources { get; set; }
    public bool? IncludePDFSources { get; set; }
    public int? MaxResults { get; set; }
    public decimal? MinRelevanceScore { get; set; }
    public List<string> PreferredSources { get; set; }
}

public class QueryContext
{
    public string SessionId { get; set; }
    public List<string> PreviousQueries { get; set; }
    public Dictionary<string, object> UserMemory { get; set; }  // Mobile-specific
    public object UserPreferences { get; set; }
    public string Language { get; set; }
    public DateTime? Timestamp { get; set; }
    public bool? RequiresMultiModal { get; set; }
    public string CorrelationId { get; set; }
}
```

**Response (Success - 200 OK)**:
```json
HTTP/1.1 200 OK
Content-Type: application/json

{
  "response": "The 2023 Yamaha YZF-R1 produces 200 horsepower at 13,500 rpm. Since you own an R1M, you'll find the power delivery is very similar between the standard R1 and R1M models.",
  "sources": [
    {
      "id": "src-1",
      "content": "Engine: 998cc inline-four, 200 hp @ 13,500 rpm",
      "relevanceScore": 0.95,
      "source": {
        "agentType": "PdfSearch",
        "sourceName": "2023 Yamaha YZF-R1 Service Manual",
        "sourceUrl": null,
        "documentId": "yamaha-r1-2023-manual",
        "lastUpdated": "2023-01-15T00:00:00Z"
      },
      "metadata": {
        "pageNumber": 12,
        "section": "Engine Specifications",
        "chunkId": "chunk-456"
      },
      "generatedAt": "2025-12-26T12:35:01Z",
      "highlights": ["200 hp @ 13,500 rpm"]
    },
    {
      "id": "src-2",
      "content": "The R1 and R1M share the same engine with identical power output...",
      "relevanceScore": 0.88,
      "source": {
        "agentType": "WebSearch",
        "sourceName": "Yamaha Motor Corporation",
        "sourceUrl": "https://www.yamahamotorsports.com/sport/models/yzf-r1",
        "documentId": null,
        "lastUpdated": "2025-12-20T00:00:00Z"
      },
      "metadata": {
        "retrievalDate": "2025-12-26T12:35:00Z"
      },
      "generatedAt": "2025-12-26T12:35:02Z",
      "highlights": []
    }
  ],
  "metrics": {
    "totalDurationMs": 2847,
    "agentsUsed": ["QueryPlanner", "PdfSearch", "WebSearch"],
    "sourcesCombined": 2,
    "tokensUsed": 1523
  },
  "queryId": "query-2025-12-26-xyz-789",
  "generatedAt": "2025-12-26T12:35:02Z"
}
```

**Response Schema**:
```csharp
public class QueryResponse
{
    public string Response { get; set; }                    // Natural language answer
    public List<SearchResult> Sources { get; set; }         // Supporting sources
    public QueryMetrics Metrics { get; set; }               // Performance metrics
    public string QueryId { get; set; }                     // Unique query identifier
    public DateTime GeneratedAt { get; set; }               // When response was generated
}

public class SearchResult
{
    public string Id { get; set; }
    public string Content { get; set; }                     // Excerpt/snippet
    public decimal RelevanceScore { get; set; }
    public SourceInfo Source { get; set; }
    public Dictionary<string, object> Metadata { get; set; }  // Page, section, etc.
    public DateTime GeneratedAt { get; set; }
    public List<string> Highlights { get; set; }            // Highlighted text
}

public class SourceInfo
{
    public string AgentType { get; set; }                   // PdfSearch, WebSearch, VectorSearch
    public string SourceName { get; set; }                  // Display name
    public string SourceUrl { get; set; }                   // URL (web sources)
    public string DocumentId { get; set; }                  // PDF/dataset ID
    public DateTime? LastUpdated { get; set; }
}

public class QueryMetrics
{
    public long TotalDurationMs { get; set; }
    public List<string> AgentsUsed { get; set; }
    public int SourcesCombined { get; set; }
    public int? TokensUsed { get; set; }
}
```

**Response (Rate Limit - 429 Too Many Requests)**:
```json
HTTP/1.1 429 Too Many Requests
Content-Type: application/json

{
  "type": "https://httpstatuses.com/429",
  "title": "Daily request limit exceeded",
  "status": 429,
  "detail": "You have used all 10 requests allowed for your Free plan today. Limit resets at 2025-12-27T00:00:00Z.",
  "instance": "/api/motorcycles/query",
  "extensions": {
    "requestsUsedToday": 10,
    "dailyLimit": 10,
    "resetAt": "2025-12-27T00:00:00Z",
    "currentPlan": "Free"
  }
}
```

**Response (Authentication Error - 401 Unauthorized)**:
```json
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer error="invalid_token", error_description="The token is expired"
Content-Type: application/json

{
  "type": "https://httpstatuses.com/401",
  "title": "Unauthorized",
  "status": 401,
  "detail": "The access token is expired or invalid. Please re-authenticate."
}
```

**Response (Bad Request - 400 Bad Request)**:
```json
HTTP/1.1 400 Bad Request
Content-Type: application/json

{
  "type": "https://httpstatuses.com/400",
  "title": "Validation failed",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "errors": {
    "query": ["The query field is required."]
  }
}
```

**Response (Server Error - 500 Internal Server Error)**:
```json
HTTP/1.1 500 Internal Server Error
Content-Type: application/json

{
  "type": "https://httpstatuses.com/500",
  "title": "Internal server error",
  "status": 500,
  "detail": "An unexpected error occurred while processing your request.",
  "instance": "/api/motorcycles/query",
  "correlationId": "mobile-query-xyz-789"
}
```

---

### GET /api/motorcycles/health

Check API availability (does not require authentication).

**Request**:
```http
GET /api/motorcycles/health HTTP/1.1
Host: api.motorcyclerag.com
```

**Response (Success - 200 OK)**:
```json
HTTP/1.1 200 OK
Content-Type: application/json

{
  "status": "Healthy",
  "timestamp": "2025-12-26T12:35:05Z"
}
```

**Response (Degraded - 200 OK)**:
```json
{
  "status": "Degraded",
  "timestamp": "2025-12-26T12:35:05Z",
  "details": {
    "azureOpenAI": "Healthy",
    "azureSearch": "Degraded",
    "webSearch": "Healthy"
  }
}
```

---

### GET /health (Platform Health)

Check overall platform health (does not require authentication).

**Request**:
```http
GET /health HTTP/1.1
Host: api.motorcyclerag.com
```

**Response**:
```http
HTTP/1.1 200 OK
Content-Type: text/plain

Healthy
```

---

## Mobile App Client Implementation

### Interface Definition

```csharp
public interface IApiClient
{
    /// <summary>
    /// Send a motorcycle query and receive a conversational answer with sources.
    /// </summary>
    /// <param name="request">Query request with question and optional context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Query response with answer and sources</returns>
    /// <exception cref="ApiException">Thrown for 4xx/5xx responses</exception>
    /// <exception cref="HttpRequestException">Thrown for network errors</exception>
    Task<QueryResponse> SendQueryAsync(QueryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the API is available and healthy.
    /// </summary>
    /// <returns>True if healthy, false otherwise</returns>
    Task<bool> IsHealthyAsync();

    /// <summary>
    /// Get the current user's profile including plan and quota.
    /// </summary>
    /// <returns>User profile with plan and usage information</returns>
    Task<UserProfile> GetUserProfileAsync();
}
```

### Implementation Pattern

```csharp
public class MotorcycleRagApiClient : IApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IAuthenticationService _authService;
    private readonly ILogger<MotorcycleRagApiClient> _logger;

    public MotorcycleRagApiClient(
        HttpClient httpClient,
        IAuthenticationService authService,
        ILogger<MotorcycleRagApiClient> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _logger = logger;
    }

    public async Task<QueryResponse> SendQueryAsync(
        QueryRequest request,
        CancellationToken cancellationToken = default)
    {
        // Add authentication header
        var token = await _authService.GetAccessTokenSilentlyAsync();
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        // Send request
        var response = await _httpClient.PostAsJsonAsync(
            "/api/motorcycles/query",
            request,
            cancellationToken);

        // Handle response
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<QueryResponse>(cancellationToken);
    }

    private async Task HandleErrorResponseAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        switch ((int)response.StatusCode)
        {
            case 401:
                throw new AuthenticationException("Token expired or invalid. Please re-authenticate.");

            case 429:
                var resetAt = problem.Extensions?["resetAt"]?.ToString();
                throw new RateLimitException(
                    $"Daily request limit exceeded. Resets at {resetAt}",
                    DateTime.Parse(resetAt ?? DateTime.UtcNow.AddDays(1).ToString("O")));

            case 400:
                var errors = problem.Errors ?? new Dictionary<string, string[]>();
                throw new ValidationException("Request validation failed", errors);

            case >= 500:
                var correlationId = problem.Extensions?["correlationId"]?.ToString();
                _logger.LogError("Server error. CorrelationId: {CorrelationId}", correlationId);
                throw new ApiException("Server error occurred. Please try again later.");

            default:
                throw new ApiException($"API returned {response.StatusCode}: {problem.Detail}");
        }
    }
}
```

### Error Handling

**Exception Hierarchy**:
```csharp
public class ApiException : Exception
{
    public int? StatusCode { get; set; }
    public string CorrelationId { get; set; }
}

public class AuthenticationException : ApiException { }

public class RateLimitException : ApiException
{
    public DateTime ResetAt { get; set; }
}

public class ValidationException : ApiException
{
    public Dictionary<string, string[]> Errors { get; set; }
}
```

**ViewModel Error Handling**:
```csharp
try
{
    var response = await _apiClient.SendQueryAsync(request);
    // Handle success
}
catch (AuthenticationException)
{
    // Token expired, trigger re-authentication
    await _authService.SignInAsync();
}
catch (RateLimitException ex)
{
    // Show rate limit message with reset time
    ErrorMessage = $"Daily limit reached. Resets at {ex.ResetAt:t}";
}
catch (ValidationException ex)
{
    // Show validation errors
    ErrorMessage = string.Join(", ", ex.Errors.SelectMany(e => e.Value));
}
catch (ApiException ex)
{
    // Generic API error
    ErrorMessage = "Unable to process your question. Please try again.";
    _logger.LogError(ex, "API error occurred");
}
catch (HttpRequestException)
{
    // Network error
    ErrorMessage = "Unable to connect. Check your internet connection.";
}
```

---

## Configuration

**appsettings.json** (or platform-specific configuration):
```json
{
  "Api": {
    "BaseUrl": "https://api.motorcyclerag.com",
    "Timeout": "00:00:30",
    "RetryAttempts": 3,
    "RetryDelaySeconds": [1, 2, 4]
  },
  "Authentication": {
    "ClientId": "your-client-id",
    "Authority": "https://yourtenant.b2clogin.com/tfp/yourtenant.onmicrosoft.com/B2C_1_SignUpSignIn",
    "RedirectUri": "msauth://com.motorcyclerag.mobile",
    "Scopes": ["https://yourtenant.onmicrosoft.com/api/user_impersonation"]
  }
}
```

---

## Testing

### Mock Responses (for unit tests)

```csharp
public class MockApiClient : IApiClient
{
    public Task<QueryResponse> SendQueryAsync(QueryRequest request, CancellationToken ct)
    {
        return Task.FromResult(new QueryResponse
        {
            Response = "Mock answer for: " + request.Query,
            Sources = new List<SearchResult>
            {
                new SearchResult
                {
                    Id = "mock-1",
                    Content = "Mock content",
                    RelevanceScore = 0.95m,
                    Source = new SourceInfo
                    {
                        AgentType = "Mock",
                        SourceName = "Mock Source",
                        SourceUrl = "https://example.com"
                    }
                }
            },
            QueryId = "mock-query-id",
            GeneratedAt = DateTime.UtcNow
        });
    }
}
```

### Integration Tests

Test against actual MotorcycleRAG.API test endpoint with test authentication credentials.

---

## Summary

**Endpoints Used**:
- `POST /api/motorcycles/query` - Primary query endpoint (with user memory in context)
- `GET /api/motorcycles/health` - API health check
- `GET /health` - Platform health check

**Authentication**: Bearer token via Microsoft Entra External ID / B2C

**Error Handling**: RFC 7807 ProblemDetails with custom extensions

**Mobile-Specific Features**:
- User memory included in `context.userMemory` field
- Correlation ID for request tracing
- Platform and version in User-Agent header

This contract ensures type-safe integration between the mobile app and the existing backend API while supporting the user memory personalization feature.
