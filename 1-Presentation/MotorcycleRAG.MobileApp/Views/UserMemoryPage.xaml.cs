using MotorcycleRAG.MobileApp.ViewModels;

namespace MotorcycleRAG.MobileApp.Views
{
    public partial class UserMemoryPage : ContentPage
    {
        private readonly UserMemoryViewModel _viewModel;

        public UserMemoryPage(UserMemoryViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = _viewModel = viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _viewModel.LoadMemoriesCommand.ExecuteAsync(null);
        }
    }
}
