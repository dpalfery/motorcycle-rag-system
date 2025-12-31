using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Moq.Protected;
using MotorcycleRAG.MobileApp.Services;
using Xunit;

namespace MotorcycleRAG.MobileApp.Tests.Services
{
    public class PdfViewerServiceTests
    {
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
        private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
        private readonly Mock<IPdfRenderer> _mockRenderer;
        private readonly Mock<IStorageService> _mockStorageService;
        private readonly PdfViewerService _service;
        private readonly string _cacheDir;

        public PdfViewerServiceTests()
        {
            _mockRenderer = new Mock<IPdfRenderer>();
            _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            _mockStorageService = new Mock<IStorageService>();
            var client = new HttpClient(_mockHttpMessageHandler.Object);

            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(client);

            _cacheDir = Path.Combine(Path.GetTempPath(), "pdfs_test");
            if (!Directory.Exists(_cacheDir)) Directory.CreateDirectory(_cacheDir);

            _mockStorageService.Setup(s => s.GetCacheDirectory()).Returns(_cacheDir);

            _service = new PdfViewerService(_mockRenderer.Object, _mockHttpClientFactory.Object, _mockStorageService.Object);
        }

        [Fact]
        public async Task DownloadPdfAsync_ShouldDownloadAndCache_WhenNotCached()
        {
            // Arrange
            string url = "http://example.com/manual.pdf";
            var pdfContent = new byte[] { 1, 2, 3, 4 };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    Content = new ByteArrayContent(pdfContent)
                });

            // Act
            string path = await _service.DownloadPdfAsync(url);

            // Assert
            path.Should().NotBeNullOrEmpty();
            File.Exists(path).Should().BeTrue();

            // Cleanup
            if (File.Exists(path)) File.Delete(path);
        }

        [Fact]
        public async Task RenderPageToStreamAsync_ShouldCallRenderer()
        {
            // Arrange
            string path = "test.pdf";
            int page = 1;
            var imageStream = new MemoryStream(new byte[] { 5, 6, 7 });

            _mockRenderer.Setup(r => r.RenderPageAsync(path, page))
                .ReturnsAsync(imageStream);

            // Act
            var result = await _service.RenderPageToStreamAsync(path, page);

            // Assert
            result.Should().NotBeNull();
            result.Length.Should().Be(3);
            _mockRenderer.Verify(r => r.RenderPageAsync(path, page), Times.Once);
        }
    }
}
