using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    public class RANSACPlaneFitter
    {
        private readonly int _iterations;
        private readonly float _threshold; // distance threshold in meters

        public RANSACPlaneFitter(int iterations = 500, float threshold = 20f)
        {
            _iterations = iterations;
            _threshold = threshold;
        }

        public PlaneResult FitPlane(List<Vector3> points)
        {
            if (points.Count < 3)
                throw new ArgumentException("Need at least 3 points to fit a plane.");

            Random rand = new Random();
            PlaneResult best = null;

            for (int i = 0; i < _iterations; i++)
            {
                // pick 3 random distinct points
                var sample = Pick3(points, rand);
                var plane = FitPlaneFrom3(sample[0], sample[1], sample[2]);

                if (plane == null) continue;

                var inliers = new List<Vector3>();
                var outliers = new List<Vector3>();

                foreach (var p in points)
                {
                    float dist = plane.DistanceTo(p);
                    if (dist < _threshold)
                        inliers.Add(p);
                    else
                        outliers.Add(p);
                }

                if (best == null || inliers.Count > best.InliersCount)
                {
                    plane.InliersCount = inliers.Count;
                    plane.Outliers = outliers;
                    best = plane;
                }
            }

            Canonicalize(ref best);
            return best;
        }
        private void Canonicalize(ref PlaneResult plane)
        {
            // Example: enforce Nz < 0
            if (plane.Normal.Z > 0f)
            {
                plane.Normal = -plane.Normal;
                plane.D = -plane.D;
            }

        }

        private List<Vector3> Pick3(List<Vector3> pts, Random rand)
        {
            var result = new HashSet<int>();
            while (result.Count < 3)
                result.Add(rand.Next(pts.Count));
            return result.Select(i => pts[i]).ToList();
        }

        private PlaneResult FitPlaneFrom3(Vector3 p1, Vector3 p2, Vector3 p3)
        {
            var v1 = p2 - p1;
            var v2 = p3 - p1;
            var n = Vector3.Cross(v1, v2);

            if (n.Length() < 1e-6) return null; // degenerate triangle

            n = Vector3.Normalize(n);
            float d = -Vector3.Dot(n, p1);

            return new PlaneResult
            {
                Normal = n,
                D = d
            };
        }
    }

    public class PlaneResult
    {
        public Vector3 Normal { get; set; }
        public float D { get; set; }
        public List<Vector3> Outliers { get; set; } = new();
        public int InliersCount { get; set; }

        public float DistanceTo(Vector3 p)
        {
            return Math.Abs(Vector3.Dot(Normal, p) + D) / Normal.Length();
        }

        public override string ToString()
        {
            return $"Plane equation: {Normal.X:F4}x + {Normal.Y:F4}y + {Normal.Z:F4}z + {D:F4} = 0 " +
                   $"(Inliers: {InliersCount}, Outliers: {Outliers.Count})";
        }
    }
}
