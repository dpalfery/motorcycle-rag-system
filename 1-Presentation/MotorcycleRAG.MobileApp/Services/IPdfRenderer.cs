using System.IO;
using System.Threading.Tasks;

namespace MotorcycleRAG.MobileApp.Services
{
    public interface IPdfRenderer
    {
        Task<Stream?> RenderPageAsync(string filePath, int pageNumber);
    }
}
