using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using MotorcycleRAG.MobileApp.Services;
using MotorcycleRAG.MobileApp.ViewModels;
using Xunit;

namespace MotorcycleRAG.MobileApp.Tests.ViewModels
{
    public class PdfViewerViewModelTests
    {
        private readonly Mock<IPdfViewerService> _mockPdfService;
        private readonly Mock<IImageSourceFactory> _mockImageFactory;
        private readonly PdfViewerViewModel _viewModel;

        public PdfViewerViewModelTests()
        {
            _mockPdfService = new Mock<IPdfViewerService>();
            _mockImageFactory = new Mock<IImageSourceFactory>();
            _viewModel = new PdfViewerViewModel(_mockPdfService.Object, _mockImageFactory.Object);
        }

        [Fact]
        public async Task LoadPageCommand_ShouldLoadAndRenderPage()
        {
            // Arrange
            string url = "http://example.com/manual.pdf";
            int page = 5;
            string localPath = "/local/path/manual.pdf";
            var stream = new MemoryStream(new byte[] { 1, 2, 3 });

            _mockPdfService.Setup(s => s.DownloadPdfAsync(url))
                .ReturnsAsync(localPath);
            _mockPdfService.Setup(s => s.RenderPageToStreamAsync(localPath, page))
                .ReturnsAsync(stream);

            // We return null because instantiating ImageSource in unit tests might fail due to UI thread requirements
            // We will verify the factory was called instead
            _mockImageFactory.Setup(f => f.FromStream(It.IsAny<System.Func<Stream>>()))
                .Returns((Microsoft.Maui.Controls.ImageSource)null);

            // Act
            await _viewModel.LoadPageCommand.ExecuteAsync((url, page));

            // Assert
            // _viewModel.PageImage.Should().NotBeNull(); // Cannot assert this if we return null
            _mockPdfService.Verify(s => s.DownloadPdfAsync(url), Times.Once);
            _mockPdfService.Verify(s => s.RenderPageToStreamAsync(localPath, page), Times.Once);
            _mockImageFactory.Verify(f => f.FromStream(It.IsAny<System.Func<Stream>>()), Times.Once);
        }

        [Fact]
        public async Task LoadPageCommand_ShouldHandleErrors()
        {
            // Arrange
            _mockPdfService.Setup(s => s.DownloadPdfAsync(It.IsAny<string>()))
                .ThrowsAsync(new System.Exception("Download failed"));

            // Act
            await _viewModel.LoadPageCommand.ExecuteAsync(("http://example.com/fail.pdf", 1));

            // Assert
            _viewModel.PageImage.Should().BeNull();
            // In a real app we might check if an error message property is set or a dialog service is called
        }
    }
}
