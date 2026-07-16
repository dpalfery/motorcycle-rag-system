using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.UnitTests.Presentation.API.Controllers;

public sealed class McpAdminControllerTests
{
    [Fact]
    public void Constructor_WhenConfigServiceIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new McpAdminController(
            null!,
            NullLogger<McpAdminController>.Instance,
            Mock.Of<ICurrentUserService>());

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("configService");
    }

    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new McpAdminController(
            Mock.Of<IToolConfigurationService>(),
            null!,
            Mock.Of<ICurrentUserService>());

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("logger");
    }

    [Fact]
    public void Constructor_WhenCurrentUserServiceIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new McpAdminController(
            Mock.Of<IToolConfigurationService>(),
            NullLogger<McpAdminController>.Instance,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("currentUserService");
    }

    [Fact]
    public async Task GetAllToolsAsync_WhenServiceReturnsConfigurations_ReturnsMappedDtos()
    {
        // Arrange
        var tools = new[]
        {
            CreateTool(toolId: "tool-1", name: "Tool One", isEnabled: true),
            CreateTool(toolId: "tool-2", name: "Tool Two", isEnabled: false)
        };
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetAllToolsAsync()).ReturnsAsync(tools);
        var sut = CreateController(configService);

        // Act
        var result = await sut.GetAllToolsAsync();

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeOfType<McpToolConfigurationDto[]>().Subject;
        dtos.Should().HaveCount(2);
        dtos[0].Should().BeEquivalentTo(new
        {
            tools[0].Id,
            tools[0].ToolId,
            tools[0].Name,
            tools[0].Description,
            ServerUrl = tools[0].ServerUrl.ToString(),
            tools[0].IsEnabled,
            tools[0].ToolType,
            tools[0].Version,
            tools[0].IsSystemTool,
            tools[0].Priority,
            tools[0].TimeoutMs,
            tools[0].RetryOnFailure,
            tools[0].MaxRetries,
            tools[0].DisabledReason,
            tools[0].LastConnectionStatus,
            tools[0].LastTestedAt,
            tools[0].ConfigurationJson,
            tools[0].CreatedAt,
            tools[0].UpdatedAt
        });
        dtos[1].ServerUrl.Should().Be("https://mcp.example.com/");
    }

    [Fact]
    public async Task GetAllToolsAsync_WhenServiceThrows_ReturnsInternalServerError()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetAllToolsAsync()).ThrowsAsync(new ApplicationException("boom"));
        var sut = CreateController(configService);

        // Act
        var result = await sut.GetAllToolsAsync();

        // Assert
        var error = result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        error.Value.Should().BeEquivalentTo(new { error = "An error occurred retrieving tool configurations" });
    }

    [Fact]
    public async Task GetToolAsync_WhenToolIdIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateController();

        // Act
        var result = await sut.GetToolAsync(string.Empty);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Tool ID must not be empty" });
    }

    [Fact]
    public async Task GetToolAsync_WhenToolDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetToolAsync("missing-tool")).ReturnsAsync((McpToolConfiguration?)null);
        var sut = CreateController(configService);

        // Act
        var result = await sut.GetToolAsync("missing-tool");

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.Value.Should().BeEquivalentTo(new { error = "Tool not found" });
    }

    [Fact]
    public async Task GetToolAsync_WhenToolExists_ReturnsMappedDto()
    {
        // Arrange
        var tool = CreateTool(toolId: "tool-1", name: "Search Tool");
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetToolAsync("tool-1")).ReturnsAsync(tool);
        var sut = CreateController(configService);

        // Act
        var result = await sut.GetToolAsync("tool-1");

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<McpToolConfigurationDto>().Subject;
        dto.ToolId.Should().Be("tool-1");
        dto.Name.Should().Be("Search Tool");
        dto.ServerUrl.Should().Be("https://mcp.example.com/");
        dto.ConfigurationJson.Should().Be(tool.ConfigurationJson);
    }

    [Fact]
    public async Task GetToolAsync_WhenServiceThrows_ReturnsInternalServerError()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetToolAsync("tool-1")).ThrowsAsync(new ApplicationException("boom"));
        var sut = CreateController(configService);

        // Act
        var result = await sut.GetToolAsync("tool-1");

        // Assert
        var error = result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        error.Value.Should().BeEquivalentTo(new { error = "An error occurred" });
    }

    [Fact]
    public async Task GetEnabledToolsAsync_WhenServiceReturnsEnabledTools_ReturnsMappedDtos()
    {
        // Arrange
        var tools = new[]
        {
            CreateTool(toolId: "tool-1", isEnabled: true),
            CreateTool(toolId: "tool-2", isEnabled: true)
        };
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetEnabledToolsAsync()).ReturnsAsync(tools);
        var sut = CreateController(configService);

        // Act
        var result = await sut.GetEnabledToolsAsync();

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeOfType<McpToolConfigurationDto[]>().Subject;
        dtos.Should().HaveCount(2);
        dtos.Should().OnlyContain(dto => dto.IsEnabled);
    }

    [Fact]
    public async Task GetEnabledToolsAsync_WhenServiceThrows_ReturnsInternalServerError()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetEnabledToolsAsync()).ThrowsAsync(new ApplicationException("boom"));
        var sut = CreateController(configService);

        // Act
        var result = await sut.GetEnabledToolsAsync();

        // Assert
        var error = result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        error.Value.Should().BeEquivalentTo(new { error = "An error occurred" });
    }

    [Fact]
    public async Task CreateToolAsync_WhenRequestIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateController();

        // Act
        Func<Task> act = () => sut.CreateToolAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("request");
    }

    [Fact]
    public async Task CreateToolAsync_WhenModelStateIsInvalid_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateController();
        sut.ModelState.AddModelError("ToolId", "Tool ID is required");

        // Act
        var result = await sut.CreateToolAsync(CreateValidCreateRequest());

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeOfType<SerializableError>();
    }

    [Fact]
    public async Task CreateToolAsync_WhenToolInputValidationFails_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateController();
        var request = CreateValidCreateRequest();
        request.ToolId = string.Empty;

        // Act
        var result = await sut.CreateToolAsync(request);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new
        {
            errors = SingleErrorArray
        });
    }

    [Fact]
    public async Task CreateToolAsync_WhenConfigurationJsonIsInvalid_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateController();
        var request = CreateValidCreateRequest();
        request.ConfigurationJson = "{ invalid";

        // Act
        var result = await sut.CreateToolAsync(request);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Invalid JSON in ConfigurationJson" });
    }

    [Fact]
    public async Task CreateToolAsync_WhenConfigurationJsonExceedsLimit_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateController();
        var request = CreateValidCreateRequest();
        request.ConfigurationJson = CreateOversizedJson();

        // Act
        var result = await sut.CreateToolAsync(request);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Configuration JSON exceeds 10KB limit" });
    }

    [Fact]
    public async Task CreateToolAsync_WhenRequestIsValid_ReturnsCreatedAtAction()
    {
        // Arrange
        var request = CreateValidCreateRequest();
        var savedTool = CreateTool(
            toolId: request.ToolId,
            name: request.Name,
            isEnabled: request.IsEnabled!.Value,
            description: request.Description,
            toolType: request.ToolType,
            version: request.Version,
            priority: request.Priority!.Value,
            timeoutMs: request.TimeoutMs,
            retryOnFailure: request.RetryOnFailure!.Value,
            maxRetries: request.MaxRetries!.Value,
            configurationJson: request.ConfigurationJson);

        var configService = new Mock<IToolConfigurationService>();
        configService
            .Setup(service => service.CreateToolAsync(
                It.Is<McpToolConfiguration>(tool =>
                    tool.ToolId == request.ToolId &&
                    tool.Name == request.Name &&
                    tool.Description == request.Description &&
                    tool.ServerUrl == new Uri(request.ServerUrl) &&
                    tool.ToolType == request.ToolType &&
                    tool.Version == request.Version &&
                    tool.IsEnabled == request.IsEnabled &&
                    tool.Priority == request.Priority &&
                    tool.TimeoutMs == request.TimeoutMs &&
                    tool.RetryOnFailure == request.RetryOnFailure &&
                    tool.MaxRetries == request.MaxRetries &&
                    tool.ConfigurationJson == request.ConfigurationJson),
                "admin-user"))
            .ReturnsAsync(savedTool);
        var sut = CreateController(configService);

        // Act
        var result = await sut.CreateToolAsync(request);

        // Assert
        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(McpAdminController.GetToolAsync));
        created.RouteValues.Should().NotBeNull();
        created.RouteValues!["toolId"].Should().Be(request.ToolId);
        var dto = created.Value.Should().BeOfType<McpToolConfigurationDto>().Subject;
        dto.ToolId.Should().Be(request.ToolId);
        dto.Name.Should().Be(request.Name);
        dto.ServerUrl.Should().Be("https://mcp.example.com/");
        configService.VerifyAll();
    }

    [Fact]
    public async Task CreateToolAsync_WhenServiceThrowsInvalidOperationException_ReturnsConflict()
    {
        // Arrange
        var request = CreateValidCreateRequest();
        var configService = new Mock<IToolConfigurationService>();
        configService
            .Setup(service => service.CreateToolAsync(It.IsAny<McpToolConfiguration>(), "admin-user"))
            .ThrowsAsync(new InvalidOperationException("duplicate"));
        var sut = CreateController(configService);

        // Act
        var result = await sut.CreateToolAsync(request);

        // Assert
        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeEquivalentTo(new { error = "Tool already exists" });
    }

    [Fact]
    public async Task CreateToolAsync_WhenServiceThrowsUnexpectedException_ReturnsInternalServerError()
    {
        // Arrange
        var request = CreateValidCreateRequest();
        var configService = new Mock<IToolConfigurationService>();
        configService
            .Setup(service => service.CreateToolAsync(It.IsAny<McpToolConfiguration>(), "admin-user"))
            .ThrowsAsync(new ApplicationException("boom"));
        var sut = CreateController(configService);

        // Act
        var result = await sut.CreateToolAsync(request);

        // Assert
        var error = result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        error.Value.Should().BeEquivalentTo(new { error = "An error occurred creating the tool" });
    }

    [Fact]
    public async Task UpdateToolAsync_WhenToolIdIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateController();

        // Act
        var result = await sut.UpdateToolAsync(string.Empty, new UpdateMcpToolRequest());

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Tool ID must not be empty" });
    }

    [Fact]
    public async Task UpdateToolAsync_WhenToolIdExceedsLimit_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateController();
        var toolId = new string('t', 256);

        // Act
        var result = await sut.UpdateToolAsync(toolId, new UpdateMcpToolRequest());

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Tool ID exceeds maximum length of 255 characters" });
    }

    [Fact]
    public async Task UpdateToolAsync_WhenRequestIsNull_ThrowsArgumentNullException()
    {
        var sut = CreateController();

        await sut.Invoking(controller => controller.UpdateToolAsync("tool-1", null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateToolAsync_WhenModelStateIsInvalid_ReturnsBadRequest()
    {
        var sut = CreateController();
        sut.ModelState.AddModelError("Name", "Required");

        (await sut.UpdateToolAsync("tool-1", new UpdateMcpToolRequest())).Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateToolAsync_WhenConfigurationJsonIsInvalid_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateController();
        var request = new UpdateMcpToolRequest
        {
            ConfigurationJson = "{ invalid"
        };

        // Act
        var result = await sut.UpdateToolAsync("tool-1", request);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Invalid JSON in ConfigurationJson" });
    }

    [Fact]
    public async Task UpdateToolAsync_WhenConfigurationJsonExceedsLimit_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateController();
        var request = new UpdateMcpToolRequest
        {
            ConfigurationJson = CreateOversizedJson()
        };

        // Act
        var result = await sut.UpdateToolAsync("tool-1", request);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Configuration JSON exceeds 10KB limit" });
    }

    [Fact]
    public async Task UpdateToolAsync_WhenToolDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetToolAsync("missing-tool")).ReturnsAsync((McpToolConfiguration?)null);
        var sut = CreateController(configService);

        // Act
        var result = await sut.UpdateToolAsync("missing-tool", new UpdateMcpToolRequest());

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.Value.Should().BeEquivalentTo(new { error = "Tool not found" });
    }

    [Theory]
    [InlineData("Name", 256, "Name exceeds maximum length of 255 characters")]
    [InlineData("Description", 1001, "Description exceeds maximum length of 1000 characters")]
    [InlineData("ServerUrl", 501, "Server URL exceeds maximum length of 500 characters")]
    public async Task UpdateToolAsync_WhenFieldExceedsLimit_ReturnsBadRequest(string fieldName, int length, string expectedError)
    {
        // Arrange
        var existingTool = CreateTool();
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetToolAsync("tool-1")).ReturnsAsync(existingTool);
        var sut = CreateController(configService);
        var request = new UpdateMcpToolRequest();

        if (fieldName == "Name")
        {
            request.Name = new string('n', length);
        }
        else if (fieldName == "Description")
        {
            request.Description = new string('d', length);
        }
        else
        {
            request.ServerUrl = $"https://{new string('a', length - "https://".Length - ".com".Length)}.com";
        }

        // Act
        var result = await sut.UpdateToolAsync("tool-1", request);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = expectedError });
        configService.Verify(service => service.UpdateToolAsync(
            It.IsAny<string>(),
            It.IsAny<McpToolConfiguration>(),
            It.IsAny<string?>(),
            It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task UpdateToolAsync_WhenPartialUpdateIsValid_ReturnsUpdatedDto()
    {
        // Arrange
        var existingTool = CreateTool(
            toolId: "tool-1",
            name: "Original Name",
            description: "Original description",
            priority: 2,
            configurationJson: """{"old":true}""");

        var request = new UpdateMcpToolRequest
        {
            Name = "Updated Name",
            Priority = 10,
            RetryOnFailure = false,
            ConfigurationJson = """{"new":true}""",
            ChangeReason = "Tune config"
        };

        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetToolAsync("tool-1")).ReturnsAsync(existingTool);
        configService
            .Setup(service => service.UpdateToolAsync(
                "tool-1",
                It.Is<McpToolConfiguration>(tool =>
                    tool.ToolId == "tool-1" &&
                    tool.Name == "Updated Name" &&
                    tool.Description == "Original description" &&
                    tool.ServerUrl == new Uri("https://mcp.example.com") &&
                    tool.Priority == 10 &&
                    tool.RetryOnFailure == false &&
                    tool.ConfigurationJson == """{"new":true}"""),
                "Tune config",
                "admin-user"))
            .ReturnsAsync((string _, McpToolConfiguration tool, string? _, string? _) => tool);
        var sut = CreateController(configService);

        // Act
        var result = await sut.UpdateToolAsync("tool-1", request);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<McpToolConfigurationDto>().Subject;
        dto.ToolId.Should().Be("tool-1");
        dto.Name.Should().Be("Updated Name");
        dto.Description.Should().Be("Original description");
        dto.Priority.Should().Be(10);
        dto.RetryOnFailure.Should().BeFalse();
        dto.ConfigurationJson.Should().Be("""{"new":true}""");
        configService.VerifyAll();
    }

    [Fact]
    public async Task UpdateToolAsync_WhenServiceThrowsInvalidOperationException_ReturnsBadRequest()
    {
        // Arrange
        var existingTool = CreateTool();
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetToolAsync("tool-1")).ReturnsAsync(existingTool);
        // Controller builds a merged snapshot; match any config rather than the original instance.
        configService
            .Setup(service => service.UpdateToolAsync(
                "tool-1",
                It.IsAny<McpToolConfiguration>(),
                null,
                "admin-user"))
            .ThrowsAsync(new InvalidOperationException("invalid"));
        var sut = CreateController(configService);

        // Act
        var result = await sut.UpdateToolAsync("tool-1", new UpdateMcpToolRequest());

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Tool configuration is invalid" });
    }

    [Fact]
    public async Task UpdateToolAsync_WhenServiceThrowsUnexpectedException_ReturnsInternalServerError()
    {
        // Arrange
        var existingTool = CreateTool();
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetToolAsync("tool-1")).ReturnsAsync(existingTool);
        configService
            .Setup(service => service.UpdateToolAsync(
                "tool-1",
                It.IsAny<McpToolConfiguration>(),
                null,
                "admin-user"))
            .ThrowsAsync(new ApplicationException("boom"));
        var sut = CreateController(configService);

        // Act
        var result = await sut.UpdateToolAsync("tool-1", new UpdateMcpToolRequest());

        // Assert
        var error = result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        error.Value.Should().BeEquivalentTo(new { error = "An error occurred updating the tool" });
    }

    [Fact]
    public async Task EnableToolAsync_WhenToolIdIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateController();

        // Act
        var result = await sut.EnableToolAsync(string.Empty);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Tool ID must not be empty" });
    }

    [Fact]
    public async Task EnableToolAsync_WhenToolExists_ReturnsOk()
    {
        // Arrange
        var enabledTool = CreateTool(isEnabled: true);
        var configService = new Mock<IToolConfigurationService>();
        configService
            .Setup(service => service.EnableToolAsync("tool-1", "admin-user"))
            .ReturnsAsync(enabledTool);
        var sut = CreateController(configService);

        // Act
        var result = await sut.EnableToolAsync("tool-1");

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<McpToolConfigurationDto>()
            .Which.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task EnableToolAsync_WhenToolDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService
            .Setup(service => service.EnableToolAsync("tool-1", "admin-user"))
            .ThrowsAsync(new InvalidOperationException("Tool not found"));
        var sut = CreateController(configService);

        // Act
        var result = await sut.EnableToolAsync("tool-1");

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.Value.Should().BeEquivalentTo(new { error = "Tool not found" });
    }

    [Fact]
    public async Task EnableToolAsync_WhenServiceThrowsUnexpectedException_ReturnsInternalServerError()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService
            .Setup(service => service.EnableToolAsync("tool-1", "admin-user"))
            .ThrowsAsync(new ApplicationException("boom"));
        var sut = CreateController(configService);

        // Act
        var result = await sut.EnableToolAsync("tool-1");

        // Assert
        var error = result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        error.Value.Should().BeEquivalentTo(new { error = "An error occurred" });
    }

    [Fact]
    public async Task DisableToolAsync_WhenToolIdIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateController();

        // Act
        var result = await sut.DisableToolAsync(string.Empty, new DisableMcpToolRequest());

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Tool ID must not be empty" });
    }

    [Fact]
    public async Task DisableToolAsync_WhenRequestIsNull_UsesDefaultReason()
    {
        // Arrange
        var disabledTool = CreateTool(isEnabled: false, disabledReason: "Disabled by admin");

        var configService = new Mock<IToolConfigurationService>();
        configService
            .Setup(service => service.DisableToolAsync("tool-1", "Disabled by admin", "admin-user"))
            .ReturnsAsync(disabledTool);
        var sut = CreateController(configService);

        // Act
        var result = await sut.DisableToolAsync("tool-1", null!);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<McpToolConfigurationDto>()
            .Which.DisabledReason.Should().Be("Disabled by admin");
    }

    [Fact]
    public async Task DisableToolAsync_WhenRequestIsValid_ReturnsOk()
    {
        // Arrange
        var disabledTool = CreateTool(isEnabled: false, disabledReason: "Maintenance");

        var configService = new Mock<IToolConfigurationService>();
        configService
            .Setup(service => service.DisableToolAsync("tool-1", "Maintenance", "admin-user"))
            .ReturnsAsync(disabledTool);
        var sut = CreateController(configService);

        // Act
        var result = await sut.DisableToolAsync("tool-1", new DisableMcpToolRequest { Reason = "Maintenance" });

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<McpToolConfigurationDto>().Subject;
        dto.IsEnabled.Should().BeFalse();
        dto.DisabledReason.Should().Be("Maintenance");
    }

    [Fact]
    public async Task DisableToolAsync_WhenToolDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService
            .Setup(service => service.DisableToolAsync("tool-1", "Maintenance", "admin-user"))
            .ThrowsAsync(new InvalidOperationException("Tool not found"));
        var sut = CreateController(configService);

        // Act
        var result = await sut.DisableToolAsync("tool-1", new DisableMcpToolRequest { Reason = "Maintenance" });

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.Value.Should().BeEquivalentTo(new { error = "Tool not found" });
    }

    [Fact]
    public async Task DisableToolAsync_WhenServiceThrowsArgumentException_ReturnsBadRequest()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService
            .Setup(service => service.DisableToolAsync("tool-1", "Maintenance", "admin-user"))
            .ThrowsAsync(new ArgumentException("Reason is invalid"));
        var sut = CreateController(configService);

        // Act
        var result = await sut.DisableToolAsync("tool-1", new DisableMcpToolRequest { Reason = "Maintenance" });

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Reason is invalid" });
    }

    [Fact]
    public async Task DisableToolAsync_WhenServiceThrowsUnexpectedException_ReturnsInternalServerError()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService
            .Setup(service => service.DisableToolAsync("tool-1", "Maintenance", "admin-user"))
            .ThrowsAsync(new ApplicationException("boom"));
        var sut = CreateController(configService);

        // Act
        var result = await sut.DisableToolAsync("tool-1", new DisableMcpToolRequest { Reason = "Maintenance" });

        // Assert
        var error = result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        error.Value.Should().BeEquivalentTo(new { error = "An error occurred" });
    }

    [Fact]
    public async Task DeleteToolAsync_WhenToolIdIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateController();

        // Act
        var result = await sut.DeleteToolAsync(string.Empty);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Tool ID must not be empty" });
    }

    [Fact]
    public async Task DeleteToolAsync_WhenToolDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.DeleteToolAsync("tool-1", "admin-user")).ReturnsAsync(false);
        var sut = CreateController(configService);

        // Act
        var result = await sut.DeleteToolAsync("tool-1");

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.Value.Should().BeEquivalentTo(new { error = "Tool not found" });
    }

    [Fact]
    public async Task DeleteToolAsync_WhenToolIsDeleted_ReturnsNoContent()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.DeleteToolAsync("tool-1", "admin-user")).ReturnsAsync(true);
        var sut = CreateController(configService);

        // Act
        var result = await sut.DeleteToolAsync("tool-1");

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteToolAsync_WhenServiceThrowsUnexpectedException_ReturnsInternalServerError()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.DeleteToolAsync("tool-1", "admin-user")).ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateController(configService);

        // Act
        var result = await sut.DeleteToolAsync("tool-1");

        // Assert
        var error = result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        error.Value.Should().BeEquivalentTo(new { error = "An error occurred deleting the tool" });
    }

    [Fact]
    public async Task GetAuditHistoryAsync_WithDefaultLimit_WhenToolIdIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateController();

        // Act
        var result = await sut.GetAuditHistoryAsync(string.Empty);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Tool ID must not be empty" });
    }

    [Fact]
    public async Task GetAuditHistoryAsync_WithDefaultLimit_WhenToolDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetToolAsync("tool-1")).ReturnsAsync((McpToolConfiguration?)null);
        var sut = CreateController(configService);

        // Act
        var result = await sut.GetAuditHistoryAsync("tool-1");

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.Value.Should().BeEquivalentTo(new { error = "Tool not found" });
    }

    [Fact]
    public async Task GetAuditHistoryAsync_WithDefaultLimit_WhenToolExists_ReturnsAuditEntries()
    {
        // Arrange
        var tool = CreateTool();
        var auditEntries = new[]
        {
            new ToolConfigurationAuditEntry
            {
                Id = 1,
                ToolConfigurationId = tool.Id,
                ToolId = tool.ToolId,
                Action = "updated",
                UserId = "admin-user",
                ChangeReason = "Changed priority",
                ChangedAt = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc)
            }
        };

        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetToolAsync("tool-1")).ReturnsAsync(tool);
        configService.Setup(service => service.GetAuditHistoryAsync(tool.Id, 100)).ReturnsAsync(auditEntries);
        var sut = CreateController(configService);

        // Act
        var result = await sut.GetAuditHistoryAsync("tool-1");

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(auditEntries);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task GetAuditHistoryAsync_WithCustomLimit_WhenLimitIsOutOfRange_ReturnsBadRequest(int limit)
    {
        // Arrange
        var sut = CreateController();

        // Act
        var result = await sut.GetAuditHistoryAsync("tool-1", limit);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Limit must be between 1 and 1000" });
    }

    [Fact]
    public async Task GetAuditHistoryAsync_WithCustomLimit_WhenRequestIsValid_ReturnsAuditEntries()
    {
        // Arrange
        var tool = CreateTool();
        var auditEntries = new[]
        {
            new ToolConfigurationAuditEntry
            {
                Id = 2,
                ToolConfigurationId = tool.Id,
                ToolId = tool.ToolId,
                Action = "disabled",
                UserId = "admin-user",
                ChangeReason = "Maintenance",
                ChangedAt = new DateTime(2026, 7, 10, 13, 0, 0, DateTimeKind.Utc)
            }
        };

        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetToolAsync("tool-1")).ReturnsAsync(tool);
        configService.Setup(service => service.GetAuditHistoryAsync(tool.Id, 25)).ReturnsAsync(auditEntries);
        var sut = CreateController(configService);

        // Act
        var result = await sut.GetAuditHistoryAsync("tool-1", 25);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(auditEntries);
    }

    [Fact]
    public async Task GetAuditHistoryAsync_WhenServiceThrows_ReturnsInternalServerError()
    {
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetToolAsync("tool-1")).ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateController(configService);

        var result = await sut.GetAuditHistoryAsync("tool-1");

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task GetAuditSummaryAsync_WhenServiceReturnsSummary_ReturnsOk()
    {
        // Arrange
        var summary = new ToolConfigurationAuditSummary
        {
            TotalEntries = 42,
            UniqueTools = 8,
            UniqueUsers = 3,
            OldestEntry = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            LatestEntry = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc)
        };

        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetAuditSummaryAsync()).ReturnsAsync(summary);
        var sut = CreateController(configService);

        // Act
        var result = await sut.GetAuditSummaryAsync();

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(summary);
    }

    [Fact]
    public async Task GetAuditSummaryAsync_WhenServiceThrows_ReturnsInternalServerError()
    {
        // Arrange
        var configService = new Mock<IToolConfigurationService>();
        configService.Setup(service => service.GetAuditSummaryAsync()).ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateController(configService);

        // Act
        var result = await sut.GetAuditSummaryAsync();

        // Assert
        var error = result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        error.Value.Should().BeEquivalentTo(new { error = "An error occurred" });
    }

    [Theory]
    [InlineData("toolIdTooLong", "Tool ID exceeds maximum length of 255 characters")]
    [InlineData("nameRequired", "Name is required")]
    [InlineData("nameTooLong", "Name exceeds maximum length of 255 characters")]
    [InlineData("serverUrlRequired", "Server URL is required")]
    [InlineData("serverUrlTooLong", "Server URL exceeds maximum length of 500 characters")]
    public async Task CreateToolAsync_WhenManualInputValidationFails_ReturnsBadRequestWithErrors(string scenario, string expectedError)
    {
        // Arrange
        var sut = CreateController();
        var request = CreateValidCreateRequest();

        switch (scenario)
        {
            case "toolIdTooLong":
                request.ToolId = new string('t', 256);
                break;
            case "nameRequired":
                request.Name = " ";
                break;
            case "nameTooLong":
                request.Name = new string('n', 256);
                break;
            case "serverUrlRequired":
                request.ServerUrl = null!;
                break;
            case "serverUrlTooLong":
                request.ServerUrl = "https://x.com/" + new string('a', 500);
                break;
        }

        // Act
        var result = await sut.CreateToolAsync(request);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { errors = new[] { expectedError } });
    }

    [Fact]
    public async Task CreateToolAsync_WhenUserIdIsNull_StillCreatesToolSanitizingForLogging()
    {
        // Exercises the SanitizeUserId null/whitespace branch (returns "[system]").
        var request = CreateValidCreateRequest();
        var savedTool = CreateTool(toolId: request.ToolId, name: request.Name);
        var configService = new Mock<IToolConfigurationService>();
        configService
            .Setup(service => service.CreateToolAsync(It.IsAny<McpToolConfiguration>(), It.IsAny<string?>()))
            .ReturnsAsync(savedTool);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns((string?)null);

        // Construct directly: the CreateController helper force-sets UserId to "admin-user",
        // but this test must exercise the null-UserId sanitization branch.
        var sut = new McpAdminController(
            configService.Object,
            NullLogger<McpAdminController>.Instance,
            currentUser.Object);

        // Act
        var result = await sut.CreateToolAsync(request);

        // Assert
        result.Should().BeOfType<CreatedAtActionResult>();
        configService.Verify(service => service.CreateToolAsync(It.IsAny<McpToolConfiguration>(), null), Times.Once);
    }

    private static McpAdminController CreateController(
        Mock<IToolConfigurationService>? configService = null,
        Mock<ICurrentUserService>? currentUserService = null)
    {
        currentUserService ??= new Mock<ICurrentUserService>();
        currentUserService.SetupGet(service => service.UserId).Returns("admin-user");

        return new McpAdminController(
            configService?.Object ?? Mock.Of<IToolConfigurationService>(),
            NullLogger<McpAdminController>.Instance,
            currentUserService.Object);
    }

    private static CreateMcpToolRequest CreateValidCreateRequest()
    {
        return new CreateMcpToolRequest
        {
            ToolId = "tool-1",
            Name = "Test Tool",
            Description = "Tool description",
            ServerUrl = "https://mcp.example.com",
            ToolType = "search",
            Version = "1.0.0",
            IsEnabled = true,
            Priority = 4,
            TimeoutMs = 1500,
            RetryOnFailure = true,
            MaxRetries = 5,
            ConfigurationJson = """{"mode":"fast"}"""
        };
    }

    private static McpToolConfiguration CreateTool(
        string toolId = "tool-1",
        string name = "Test Tool",
        bool isEnabled = true,
        string? description = "Tool description",
        string? toolType = "search",
        string? version = "1.0.0",
        int priority = 3,
        int? timeoutMs = 1500,
        bool retryOnFailure = true,
        int maxRetries = 5,
        string? disabledReason = null,
        string? configurationJson = """{"mode":"fast"}""")
    {
        return new McpToolConfiguration
        {
            Id = Guid.NewGuid(),
            ToolId = toolId,
            Name = name,
            Description = description,
            ServerUrl = new Uri("https://mcp.example.com"),
            IsEnabled = isEnabled,
            ToolType = toolType ?? "search",
            Version = version,
            IsSystemTool = false,
            Priority = priority,
            TimeoutMs = timeoutMs,
            RetryOnFailure = retryOnFailure,
            MaxRetries = maxRetries,
            DisabledReason = disabledReason ?? (isEnabled ? null : "Disabled by admin"),
            LastConnectionStatus = "Healthy",
            LastTestedAt = new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc),
            ConfigurationJson = configurationJson,
            CreatedAt = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 7, 9, 9, 0, 0, DateTimeKind.Utc)
        };
    }

    private static string CreateOversizedJson()
    {
        return "\"" + new string('a', 10239) + "\"";
    }

    private static readonly string[] SingleErrorArray = ["Tool ID is required"];
}
