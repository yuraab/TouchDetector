using OBSharp.Sensor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    public class Mock
    {

        public void ExportSingleLiveCaptureToDisk(Device _device, string outputFilePath)
        {
            Debug.WriteLine("Waiting for live Orbbec camera capture...");

            // 1. Fetch a single live frame from your real Orbbec camera device
            if (_device.TryGetCapture(out var capture, TimeSpan.FromMilliseconds(100)))
            {
                using (var fileStream = File.Open(outputFilePath, FileMode.Create))
                using (var writer = new BinaryWriter(fileStream))
                {
                    // 2. Extract frame pointers from the global capture object
                    using var depthImage = capture.DepthImage;
                    using var colorImage = capture.ColorImage;

                    // === SECTION A: EXPORT DEPTH DATA ===
                    if (depthImage != null)
                    {
                        writer.Write(true); // Flag: Depth Exists
                        writer.Write(depthImage.WidthPixels);
                        writer.Write(depthImage.HeightPixels);
                        writer.Write((int)depthImage.Format);

                        // Get unmanaged buffer payload parameters
                        long size = depthImage.SizeBytes;
                        writer.Write(size);

                        // Access memory slice and copy payload to standard byte array
                        byte[] depthBytes = new byte[size];
                        System.Runtime.InteropServices.Marshal.Copy(depthImage.Buffer, depthBytes, 0, (int)size);
                        writer.Write(depthBytes);
                    }
                    else
                    {
                        writer.Write(false); // Flag: False = No Depth stream
                    }

                    // === SECTION B: EXPORT COLOR DATA ===
                    if (colorImage != null)
                    {
                        writer.Write(true); // Flag: Color Exists
                        writer.Write(colorImage.WidthPixels);
                        writer.Write(colorImage.HeightPixels);
                        writer.Write((int)colorImage.Format);

                        long size = colorImage.SizeBytes;
                        writer.Write(size);

                        byte[] colorBytes = new byte[size];
                        System.Runtime.InteropServices.Marshal.Copy(colorImage.Buffer, colorBytes, 0, (int)size);
                        writer.Write(colorBytes);
                    }
                    else
                    {
                        writer.Write(false);
                    }
                }

                // 3. Clean up native pointer tracking immediately
                capture.Dispose();
                Debug.WriteLine($"Mock data profile completely saved to: {outputFilePath}");
            }
            else
            {
                Debug.WriteLine("Error: Orbbec camera timed out or failed to provide data stream.");
            }


        }
    }
}
