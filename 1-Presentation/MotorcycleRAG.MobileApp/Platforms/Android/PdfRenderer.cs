using System.IO;
using System.Threading.Tasks;
using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using MotorcycleRAG.MobileApp.Services;
using Path = System.IO.Path;

namespace MotorcycleRAG.MobileApp.Services
{
    public class PdfRenderer : IPdfRenderer
    {
        public async Task<Stream?> RenderPageAsync(string filePath, int pageNumber)
        {
            return await Task.Run(() =>
            {
                var file = new Java.IO.File(filePath);
                var fileDescriptor = ParcelFileDescriptor.Open(file, ParcelFileMode.ReadOnly);
                if (fileDescriptor == null) return null;

                using var pdfRenderer = new Android.Graphics.Pdf.PdfRenderer(fileDescriptor);

                // Page numbers are 0-indexed in Android PdfRenderer, but 1-indexed in our app
                int index = pageNumber - 1;
                if (index < 0 || index >= pdfRenderer.PageCount) return null;

                using var page = pdfRenderer.OpenPage(index);

                // Create bitmap
                var bitmap = Bitmap.CreateBitmap(page.Width, page.Height, Bitmap.Config.Argb8888);

                // Render to bitmap
                page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);

                var stream = new MemoryStream();
                bitmap.Compress(Bitmap.CompressFormat.Png, 100, stream);
                stream.Position = 0;

                return stream;
            });
        }
    }
}
