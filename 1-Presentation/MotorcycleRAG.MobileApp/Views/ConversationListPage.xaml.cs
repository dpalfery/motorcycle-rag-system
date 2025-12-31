using MotorcycleRAG.MobileApp.ViewModels;

namespace MotorcycleRAG.MobileApp.Views;

public partial class ConversationListPage : ContentPage
{
    public ConversationListPage(ConversationListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ConversationListViewModel vm)
        {
            await vm.LoadConversationsCommand.ExecuteAsync(null);
        }
    }
}
