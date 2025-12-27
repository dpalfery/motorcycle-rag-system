using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MotorcycleRAG.MobileApp.Services;
using System.Threading.Tasks;

namespace MotorcycleRAG.MobileApp.ViewModels;

public partial class AuthenticationViewModel : ObservableObject
{
    private readonly IAuthenticationService _authenticationService;

    private bool _isAuthenticated;
    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set => SetProperty(ref _isAuthenticated, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public AuthenticationViewModel(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            var result = await _authenticationService.SignInAsync();
            IsAuthenticated = result;
            if (IsAuthenticated)
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.GoToAsync("//MainPage");
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            await _authenticationService.SignOutAsync();
            IsAuthenticated = false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
