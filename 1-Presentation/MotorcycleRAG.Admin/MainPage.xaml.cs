namespace MotorcycleRAG.Admin;

internal partial class MainPage : ContentPage
{
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Navigate to Dashboard page
        await Shell.Current.GoToAsync("///Dashboard");
    }
}


