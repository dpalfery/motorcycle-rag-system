using Microsoft.Maui.Controls;
using System;
using System.IO;

namespace MotorcycleRAG.MobileApp.Services
{
    public interface IImageSourceFactory
    {
        ImageSource FromStream(Func<Stream> stream);
    }

    public class ImageSourceFactory : IImageSourceFactory
    {
        public ImageSource FromStream(Func<Stream> stream) => ImageSource.FromStream(stream);
    }
}
