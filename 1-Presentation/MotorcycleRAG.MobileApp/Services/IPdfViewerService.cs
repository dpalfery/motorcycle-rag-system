using System.IO;
using System.Threading.Tasks;

namespace MotorcycleRAG.MobileApp.Services
{
    public interface IPdfViewerService
    {
        Task<string> DownloadPdfAsync(string url);
        Task<Stream?> RenderPageToStreamAsync(string localPath, int pageNumber);
    }
}
