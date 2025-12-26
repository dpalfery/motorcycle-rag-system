using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MotorcycleRAG.MobileApp.Models;
using MotorcycleRAG.MobileApp.Services;

namespace MotorcycleRAG.MobileApp.Tests.Mocks;

public class MockApiClient : IApiClient
{
    public QueryResponse? ResponseToReturn { get; set; }
    public Exception? ExceptionToThrow { get; set; }

    public Task<QueryResponse> SendQueryAsync(QueryRequest request, CancellationToken cancellationToken = default)
    {
        if (ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(ResponseToReturn ?? new QueryResponse
        {
            Response = "Mock response",
            Sources = new List<SearchResult>(),
            GeneratedAt = DateTime.UtcNow,
            QueryId = "mock-query-id"
        });
    }

    public Task<bool> IsHealthyAsync()
    {
        return Task.FromResult(true);
    }

    public Task<UserProfile> GetUserProfileAsync()
    {
        return Task.FromResult(new UserProfile { Id = "mock-user" });
    }
}
