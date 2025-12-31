using Xunit;
using Moq;
using FluentAssertions;
using MotorcycleRAG.MobileApp.ViewModels;
using MotorcycleRAG.MobileApp.Services;
using System.Threading.Tasks;

namespace MotorcycleRAG.MobileApp.Tests.ViewModels
{
    public class AuthenticationViewModelTests
    {
        private readonly Mock<IAuthenticationService> _mockAuthService;
        private readonly AuthenticationViewModel _viewModel;

        public AuthenticationViewModelTests()
        {
            _mockAuthService = new Mock<IAuthenticationService>();
            _viewModel = new AuthenticationViewModel(_mockAuthService.Object);
        }

        [Fact]
        public async Task SignInCommand_ShouldCallSignInAsync_OnAuthService()
        {
            // Arrange
            _mockAuthService.Setup(x => x.SignInAsync())
                .ReturnsAsync(true);

            // Act
            await _viewModel.SignInCommand.ExecuteAsync(null);

            // Assert
            _mockAuthService.Verify(x => x.SignInAsync(), Times.Once);
            _viewModel.IsAuthenticated.Should().BeTrue();
        }

        [Fact]
        public async Task SignOutCommand_ShouldCallSignOutAsync_OnAuthService()
        {
            // Arrange
            _mockAuthService.Setup(x => x.SignOutAsync()).Returns(Task.CompletedTask);

            // Act
            await _viewModel.SignOutCommand.ExecuteAsync(null);

            // Assert
            _mockAuthService.Verify(x => x.SignOutAsync(), Times.Once);
            _viewModel.IsAuthenticated.Should().BeFalse();
        }
    }
}
