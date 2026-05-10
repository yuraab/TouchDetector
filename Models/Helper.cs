using OBSharp.Sensor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    public static class PlaneChecker
    {
        public static void CheckPlane(List<Vector3> points, PlaneResult best)
        {
            CheckPlane(points, new Plane(best.Normal, best.D));
        }

        public static void CheckPlane(List<Vector3> points, Plane best)
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


        /// <summary>
        /// Finds the primary (dominant) plane in a raw depth image using RANSAC,
        /// then refines it via PCA on the inliers.
        /// "Primary" means the plane with the most supporting depth points —
        /// typically the wall when the camera faces it.
        /// Works entirely in depth camera space using depth intrinsics.
        /// </summary>
        public static (Vector3 Normal, float D)? FindPrimaryPlane(
            ushort[] depthImage,
            int imageWidth,
            int imageHeight,
            Calibration calibration,
            float inlierThresholdMeters = 10f,  // 1cm — tighter = more precise plane
            int ransacIterations = 500,
            int minInliers = 1000,                // reject if plane has too few supporters
            int sampleStep = 1)                   // subsample to speed up — every 4th pixel
        {
            var intr = calibration.DepthCameraCalibration.Intrinsics.Parameters;

            var cx = intr[0];
            var cy = intr[1];
            var fx = intr[2];
            var fy = intr[3];

            // ── 1. Backproject valid depth pixels to 3D (subsampled) ──────────────────
            var points = new List<Vector3>(imageWidth * imageHeight / (sampleStep * sampleStep));

            for (int y = 0; y < imageHeight; y += sampleStep)
                for (int x = 0; x < imageWidth; x += sampleStep)
                {
                    float Z = depthImage[y * imageWidth + x];
                    if (Z <= 0) continue;

                    points.Add(new Vector3(
                        (x - cx) * Z / fx,
                        (y - cy) * Z / fy,
                        Z));
                }

            if (points.Count < 3)
                return null;

            // ── 2. RANSAC ─────────────────────────────────────────────────────────────
            var rng = new Random(42);
            int bestCount = 0;
            var bestNormal = Vector3.UnitZ;
            var bestAnchor = Vector3.Zero;

            for (int iter = 0; iter < ransacIterations; iter++)
            {
                // Pick 3 distinct random points
                var p0 = points[rng.Next(points.Count)];
                var p1 = points[rng.Next(points.Count)];
                var p2 = points[rng.Next(points.Count)];

                Vector3 normal = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
                if (float.IsNaN(normal.X) || float.IsNaN(normal.Y) || float.IsNaN(normal.Z))
                    continue;

                // Ensure normal points toward camera (negative Z in depth space)
                if (normal.Z > 0) normal = -normal;

                float planeD = Vector3.Dot(normal, p0);

                // Count inliers
                int count = 0;
                foreach (var p in points)
                    if (MathF.Abs(Vector3.Dot(normal, p) - planeD) < inlierThresholdMeters)
                        count++;

                if (count > bestCount)
                {
                    bestCount = count;
                    bestNormal = normal;
                    bestAnchor = p0;
                }
            }

            if (bestCount < minInliers)
                return null; // no dominant plane found

            // ── 3. Collect inliers of best plane ──────────────────────────────────────
            float bestD = Vector3.Dot(bestNormal, bestAnchor);
            var inliers = points
                .Where(p => MathF.Abs(Vector3.Dot(bestNormal, p) - bestD) < inlierThresholdMeters)
                .ToList();

            // ── 4. Refine via PCA on inliers ──────────────────────────────────────────
            return RefinePlaneWithPca(inliers);
        }

        /// <summary>
        /// Fits the best plane through a set of 3D points using PCA (SVD-free, iterative).
        /// The plane normal = eigenvector with the smallest eigenvalue of the covariance matrix.
        /// Uses power iteration on the deflated covariance to find the minor eigenvector.
        /// </summary>
        private static (Vector3 Normal, float D) RefinePlaneWithPca(List<Vector3> points)
        {
            // Centroid
            var centroid = Vector3.Zero;
            foreach (var p in points) centroid += p;
            centroid /= points.Count;

            // Covariance matrix (symmetric 3×3)
            float xx = 0, xy = 0, xz = 0,
                           yy = 0, yz = 0,
                                    zz = 0;
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
            xx /= points.Count; xy /= points.Count; xz /= points.Count;
            yy /= points.Count; yz /= points.Count; zz /= points.Count;

            // Power iteration to find the MAJOR eigenvector (largest eigenvalue)
            // We want the MINOR eigenvector (smallest) = plane normal
            // So: find top 2 eigenvectors, the remaining one is the normal

            Vector3 Multiply(Vector3 v) => new(
                xx * v.X + xy * v.Y + xz * v.Z,
                xy * v.X + yy * v.Y + yz * v.Z,
                xz * v.X + yz * v.Y + zz * v.Z);

            // Find largest eigenvector (e1)
            var e1 = Vector3.Normalize(new Vector3(1, 1, 1));
            for (int i = 0; i < 100; i++)
                e1 = Vector3.Normalize(Multiply(e1));

            // Deflate: remove e1 component, find second eigenvector (e2)
            Vector3 DeflatedMultiply(Vector3 v)
            {
                var mv = Multiply(v);
                return mv - Vector3.Dot(mv, e1) * e1;
            }
            var e2 = Vector3.Normalize(Vector3.Cross(e1, new Vector3(0, 0, 1)));
            if (e2.LengthSquared() < 1e-6f)
                e2 = Vector3.Normalize(Vector3.Cross(e1, new Vector3(0, 1, 0)));
            for (int i = 0; i < 100; i++)
                e2 = Vector3.Normalize(DeflatedMultiply(e2));

            // Normal = minor eigenvector = perpendicular to both e1 and e2
            Vector3 normal = Vector3.Normalize(Vector3.Cross(e1, e2));

            // Ensure it points toward camera
            if (normal.Z > 0) normal = -normal;

            float d = Vector3.Dot(normal, centroid);

            if (d < 0) d = -d;

            App.Log($"D = {d}");

            return (normal, d);
        }

        public static (Vector3 Normal, float D) FitPlaneToPoints(Vector3[] pts)
        {
            // Centroid
            var centroid = Vector3.Zero;
            foreach (var p in pts) centroid += p;
            centroid /= pts.Length;

            // Diagonals give most stable normal for a quad
            Vector3 v1 = pts[2] - pts[0];
            Vector3 v2 = pts[3] - pts[1];
            Vector3 normal = Vector3.Normalize(Vector3.Cross(v1, v2));

            // Ensure it points toward camera
            if (normal.Z > 0) normal = -normal;

            float d = Vector3.Dot(normal, centroid);

            if (d < 0) d = -d;

            return (normal, d);
        }

        public static (Vector3 Normal, float D) FitPlaneToPoints(DepthPoint[] ptsD)
        {
            var pts = ptsD.Select(p => p.World).ToArray();

            return FitPlaneToPoints(pts);
        }

        /// <summary>
        /// Projects a 3D point onto a plane along the plane normal.
        /// Result is the closest point on the plane to the input point.
        /// </summary>
        public static Vector3 ProjectPointOntoPlane(Vector3 point, Vector3 planeNormal, float planeD)
        {
            // Signed distance from point to plane
            float distance = Vector3.Dot(planeNormal, point) - planeD;

            // Move point along normal by that distance
            return point - distance * planeNormal;
        }

        public static Vector3 ProjectPointOntoPlane(Vector3 point, Plane plane)
        {
            return ProjectPointOntoPlane(point, plane.Normal, plane.D);
        }

        /// <summary>
        /// Transforms plane from Depth space to Color space.
        /// </summary>
        public static (Vector3 Normal, float D) DepthToColor(
            Vector3 normal, float d,
            Calibration calibration)
            => Transform(normal, d, CalibrationGeometry.Depth, CalibrationGeometry.Color, calibration);

        /// <summary>
        /// Transforms plane from Color space to Depth space.
        /// </summary>
        public static (Vector3 Normal, float D) ColorToDepth(
            Vector3 normal, float d,
            Calibration calibration)
            => Transform(normal, d, CalibrationGeometry.Color, CalibrationGeometry.Depth, calibration);

        /// <summary>
        /// Core transform — handles any geometry pair.
        ///
        /// A plane is defined by (N, D) where dot(N, P) = D for any point P on the plane.
        ///
        /// To transform the plane we need:
        ///   1. Transform the normal (direction — no translation, only rotation)
        ///   2. Transform a point on the plane (full rigid body — rotation + translation)
        ///   3. Recompute D = dot(N', P') in the new space
        ///
        /// We CANNOT just transform D directly because translation affects D.
        /// </summary>
        private static (Vector3 Normal, float D) Transform(
            Vector3 normal, float d,
            CalibrationGeometry from,
            CalibrationGeometry to,
            Calibration calibration)
        {
            // Step 1 — Transform normal as a DIRECTION (no translation)
            // We do this by transforming two points and taking their difference:
            //   origin (0,0,0) and tip of normal (nx, ny, nz)
            // The difference cancels translation, leaving only rotation.
            var originTransformed = calibration.Convert3DTo3D(
                new OBSharp.Float3(0,0,0),
                from, to);

            var tipTransformed = calibration.Convert3DTo3D(
                normal.ToFloat3(),
                from, to);

            if (originTransformed == null || tipTransformed == null)
                throw new InvalidOperationException("Convert3DTo3D failed during plane transform.");

            // Subtract to get pure rotation (translation cancelled)
            Vector3 transformedNormal = Vector3.Normalize(
                tipTransformed.ToVector3() - originTransformed.ToVector3());

            // Step 2 — Transform a point ON the plane
            // Easiest point on plane: origin projected onto plane along normal
            // P = N * D  (works when N is unit length)
            Vector3 pointOnPlane = normal * d;

            var transformedPoint = calibration.Convert3DTo3D(
                pointOnPlane.ToFloat3(),
                from, to);

            if (transformedPoint == null)
                throw new InvalidOperationException("Convert3DTo3D failed for point on plane.");

            // Step 3 — Recompute D in new space
            float transformedD = Vector3.Dot(transformedNormal, transformedPoint.ToVector3());

            return (transformedNormal, transformedD);
        }



    }

}
