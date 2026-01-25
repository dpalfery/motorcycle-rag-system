using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace MotorcycleRAG.MobileApp.Services
{
    public class PdfViewerService : IPdfViewerService
    {
        private readonly IPdfRenderer _renderer;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IStorageService _storageService;

        public PdfViewerService(IPdfRenderer renderer, IHttpClientFactory httpClientFactory, IStorageService storageService)
        {
            _renderer = renderer;
            _httpClientFactory = httpClientFactory;
            _storageService = storageService;
        }

        public async Task<string> DownloadPdfAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) throw new ArgumentNullException(nameof(url));

            string fileName = Path.GetFileName(new Uri(url).LocalPath);
            string localPath = Path.Combine(_storageService.GetCacheDirectory(), fileName);

            if (File.Exists(localPath))
            {
                return localPath;
            }

            var client = _httpClientFactory.CreateClient();
            var data = await client.GetByteArrayAsync(new Uri(url));
            await File.WriteAllBytesAsync(localPath, data);

            return localPath;
        }

        public async Task<Stream?> RenderPageToStreamAsync(string localPath, int pageNumber)
        {
            return await _renderer.RenderPageAsync(localPath, pageNumber);
        }
    }
}
