using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace CPRTouchVision.Models
{
    public class WallAnalyzer
    {
        public struct Protrusion
        {
            public Vector3 Position;  // World coordinates of the protrusion point
            public float Distance;    // Distance from fitted plane (positive = towards camera)
        }

        public Vector3 PlaneNormal { get; private set; }
        public float PlaneD { get; private set; }
        public List<Protrusion> LastProtrusions { get; private set; } = new();

        private readonly float _protrusionThresholdMm;
        private readonly bool _detectProtrusions;

        public WallAnalyzer(bool detectProtrusions = false, float protrusionThresholdMm = 30f)
        {
            _detectProtrusions = detectProtrusions;
            _protrusionThresholdMm = protrusionThresholdMm;
        }

        /// <summary>
        /// Fits a plane to the provided 3D points and optionally detects protrusions.
        /// </summary>
        public void Analyze(Vector3[] points)
        {
            if (points == null || points.Length < 3)
                throw new ArgumentException("At least 3 points are required to define a plane");

            // Step 1 — Fit plane using least squares
            FitPlane(points);

            LastProtrusions.Clear();

            // Step 2 — Detect protrusions if enabled
            if (_detectProtrusions)
            {
                foreach (var p in points)
                {
                    float distance = DistanceToPlane(p);
                    if (distance > _protrusionThresholdMm) // mm → meters
                    {
                        LastProtrusions.Add(new Protrusion
                        {
                            Position = p,
                            Distance = distance
                        });
                    }
                }
            }
        }

        private void FitPlane(Vector3[] points)
        {
            var centroid = new Vector3(
                points.Average(p => p.X),
                points.Average(p => p.Y),
                points.Average(p => p.Z)
            );

            float xx = 0, xy = 0, xz = 0;
            float yy = 0, yz = 0, zz = 0;

            foreach (var p in points)
            {
                var r = p - centroid;
                xx += r.X * r.X;
                xy += r.X * r.Y;
                xz += r.X * r.Z;
                yy += r.Y * r.Y;
                yz += r.Y * r.Z;
                zz += r.Z * r.Z;
            }

            var cov = new float[3, 3]
            {
            { xx, xy, xz },
            { xy, yy, yz },
            { xz, yz, zz }
            };

            // Eigen decomposition → smallest eigenvector = normal
            PlaneNormal = SmallestEigenVector(cov);
            PlaneNormal = Vector3.Normalize(PlaneNormal);

            PlaneD = -Vector3.Dot(PlaneNormal, centroid);
        }

        private float DistanceToPlane(Vector3 point)
        {
            return Vector3.Dot(PlaneNormal, point) + PlaneD;
        }

        // Very basic power method for smallest eigenvector
        private Vector3 SmallestEigenVector(float[,] matrix)
        {
            Vector3 v = new Vector3(1, 0, 0);
            Vector3 w = Vector3.Zero;
            float lambda = 0;

            for (int i = 0; i < 30; i++)
            {
                w = Multiply(matrix, v);
                lambda = w.Length();
                if (lambda == 0) break;
                v = Vector3.Normalize(w);
            }

            return v;
        }

        private Vector3 Multiply(float[,] m, Vector3 v)
        {
            return new Vector3(
                m[0, 0] * v.X + m[0, 1] * v.Y + m[0, 2] * v.Z,
                m[1, 0] * v.X + m[1, 1] * v.Y + m[1, 2] * v.Z,
                m[2, 0] * v.X + m[2, 1] * v.Y + m[2, 2] * v.Z
            );
        }
    }

}
