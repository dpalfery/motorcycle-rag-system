using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Application.Agents.Orchestration;
using MotorcycleRAG.Contracts.Models.DTOs;
using Xunit;

namespace MotorcycleRAG.UnitTests.Agents;

public class FoundryToolDispatcherTests
{
    private readonly Mock<ILogger<FoundryToolDispatcher>> _mockLogger = new();

    private FoundryToolDispatcher CreateDispatcher() => new(_mockLogger.Object);

    [Fact]
    public async Task DispatchAsync_RegisteredTool_InvokesHandler()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var capturedCall = (AgentToolCall?)null;
        dispatcher.RegisterHandler("my_tool", (call, _) =>
        {
            capturedCall = call;
            return Task.FromResult(new AgentToolOutput(call.CallId, "ok"));
        });

        var toolCall = new AgentToolCall("call-1", "my_tool", "{}");

        // Act
        var outputs = await dispatcher.DispatchAsync([toolCall], CancellationToken.None);

        // Assert
        Assert.Single(outputs);
        Assert.Equal("call-1", outputs[0].CallId);
        Assert.Equal("ok", outputs[0].Output);
        Assert.NotNull(capturedCall);
        Assert.Equal("call-1", capturedCall!.CallId);
    }

    [Fact]
    public async Task DispatchAsync_UnregisteredTool_ReturnsErrorOutput()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var toolCall = new AgentToolCall("call-2", "unknown_tool", "{}");

        // Act
        var outputs = await dispatcher.DispatchAsync([toolCall], CancellationToken.None);

        // Assert
        Assert.Single(outputs);
        Assert.Equal("call-2", outputs[0].CallId);
        Assert.Contains("unknown_tool", outputs[0].Output);
        Assert.Contains("not registered", outputs[0].Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DispatchAsync_HandlerThrows_ReturnsErrorOutputWithoutPropagating()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        dispatcher.RegisterHandler("failing_tool", (_, _) =>
            throw new InvalidOperationException("Simulated failure"));

        var toolCall = new AgentToolCall("call-3", "failing_tool", "{}");

        // Act — must NOT throw
        var outputs = await dispatcher.DispatchAsync([toolCall], CancellationToken.None);

        // Assert
        Assert.Single(outputs);
        Assert.Equal("call-3", outputs[0].CallId);
        Assert.Contains("error", outputs[0].Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DispatchAsync_MultipleToolCalls_DispatchesAll()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        dispatcher.RegisterHandler("tool_a", (call, _) =>
            Task.FromResult(new AgentToolOutput(call.CallId, "result_a")));
        dispatcher.RegisterHandler("tool_b", (call, _) =>
            Task.FromResult(new AgentToolOutput(call.CallId, "result_b")));

        var calls = new[]
        {
            new AgentToolCall("id-1", "tool_a", "{}"),
            new AgentToolCall("id-2", "tool_b", "{}")
        };

        // Act
        var outputs = await dispatcher.DispatchAsync(calls, CancellationToken.None);

        // Assert
        Assert.Equal(2, outputs.Count);
        Assert.Contains(outputs, o => o.CallId == "id-1" && o.Output == "result_a");
        Assert.Contains(outputs, o => o.CallId == "id-2" && o.Output == "result_b");
    }

    [Fact]
    public async Task DispatchAsync_EmptyList_ReturnsEmptyOutputs()
    {
        var dispatcher = CreateDispatcher();
        var outputs = await dispatcher.DispatchAsync([], CancellationToken.None);
        Assert.Empty(outputs);
    }
}
