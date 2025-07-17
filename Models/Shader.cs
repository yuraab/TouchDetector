using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ComputeSharp;

namespace CPRTouchVision.Models
{
    [ThreadGroupSize(DefaultThreadGroupSizes.XY)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct DepthToColorShader(
       ReadOnlyBuffer<uint> depthData,
       ReadWriteBuffer<uint> depthPixels
   ) : IComputeShader
    {
        public void Execute()
        {
            int i = ThreadIds.Y * DispatchSize.X + ThreadIds.X;
            int di = i / 2;
            int dio = i % 2;
            uint d = (depthData[di] >> (dio * 16)) & 0xFFFF;
            depthPixels[i] = DepthToBGRA(d, 80);
        }

        private static uint DepthToBGRA(uint depth, uint alpha)
        {
            const uint MinDepth = 500;
            const uint MaxDepth = 3500;

            // Transparent for 0 depth
            if (depth == 0)
                return 0x00000000; // Fully transparent (0xAARRGGBB)

            // If below 500mm, set to a fixed blue (e.g., dark blue)
            if (depth < MinDepth)
                return (uint)((alpha << 24) | (0 << 16) | (0 << 8) | 255); // BGRA: Red

            // If above 6000mm, set to a fixed red
            if (depth > MaxDepth)
                return (uint)((alpha << 24) | (255 << 16) | (0 << 8) | 0); // BGRA: Blue

            // Normalize depth (500mm-6000mm → 0 to 240 hue range)
            float normalized = (depth - MinDepth) / (float)(MaxDepth - MinDepth);

            // Convert normalized value to hue (0-240° scale for rainbow mapping)
            uint hue = (uint)(normalized * 240.0f);

            // Convert Hue to RGB
            uint r, g, b;
            HueToRGB(hue, out r, out g, out b);

            // Return BGRA color format
            return ((alpha << 24) | (b << 16) | (g << 8) | r);
        }

        // Convert Hue (0-240°) to RGB in Compute Shader
        private static void HueToRGB(uint hue, out uint r, out uint g, out uint b)
        {
            const float saturation = 1.0f;
            const float value = 1.0f;

            uint region = hue / 60;
            float remainder = (hue % 60) / 60.0f;

            float p = value * (1 - saturation);
            float q = value * (1 - remainder * saturation);
            float t = value * (1 - (1 - remainder) * saturation);

            float rf = 0, gf = 0, bf = 0;

            switch (region)
            {
                case 0: rf = value; gf = t; bf = p; break;
                case 1: rf = q; gf = value; bf = p; break;
                case 2: rf = p; gf = value; bf = t; break;
                case 3: rf = p; gf = q; bf = value; break;
                case 4: rf = t; gf = p; bf = value; break;
                case 5: rf = value; gf = p; bf = q; break;
            }

            // Convert to uint for shader compatibility
            r = (uint)(rf * 255);
            g = (uint)(gf * 255);
            b = (uint)(bf * 255);
        }
    }
}
