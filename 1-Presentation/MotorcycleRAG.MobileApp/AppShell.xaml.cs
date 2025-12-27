using MotorcycleRAG.MobileApp.Services;
using MotorcycleRAG.MobileApp.Views;

namespace MotorcycleRAG.MobileApp;

public partial class AppShell : Shell
{
	private readonly IAuthenticationService _authService;

	public AppShell(IAuthenticationService authService)
	{
		InitializeComponent();
		_authService = authService;
		Routing.RegisterRoute("ChatPage", typeof(ChatPage));
		Routing.RegisterRoute("UserMemoryPage", typeof(UserMemoryPage));
		Routing.RegisterRoute("PdfViewerPage", typeof(PdfViewerPage));
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		var token = await _authService.GetAccessTokenAsync();
		if (!string.IsNullOrEmpty(token))
		{
			await GoToAsync("//ConversationListPage");
		}
	}
}
