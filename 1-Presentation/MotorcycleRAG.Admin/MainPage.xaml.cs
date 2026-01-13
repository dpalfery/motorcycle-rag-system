namespace MotorcycleRAG.Admin;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI framework")]
internal partial class MainPage : ContentPage
{
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Navigate to Dashboard page
        await Shell.Current.GoToAsync("///Dashboard");
    }
}


