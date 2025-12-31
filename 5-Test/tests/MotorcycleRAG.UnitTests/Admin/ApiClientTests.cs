using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Domain.DTOs;
using Xunit;

namespace MotorcycleRAG.UnitTests.Admin;

public class ApiClientTests
{
    private HttpClient CreateHttpClient(HttpResponseMessage response, out Mock<HttpMessageHandler> handlerMock)
    {
        handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return new HttpClient(handlerMock.Object) { BaseAddress = new Uri("https://localhost:7000") };
    }

    [Fact]
    public async Task UploadFileAsync_ReturnsResult()
    {
        // Arrange
        var json = "{\"fileId\":\"fid\"}";
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        var httpClient = CreateHttpClient(response, out _);

        var auth = new Mock<IAdminAuthService>();
        auth.Setup(a => a.GetAccessTokenAsync()).ReturnsAsync("token");
        var client = new ApiClient(httpClient, auth.Object, NullLogger<ApiClient>.Instance);

        var path = Path.GetTempFileName();
        File.WriteAllText(path, "data");

        // Act
        var result = await client.UploadFileAsync(path, true);

        // Assert
        Assert.Equal("fid", result.FileId);
    }
}
