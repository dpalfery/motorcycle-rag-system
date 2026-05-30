using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using MotorcycleRAG.MobileApp.Services;

namespace MotorcycleRAG.MobileApp.Services
{
    public class PdfRenderer : IPdfRenderer
    {
        public async Task<Stream?> RenderPageAsync(string filePath, int pageNumber)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                var document = await PdfDocument.LoadFromFileAsync(file);

                int index = pageNumber - 1;
                if (index < 0 || index >= document.PageCount) return null;

                using var page = document.GetPage((uint)index);
                using var stream = new InMemoryRandomAccessStream();

                await page.RenderToStreamAsync(stream);

                var result = new MemoryStream();
                await stream.AsStreamForRead().CopyToAsync(result);
                result.Position = 0;
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Windows PDF Render Error: {ex.Message}");
                return null;
            }
        }
    }
}
