using Microsoft.Maui.Controls;
using MotorcycleRAG.MobileApp.ViewModels;
using System;

namespace MotorcycleRAG.MobileApp.Views
{
    public partial class PdfViewerPage : ContentPage
    {
        private double _startScale = 1;

        public PdfViewerPage(PdfViewerViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }

        private void OnPinchUpdated(object sender, PinchGestureUpdatedEventArgs e)
        {
            if (sender is Image image)
            {
                if (e.Status == GestureStatus.Started)
                {
                    _startScale = image.Scale;
                }
                else if (e.Status == GestureStatus.Running)
                {
                    // Calculate new scale
                    double targetScale = _startScale * e.Scale;

                    // Clamp scale
                    targetScale = Math.Max(1, Math.Min(targetScale, 5));

                    image.Scale = targetScale;
                }
            }
        }
    }
}
