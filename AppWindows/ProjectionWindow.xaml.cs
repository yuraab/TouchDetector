using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenCvSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using WinUIEx;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CPRTouchVision
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class ProjectionWindow : WindowEx
    {
        WriteableBitmap? _bitmap;
        byte[]? _buffer;
        public ProjectionWindow()
        {
            InitializeComponent();
            /*
            ProjectionImage.ImageOpened += (_, __) =>
            Debug.WriteLine("ProjectionImage: OPENED");

            ProjectionImage.ImageFailed += (_, e) =>
                Debug.WriteLine("ProjectionImage: FAILED " + e.ErrorMessage);
            
            var grid = new Microsoft.UI.Xaml.Controls.Grid
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Red)
            };

            Content = grid;
            */
        }

        public async void SetImage(string path)
        {
            return;
            Debug.WriteLine($"File: {new Uri(path)}");
            //ProjectionImage.Source = new BitmapImage(new Uri(path));
            return; // just show colored window from simple .xaml file
            try
            {
                if (!File.Exists(path))
                {
                    Debug.WriteLine($"File not found: {path}");
                    return;
                }

                var bitmap = new BitmapImage();

                using var fileStream = File.OpenRead(path);
                var ras = fileStream.AsRandomAccessStream();

                await bitmap.SetSourceAsync(ras);

                //ProjectionImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Image load error: {ex}");
            }

        }

        public void SetMat(Mat input)
        {
            if (input.Empty()) return;

            Mat mat = input;

            // Convert to BGRA (required by WriteableBitmap)
            if (input.Channels() == 1)
            {
                mat = new Mat();
                Cv2.CvtColor(input, mat, ColorConversionCodes.GRAY2BGRA);
            }
            else if (input.Channels() == 3)
            {
                mat = new Mat();
                Cv2.CvtColor(input, mat, ColorConversionCodes.BGR2BGRA);
            }

            int width = mat.Width;
            int height = mat.Height;
            int bytes = width * height * 4;

            if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
            {
                _bitmap = new WriteableBitmap(width, height);
                //ProjectionImage.Source = _bitmap;
                _buffer = new byte[bytes];
            }

            Marshal.Copy(mat.Data, _buffer!, 0, bytes);

            using var stream = _bitmap.PixelBuffer.AsStream();
            stream.Seek(0, SeekOrigin.Begin);
            stream.Write(_buffer!, 0, bytes);

            _bitmap.Invalidate();
        }

    }

}
