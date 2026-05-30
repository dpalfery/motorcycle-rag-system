using System.Text.Json;
using FluentAssertions;
using MotorcycleRAG.Admin.Services.Dtos;

namespace MotorcycleRAG.Admin.Tests.Services;

public class UpdateMcpToolRequestTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Serialize_WithPublicProperties_IncludesPayloadFields()
    {
        var request = new UpdateMcpToolRequest
        {
            IsEnabled = true,
            ChangeReason = "Enabled via admin panel"
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);

        json.Should().Contain("\"isEnabled\":true");
        json.Should().Contain("\"changeReason\":\"Enabled via admin panel\"");
    }
}
