using Microsoft.Extensions.Logging;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Services;

public class UserAdminServiceTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IPlanRepository> _planRepository = new();
    private readonly Mock<ILogger<UserAdminService>> _logger = new();

    [Fact]
    public async Task GetAllUsersAsync_ValidPaging_DelegatesToRepository()
    {
        var expectedUsers = new[]
        {
            new UserDTO { Id = "user-1", Email = "one@example.com", IsEnabled = true },
            new UserDTO { Id = "user-2", Email = "two@example.com", IsEnabled = true }
        };

        _userRepository
            .Setup(repository => repository.GetUsersAsync(2, 25))
            .ReturnsAsync(expectedUsers);

        var service = new UserAdminService(_userRepository.Object, _planRepository.Object, _logger.Object);

        var result = await service.GetAllUsersAsync(2, 25);

        result.Should().BeEquivalentTo(expectedUsers);
        _userRepository.Verify(repository => repository.GetUsersAsync(2, 25), Times.Once);
    }
}
