using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    public static class Helper
    {
        public static void CheckPlane(List<Vector3> points, PlaneResult best)
        {
            var distsMm = new List<float>(points.Count);
            int over10 = 0, over20 = 0, over40 = 0;

            foreach (var p in points)
            {
                float distM = MathF.Abs(Vector3.Dot(best.Normal, p) + best.D) / best.Normal.Length();
                float distMm = distM;
                distsMm.Add(distMm);
                if (distMm > 10) over10++;
                if (distMm > 20) over20++;
                if (distMm > 40) over40++;
            }

            float maxMm = distsMm.Max();
            float meanMm = distsMm.Average();
            App.Log($"Points count {points.Count}");
            App.Log($"max={maxMm:F1} mm, mean={meanMm:F1} mm, " +
                              $">10mm={over10}, >20mm={over20}, >40mm={over40} (of {points.Count})");
        }
    }
}
