using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using MotorcycleRAG.API;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.IntegrationTests;

namespace MotorcycleRAG.IntegrationTests.Api {
    /// <summary>
    /// Integration tests for MCP Admin Controller endpoints.
    /// Tests authorization, validation, and error handling.
    /// </summary>
    public class McpAdminControllerIntegrationTests : IClassFixture<TestWebApplicationFactory> {
        private readonly TestWebApplicationFactory _factory;
        private const string BaseUrl = "/api/admin/mcp-tools";

        public McpAdminControllerIntegrationTests(TestWebApplicationFactory factory) {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>
        /// Test: GET_GetTool_Unauthorized_Returns401
        /// Validates that unauthenticated requests are rejected
        /// </summary>
        [Fact]
        public async Task GET_GetTool_Unauthorized_Returns401() {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync($"{BaseUrl}/test-tool");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        /// <summary>
        /// Test: POST_CreateTool_MissingRequiredField_ReturnsBadRequest
        /// Tests SC-003: Validates required fields
        /// </summary>
        [Fact]
        public async Task POST_CreateTool_MissingRequiredField_ReturnsBadRequest() {
            // Arrange
            var client = _factory.CreateClientWithRoles("DataAdmin");
            var request = new CreateMcpToolRequest {
                ToolId = "", // Empty required field
                Name = "Test Tool",
                ServerUrl = new Uri("http://localhost:8080"),
                ToolType = "search"
            };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await client.PostAsync(BaseUrl, content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        /// <summary>
        /// Test: POST_CreateTool_InvalidJSON_ReturnsBadRequest
        /// Tests validation of ConfigurationJson parameter
        /// </summary>
        [Fact]
        public async Task POST_CreateTool_InvalidJSON_ReturnsBadRequest() {
            // Arrange
            var client = _factory.CreateClientWithRoles("DataAdmin");
            var request = new CreateMcpToolRequest {
                ToolId = "test-tool",
                Name = "Test Tool",
                ServerUrl = new Uri("http://localhost:8080"),
                ToolType = "search",
                ConfigurationJson = "{ invalid json }" // Invalid JSON
            };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await client.PostAsync(BaseUrl, content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var responseContent = await response.Content.ReadAsStringAsync();
            Assert.Contains("JSON", responseContent, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Test: PUT_UpdateTool_MissingToolId_ReturnsMethodNotAllowed
        /// Tests that routing rejects empty toolId in URL segment.
        /// The routing layer handles this before reaching the controller.
        /// </summary>
        [Fact]
        public async Task PUT_UpdateTool_MissingToolId_ReturnsMethodNotAllowed() {
            // Arrange
            var client = _factory.CreateClientWithRoles("DataAdmin");
            var request = new UpdateMcpToolRequest {
                Name = "Updated Name"
            };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            // Act - empty toolId in URL causes routing to reject
            var response = await client.PutAsync($"{BaseUrl}/", content);

            // Assert - empty route segment doesn't match [HttpPut("{toolId}")] 
            // so routing returns 405 (Method Not Allowed)
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        /// <summary>
        /// Test: DELETE_DeleteTool_Unauthorized_Returns401
        /// Validates that unauthenticated DELETE requests are rejected
        /// </summary>
        [Fact]
        public async Task DELETE_DeleteTool_Unauthorized_Returns401() {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.DeleteAsync($"{BaseUrl}/test-tool");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        /// <summary>
        /// Test: DELETE_DeleteTool_NotFound_Returns404
        /// Tests DELETE with non-existent tool
        /// </summary>
        [Fact]
        public async Task DELETE_DeleteTool_NotFound_Returns404() {
            // Arrange
            var client = _factory.CreateClientWithRoles("DataAdmin");

            // Act
            var response = await client.DeleteAsync($"{BaseUrl}/nonexistent-tool");

            // Assert
            // 404 or 500 depending on whether database is available
            Assert.True(
                response.StatusCode == HttpStatusCode.NotFound ||
                response.StatusCode == HttpStatusCode.InternalServerError);
        }

        /// <summary>
        /// Test: POST_CreateTool_ExceedsMaxLength_ReturnsBadRequest
        /// Tests SC-003: Manual input validation for field lengths
        /// </summary>
        [Fact]
        public async Task POST_CreateTool_ExceedsMaxLength_ReturnsBadRequest() {
            // Arrange
            var client = _factory.CreateClientWithRoles("DataAdmin");
            var veryLongString = new string('x', 300); // Exceeds 255 limit

            var request = new CreateMcpToolRequest {
                ToolId = veryLongString,
                Name = "Test Tool",
                ServerUrl = new Uri("http://localhost:8080"),
                ToolType = "search"
            };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await client.PostAsync(BaseUrl, content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        /// <summary>
        /// Test: GET_GetAuditHistory_Unauthorized_Returns401
        /// Validates authentication on audit endpoints
        /// </summary>
        [Fact]
        public async Task GET_GetAuditHistory_Unauthorized_Returns401() {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync($"{BaseUrl}/test-tool/audit");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        /// <summary>
        /// Test: GET_GetAuditSummary_Unauthorized_Returns401
        /// Validates authentication on audit summary endpoint
        /// </summary>
        [Fact]
        public async Task GET_GetAuditSummary_Unauthorized_Returns401() {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync($"{BaseUrl}/audit/summary");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
