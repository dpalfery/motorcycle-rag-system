using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using MotorcycleRAG.MobileApp.Services;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MotorcycleRAG.MobileApp.ViewModels
{
    public partial class PdfViewerViewModel : ObservableObject, IQueryAttributable
    {
        private readonly IPdfViewerService _pdfService;
        private readonly IImageSourceFactory _imageSourceFactory;

        [ObservableProperty]
        private ImageSource? _pageImage;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _title = string.Empty;

        public PdfViewerViewModel(IPdfViewerService pdfService, IImageSourceFactory imageSourceFactory)
        {
            _pdfService = pdfService;
            _imageSourceFactory = imageSourceFactory;
        }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            string? url = null;
            int page = 1;

            if (query.ContainsKey("url"))
                url = query["url"] as string;

            if (query.ContainsKey("page"))
            {
                var pageObj = query["page"];
                if (pageObj is int p) page = p;
                else if (pageObj is string s && int.TryParse(s, out int parsed)) page = parsed;
            }

            if (!string.IsNullOrEmpty(url))
            {
                LoadPageCommand.Execute((url, page));
            }
        }

        [RelayCommand]
        private async Task LoadPageAsync((string url, int page) args)
        {
            if (IsBusy) return;
            IsBusy = true;
            PageImage = null;

            try
            {
                string localPath = await _pdfService.DownloadPdfAsync(args.url);
                Stream? stream = await _pdfService.RenderPageToStreamAsync(localPath, args.page);

                if (stream != null)
                {
                    PageImage = _imageSourceFactory.FromStream(() => stream);
                    Title = $"Page {args.page}";
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading PDF page: {ex.Message}");
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlertAsync("Error", "Failed to load PDF page.", "OK");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task CloseAsync()
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("..");
            }
        }
    }
}
