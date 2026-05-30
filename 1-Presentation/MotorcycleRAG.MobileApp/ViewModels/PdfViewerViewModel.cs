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

        private ImageSource? _pageImage;
        public ImageSource? PageImage
        {
            get => _pageImage;
            set => SetProperty(ref _pageImage, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public PdfViewerViewModel(IPdfViewerService pdfService, IImageSourceFactory imageSourceFactory)
        {
            _pdfService = pdfService;
            _imageSourceFactory = imageSourceFactory;
        }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            string? url = null;
            int page = 1;

            if (query.TryGetValue("url", out var urlValue))
                url = urlValue as string;

            if (query.TryGetValue("page", out var pageObj))
            {
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
