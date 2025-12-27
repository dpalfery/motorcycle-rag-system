using System.IO;
using System.Threading.Tasks;
using Foundation;
using PdfKit;
using UIKit;
using MotorcycleRAG.MobileApp.Services;

namespace MotorcycleRAG.MobileApp.Services
{
    public class PdfRenderer : IPdfRenderer
    {
        public Task<Stream?> RenderPageAsync(string filePath, int pageNumber)
        {
            return Task.Run<Stream?>(() =>
            {
                var url = NSUrl.FromFilename(filePath);
                var document = new PdfDocument(url);

                // 0-indexed
                int index = pageNumber - 1;
                if (index < 0 || index >= document.PageCount) return null;

                var page = document.GetPage(index);
                if (page == null) return null;

                var rect = page.GetBoundsForBox(PdfDisplayBox.Media);
                var renderer = new UIGraphicsImageRenderer(rect.Size);

                var image = renderer.CreateImage((context) =>
                {
                    UIColor.White.SetFill();
                    context.FillRect(rect);

                    // Flip context for PDF drawing
                    context.CGContext.TranslateCTM(0, rect.Height);
                    context.CGContext.ScaleCTM(1, -1);

                    page.Draw(PdfDisplayBox.Media, context.CGContext);
                });

                var pngData = image.AsPNG();
                return pngData?.AsStream();
            });
        }
    }
}
