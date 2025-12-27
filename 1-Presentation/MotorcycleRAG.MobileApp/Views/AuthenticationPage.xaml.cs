using MotorcycleRAG.MobileApp.ViewModels;

namespace MotorcycleRAG.MobileApp.Views;

public partial class AuthenticationPage : ContentPage
{
	public AuthenticationPage(AuthenticationViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}
