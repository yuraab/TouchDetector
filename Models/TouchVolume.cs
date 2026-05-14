using ABI.System.Numerics;
using CPRTouchVision.Models;
using Dbscan;
using OBSharp;
using OBSharp.Sensor;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
using OB = OBSharp;
using Plane = System.Numerics.Plane;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace CPRTouchVision.Models
{

    /// <summary>
    /// For each depth pixel ray, computes the entry (dmin) and exit (dmax) depth values
    /// where the ray intersects the extruded quadrilateral volume.
    /// The volume is defined as the extrusion of a quad (on a plane) toward the camera.
    /// </summary>
    public class TouchVolumeSlab
    {
        // Camera intrinsics
        private readonly float _fx, _fy, _cx, _cy;

        // Depth camera resolution  
        private readonly int _fw, _fh;

        // Plane equation: dot(N, P) = d
        private readonly Vector3 _wallNormal;
        private readonly float _wallDistance;
        public Plane WallPlane => new Plane(_wallNormal, _wallDistance);  

        // Extrusion offsets along the plane normal TOWARD the camera
        private readonly float _minOffset;
        private readonly float _maxOffset;

        // The 4 corners of the quad ON the plane, in 3D world space (depth camera space)
        // Must be ordered (convex, consistent winding)
        private readonly Vector3[] _quadCorners; // length 4

        // Precomputed: the 6 planes bounding the volume (2 face planes + 4 side planes)
        private readonly (Vector3 Normal, float D)[] _boundingPlanes; // length 6

        // LUT
        private readonly (int DMin, int DMax)?[,] _lut;

        // Tight bounds
        private int _minX, _maxX, _minY, _maxY;

        private bool _isReady = false;
        public bool IsReady => _isReady;

        private readonly ICalibrationProgress _progress;

        private TouchVolumeSlab(
            Vector3 wallNormal, float wallDistance,
            float minOffset, float maxOffset,
            DepthPoint[] quadCorners,
            int fw, int fh,
            Calibration calibration
        )
        {
            _fw = fw;
            _fh = fh;

            var intr = calibration.DepthCameraCalibration.Intrinsics.Parameters;

            _cx = intr.Cx;
            _cy = intr.Cy;
            _fx = intr.Fx;
            _fy = intr.Fy;

            _wallNormal = Vector3.Normalize(wallNormal);
            _wallDistance = wallDistance;

            _minOffset = minOffset;
            _maxOffset = maxOffset;


            _quadCorners = new Vector3[] { 
                quadCorners[0].World, 
                quadCorners[1].World, 
                quadCorners[2].World, 
                quadCorners[3].World 
            };

            _boundingPlanes = PrecomputeBoundingPlanes();

            _lut = new (int DMin, int DMax)?[_fw, _fh];
        }

        /// <summary>
        /// Factory method — construction and heavy computation off the UI thread.
        /// </summary>
        public static async Task<TouchVolumeSlab> CreateAsync(
            Vector3 wallNormal, float wallDistance,
            float minOffset, float maxOffset,
            DepthPoint[] quadCorners,
            int fw, int fh,
            Calibration calibration,
            IProgress<string>? progress = null)
        {
            var instance = new TouchVolumeSlab(
                wallNormal, wallDistance,
                minOffset, maxOffset,
                quadCorners,
                fw, fh,
                calibration
            );

            await Task.Run(() =>
            {
                progress?.Report("Building LUT...");
                instance.BuildLut();

                progress?.Report("Computing bounds...");
                (instance._minX, instance._maxX,
                 instance._minY, instance._maxY) = instance.ComputeTightBounds();

#if DEBUG || TEST


                App.Log($"Min/Max indexes: X => {instance._minX}/{instance._maxX}; Y => {instance._minY}/{instance._maxY}");
#endif
                instance._isReady = true;
            });

            return instance;
        }


        /// <summary>
        /// Precompute the 6 half-space planes that define the extruded volume.
        ///
        /// The volume is a frustum-like prism:
        ///
        ///   Far face  (wall plane offset by extrudeMin along normal toward camera)
        ///   Near face (wall plane offset by extrudeMax along normal toward camera)
        ///   4 side planes (one per quad edge, extruded)
        ///
        ///   Camera
        ///      |
        ///   [_maxOffset] ← near face (closer to camera)
        ///      |
        ///   [_minOffset] ← far face  (closer to wall)
        ///      |
        ///   [wall plane / quad]
        /// </summary>
        private (Vector3 Normal, float D)[] PrecomputeBoundingPlanes()
        {
            var planes = new (Vector3 Normal, float D)[6];

            // Face 1: far face (wall side) — normal points TOWARD camera (+N direction)
            // Points on this plane: quadCorners + _minOffset * N
            Vector3 farFaceNormal = _wallNormal; // points away from wall toward camera
            float farFaceD = _wallDistance + _minOffset;
            planes[0] = (farFaceNormal, farFaceD);

            // Face 2: near face (camera side) — normal points TOWARD wall (-N direction)
            // Points on this plane: quadCorners + _maxOffset * N
            Vector3 nearFaceNormal = -_wallNormal;
            float nearFaceD = -(_wallDistance + _maxOffset);
            planes[1] = (nearFaceNormal, nearFaceD);

            // 4 side planes — one per quad edge
            // For each edge (A→B), the side plane normal is perpendicular to the edge
            // and points INWARD (toward the interior of the quad)
            for (int i = 0; i < 4; i++)
            {
                Vector3 A = _quadCorners[i];
                Vector3 B = _quadCorners[(i + 1) % 4];

                Vector3 edge = B - A;

                // Side plane normal = cross(edge, planeNormal), then normalize
                // This gives a vector perpendicular to the edge, lying in the wall plane
                Vector3 sideNormal = Vector3.Normalize(Vector3.Cross(edge, _wallNormal));

                // Ensure it points INWARD: test against the opposite corner
                Vector3 opposite = _quadCorners[(i + 2) % 4];
                if (Vector3.Dot(sideNormal, opposite - A) < 0)
                    sideNormal = -sideNormal;

                float sideD = Vector3.Dot(sideNormal, A);
                planes[2 + i] = (sideNormal, sideD);
            }

            return planes;
        }
        /// <summary>
        /// For a single depth pixel (x, y), compute dmin and dmax where
        /// the pixel ray intersects the extruded quad volume.
        ///
        /// Returns false if no intersection.
        ///
        /// The ray is: P(t) = t * rayDir, origin at camera (0,0,0)
        /// where rayDir = ((x-cx)/fx, (y-cy)/fy, 1.0) — not normalized,
        /// so t == Z (depth value in meters directly).
        /// </summary>
        private bool ComputeRayDepthRange(int x, int y, out int dmin, out int dmax)
        {
            dmin = int.MinValue;  
            dmax = int.MaxValue;

            // Ray direction (unnormalized: t = Z depth directly)
            var rayDir = new Vector3(
                (x - _cx) / _fx,
                (y - _cy) / _fy,
                1.0f
            );

            // Slab method: intersect ray with all 6 half-spaces
            // For each plane: dot(N, P(t)) >= D  →  t * dot(N, rayDir) >= D
            // → t >= D / dot(N,rd)  or  t <= D / dot(N,rd) depending on sign
            foreach (var (normal, planeD) in _boundingPlanes)
            {
                float denom = Vector3.Dot(normal, rayDir);
                float numer = planeD; // dot(N, origin)=0 since origin=(0,0,0)

                if (MathF.Abs(denom) < 1e-6f)
                {
                    // Ray is parallel to this plane
                    // Check if origin is on the correct side
                    // dot(N, origin) >= planeD → 0 >= planeD
                    if (0 < planeD)
                    {
                        // Origin is outside this half-space → no intersection possible
                        dmin = dmax = 0;
                        return false;
                    }
                    // else: origin inside this slab, no constraint from this plane
                    continue;
                }

                float t = numer / denom;

                if (denom > 0)
                    // Ray enters this half-space at t (dmin moves up)
                    dmin = (int) MathF.Max(dmin, t);
                else
                    // Ray exits this half-space at t (dmax moves down)
                    dmax = (int) MathF.Min(dmax, t);

                if (dmin > dmax)
                    return false; // Empty intersection
            }

            // Clamp to positive depth (in front of camera)
            dmin = (int) MathF.Max(dmin, 0f);

            return dmin <= dmax && dmax > 0f;
        }


        /// <summary>
        /// Precompute a lookup table: for each pixel (x,y), store (dmin, dmax).
        /// Returns null for pixels whose ray doesn't intersect the volume.
        /// Call this ONCE after calibration, reuse every frame.
        /// </summary>
        private void BuildLut()
        {
            Parallel.For(0, _fh, y =>
            {
                for (int x = 0; x < _fw; x++)
                {
                    if (ComputeRayDepthRange(x, y, out int dmin, out int dmax))
                        _lut[x, y] = (dmin, dmax);
                }
            });
        }

        private (int MinX, int MaxX, int MinY, int MaxY) ComputeTightBounds()
        {
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;

            for (int y = 0; y < _fh; y++)
                for (int x = 0; x < _fw; x++)
                {
                    if (_lut[x, y] == null) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

            return (
                minX == int.MaxValue ? 0 : minX,
                maxX == int.MinValue ? _fw - 1 : maxX,
                minY == int.MaxValue ? 0 : minY,
                maxY == int.MinValue ? _fh - 1 : maxY
            );
        }

        public List<(int X, int Y)> CheckFrame(ushort[] depthImage)
        {
            var result = new List<(int X, int Y)>();

            for (int y = _minY; y <= _maxY; y++)
                for (int x = _minX; x <= _maxX; x++)
                {
                    var range = _lut[x, y];
                    if (range == null) continue;

                    float d = depthImage[y * _fw + x];

                    if (d >= range.Value.DMin && d <= range.Value.DMax)
                        result.Add((x, y));
                }

            return result;
        }

        public List<(int X, int Y)> CheckFrameParallel(ushort[] depthImage)
        {
            var result = new List<(int X, int Y)>();

            Parallel.For(_minY, _maxY + 1,
                () => new List<(int X, int Y)>(),
                (y, _, localList) =>
                {
                    for (int x = _minX; x <= _maxX; x++)
                    {
                        var range = _lut[x, y];
                        if (range == null) continue;

                        float d = depthImage[y * _fw + x] / 1000f;

                        if (d >= range.Value.DMin && d <= range.Value.DMax)
                            localList.Add((x, y));
                    }
                    return localList;
                },
                localList => { lock (result) result.AddRange(localList); });

            return result;
        }

    }

    public interface ITouchVolume
    {
        Vector2 GetHomographyCoordinatesFrom2D(Vector2 center);
        Vector3 Get3DPointFromLocal2DPoint(Vector2 center);
        List<Float2> ExtractProjectedPointsInsideVolumeFromImage(ushort[] depthImage);
    }

    public class TouchVolume: ITouchVolume
    {
        public DepthPoint[] Polygon { get; private set; } // Always 4 points in world space

        public Vector2[] Polygon2D { get; private set; }
        public Vector3 WallNormal { get; private set; }
        public float WallDistance { get; private set; }
        public float MinOffset { get; private set; }
        public float MaxOffset { get; private set; }

        // For optimization
        private Vector3 _origin;
        private Vector3 _uAxis, _vAxis;
        private Vector3 planePolygonNormal;
        private Mat _homography;
        private Float3 _uF3, _vF3;
        private float _uu, _vv, _uv, _denom;

        private Vector3 _axisU, _axisV; // local axises on the wall

        private int _minSX, _maxSX, _minSY, _maxSY;
        private ushort _minD, _maxD;

        public Func<Float3, bool> IsProjectedPointInVolume;
        private float _triangleArea012;
        private readonly float _triangleArea023;

        public event Action? WallNotAligned;

        private readonly object _depthLock = new();
        private readonly Calibration _calibration;
        private CalibrationExtrinsics _extrinsics;
        private readonly int _fw, _fh;
#if DEBUG || TEST
        public int counter = 0;
        private string depthValues = "";
        private int counterG;

#endif
        public TouchVolume(
            Vector3 planeNormal,
            float PlaneDistance,
            float minOffset,
            float maxOffset,
            DepthPoint[] polygon,
            ushort minWalDepth,
            ushort maxWalDepth,
            int fw, int fh,
            Calibration calibration)
        {
            if (polygon == null || polygon.Length != 4)
                WallNotAligned?.Invoke();

            //counter = 0;
            App.Log($"Creating TouchVolume with plane normal {planeNormal} and distance {PlaneDistance}. Frame size: {fw}:{fh}");
            Polygon = polygon;
            Polygon2D = new Vector2[4];
            MinOffset = minOffset;
            MaxOffset = maxOffset;
            _fw = fw;
            _fh = fh;
            _calibration = calibration;
            _extrinsics = _calibration.GetExtrinsics(CalibrationGeometry.Color, CalibrationGeometry.Depth);

            WallNormal = planeNormal;
            WallDistance = PlaneDistance;

            // Store main reference vectors for parametric form
            _origin = Polygon[0].World;
            _uAxis = Vector3.Normalize(Polygon[1].World - Polygon[0].World); // horizontal-ish
            _uF3 = new(_uAxis.X, _uAxis.Y, _uAxis.Z);
            var diag = Polygon[3].World - Polygon[0].World;
            var diagProjectedOnU = _uAxis * Vector3.Dot(diag, _uAxis);
            _vAxis = Vector3.Normalize(diag - diagProjectedOnU); // vertical-ish
            _vF3 = new(_vAxis.X, _vAxis.Y, _vAxis.Z);
            Vector3 planePoligonNormal = Vector3.Normalize(Vector3.Cross(_uAxis, _vAxis));

            _uu = Vector3.Dot(_uAxis, _uAxis);
            _vv = Vector3.Dot(_vAxis, _vAxis);
            _uv = Vector3.Dot(_uAxis, _vAxis);
            _denom = _uv * _uv - _uu * _vv;

            if (Math.Abs(_denom) < 1e-5f)
            {
                WallNotAligned?.Invoke();
            }

            // Pick axisU as any vector perpendicular to normal
            _axisU = Math.Abs(WallNormal.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
            _axisU = Vector3.Normalize(Vector3.Cross(WallNormal, _axisU));
            // Vector perpendicular to _axisU
            _axisV = Vector3.Cross(WallNormal, _axisU);

            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = Polygon[i].World - _origin;

                // Project onto u and v to get 2D coordinates
                float x = Vector3.Dot(corner, _uAxis);
                float y = Vector3.Dot(corner, _vAxis);

                Polygon2D[i] = new Vector2(x, y);
            }

            _triangleArea012 = TriangleArea(Polygon2D[0], Polygon2D[1], Polygon2D[2]);
            _triangleArea023 = TriangleArea(Polygon2D[0], Polygon2D[2], Polygon2D[3]);

            // Precompute min/max bounds in world space for the aligned case
            /*
            _minX = Polygon.Min(p => p.World.X);
            _maxX = Polygon.Max(p => p.World.X);
            _minY = Polygon.Min(p => p.World.Y);
            _maxY = Polygon.Max(p => p.World.Y);
            _minZ = Polygon.Min(p => p.World.Z);
            _maxZ = Polygon.Max(p => p.World.Z);
            */
            // Precompute screen/depth bounds for scan restriction
            _minSX = _fw; _maxSX = 0;
            _minSY = _fh; _maxSY = 0;
            _minD = ushort.MaxValue; _maxD = 0;


            UpdateScreenMinMax(Polygon, minOffset);
            UpdateScreenMinMax(Polygon, maxOffset);
#if DEBUG || TEST
            App.Log($"Wall plane normal => {WallNormal}, distance => {WallDistance}");
            App.Log($"Filter points should be into => {WallDistance - MinOffset} - {WallDistance - MaxOffset}");
            App.Log($"Filter points into distance => {_maxD} - {_minD}");
            App.Log($"Filter points into X => {_minSX} - {_maxSX}");
            App.Log($"Filter points into Y => {_minSY} - {_maxSY}");
#endif
            //Get the 2D source quad coordinates
            var src0 = ProjectPlanePointToUV(_origin); // should be (0,0)
            var src1 = ProjectPlanePointToUV(Polygon[1].World);
            var src2 = ProjectPlanePointToUV(Polygon[2].World);
            var src3 = ProjectPlanePointToUV(Polygon[3].World);
            // prepare arrays of Point2f
            Point2f[] srcPts = new[] {
                new Point2f(src0.X, src0.Y),
                new Point2f(src1.X, src1.Y),
                new Point2f(src2.X, src2.Y),
                new Point2f(src3.X, src3.Y)
            };
#if DEBUG || TEST
            App.Log($"Projected points in the local coordinates");
            App.Log($"Top Left => {src0.X}:{src0.Y}");
            App.Log($"Top Right => {src1.X}:{src1.Y}");
            App.Log($"Bottom Right => {src2.X}:{src2.Y}");
            App.Log($"Bottom Left => {src3.X}:{src3.Y}");
#endif
            Point2f[] dstPts = new[] {
                new Point2f(0f, 0f),
                new Point2f(1f, 0f),
                new Point2f(1f, 1f),
                new Point2f(0f, 1f)
            };

            _homography = Cv2.GetPerspectiveTransform(srcPts, dstPts); // 3x3 homography
        }

        OBSharp.Float3 TransformToDepthSpace(OBSharp.Float3 worldPoint)
        {
            // Invert rotation (transpose for orthonormal matrix)
            var R = _extrinsics.Rotation;
            var T = _extrinsics.Translation;

            // Subtract translation
            float x = worldPoint.X - T[0];
            float y = worldPoint.Y - T[1];
            float z = worldPoint.Z - T[2];

            // Apply transposed rotation
            float dx = R[0] * x + R[3] * y + R[6] * z;
            float dy = R[1] * x + R[4] * y + R[7] * z;
            float dz = R[2] * x + R[5] * y + R[8] * z;

            return new OBSharp.Float3(dx, dy, dz);
        }

        float TransformToDepth(OBSharp.Float3 worldPoint)
        {
            //var depthPoint = TransformToDepthSpace(worldPoint);
            //return depthPoint.Z;
            return worldPoint.Z;
        }

        Vector3 ShiftAlongNormal(Vector3 point, float offset, bool towardCamera)
        {
            return towardCamera ? point + WallNormal * offset : point - WallNormal * offset;
        }

        private void UpdateScreenMinMax(DepthPoint[] poly, float offset)
        {
            foreach (var p in poly)
            {
                var shifted = ShiftAlongNormal(p.World, offset, true); // point shifted from wall on offset
                var proj = _calibration.Convert3DTo2D(new(shifted.X, shifted.Y, shifted.Z), CalibrationGeometry.Depth, CalibrationGeometry.Color);

                int sx = p.SX, sy = p.SY;
                if (proj.HasValue)
                {
                    sx = (int)proj.Value.X;
                    sy = (int)proj.Value.Y;
                }

                if (sx < _minSX) _minSX = sx;
                if (sx > _maxSX) _maxSX = sx;
                if (sy < _minSY) _minSY = sy;
                if (sy > _maxSY) _maxSY = sy;

                var depth = TransformToDepth(new OBSharp.Float3(shifted.X, shifted.Y, shifted.Z));
#if DEBUG || TEST
                App.Log($"Original Point => {p.World} Shifted Point => {shifted} has depth {depth}");

#endif
                if (depth < _minD) _minD = (ushort)Math.Floor(depth);
                if (depth > _maxD) _maxD = (ushort)Math.Ceiling(depth);
            }

        }

        public bool IsPointInVolume(Vector3 point, out Vector2? point2D)
        {
            point2D = new Vector2?();
            float distanceToPlane = Vector3.Dot(WallNormal, point) + WallDistance;
            if (distanceToPlane > MaxOffset || distanceToPlane < MinOffset)
                return false;
#if DEBUG
            counter++;
#endif

            // Get 3D projected point
            Vector3 projected = new Vector3(
                point.X - WallNormal.X * distanceToPlane,
                point.Y - WallNormal.Y * distanceToPlane,
                point.Z - WallNormal.Z * distanceToPlane
            ) - _origin;

            // Get 2D projected point
            // Project onto u and v to get 2D coordinates
            float x = Vector3.Dot(projected, _uAxis);
            float y = Vector3.Dot(projected, _vAxis);

            point2D = new Vector2(x, y);

            return PointInTriangle(point2D!.Value, Polygon2D[0], Polygon2D[1], Polygon2D[2], _triangleArea012) ||
                   PointInTriangle(point2D!.Value, Polygon2D[0], Polygon2D[2], Polygon2D[3], _triangleArea023);
        }

        public bool IsPointInVolume(OB.Float3 point, out Vector2? point2D)
        {
            point2D = new Vector2?();
            float distanceToPlane = WallNormal.X * point.X
                       + WallNormal.Y * point.Y
                       + WallNormal.Z * point.Z
                       + WallDistance;
#if DEBUG || TEST
            counterG++;
            //depthValues += $"{distanceToPlane.ToString("F3")}; ";
#endif
            if (distanceToPlane > MaxOffset || distanceToPlane < MinOffset)
                return false;

#if DEBUG || TEST
            counter++;
#endif

        // Get 3D projected point
        Vector3 projected = new Vector3(
                point.X - WallNormal.X * distanceToPlane,
                point.Y - WallNormal.Y * distanceToPlane,
                point.Z - WallNormal.Z * distanceToPlane
            ) - _origin;

            // Get 2D projected point
            // Project onto u and v to get 2D coordinates
            float x = Vector3.Dot(projected, _uAxis);
            float y = Vector3.Dot(projected, _vAxis);

            point2D = new Vector2(x, y);

            return PointInTriangle(point2D!.Value, Polygon2D[0], Polygon2D[1], Polygon2D[2], _triangleArea012) ||
                   PointInTriangle(point2D!.Value, Polygon2D[0], Polygon2D[2], Polygon2D[3], _triangleArea023);
        }


        bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float areaOrig)
        {
            float areaSum = TriangleArea(a, b, p) +
                            TriangleArea(b, c, p) +
                            TriangleArea(a, c, p);

            return Math.Abs(areaOrig - areaSum) < 1e-4f;
        }

        private static float TriangleArea(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return 0.5f * Math.Abs(
                p1.X * (p2.Y - p3.Y) +
                p2.X * (p3.Y - p1.Y) +
                p3.X * (p1.Y - p2.Y)
            );
        }

        public static float Dot(OB.Float3 a, OB.Float3 b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        public List<Vector3> Extract3DPointsInsideVolumeFromList(List<Vector3> points)
        {
            List<Vector3> result;
            lock (_depthLock)
            {
                result = new List<Vector3>(points.Count);
                foreach (var point in points)
                {
                    if (IsPointInVolume(point, out _))
                        result.Add(point);
                }
            }
            return result;
        }

        public List<Float2> ExtractProjectedPointsInsideVolumeFromList(List<Vector3> points)
        {
            List<Float2> result;
            lock (_depthLock)
            {
                result = new List<Float2>(points.Count);
                foreach (var point in points)
                {
                    var point2D = new Vector2?();
                    if (IsPointInVolume(point, out point2D) && point2D != null)
                        result.Add(new(point2D.Value.X, point2D.Value.Y));
                }
            }
            return result;

        }
        public List<Float3> Extract3DPointsInsideVolumeFromImage(ushort[] depthImage)
        {
            var result = new List<OB.Float3>();
            int step = 2;
            lock (_depthLock)
            {
                var localLists = new List<OB.Float3>[Environment.ProcessorCount];
                Parallel.For(0, localLists.Length, i => localLists[i] = new List<OB.Float3>());

                Parallel.ForEach(
                    Partitioner.Create(_minSY, _maxSY),
                    new ParallelOptions { MaxDegreeOfParallelism = localLists.Length },
                    () => new List<OB.Float3>(),
                    (range, _, localList) =>
                    {
                        for (int y = range.Item1; y < range.Item2; y++)
                        {
                            if ((y - _minSY) % step != 0) continue;

                            for (int x = _minSX; x < _maxSX; x += step)
                            {
                                int index = y * _fw + x;
                                float d = depthImage[index];

                                if (d <= 0 || d < _minD || d > _maxD) continue;

                                var world = _calibration.Convert2DTo3D(new(x, y), d, CalibrationGeometry.Color, CalibrationGeometry.Depth);
                                if (world == null) continue;

                                var point2D = new Vector2?();
                                if (IsPointInVolume(world.Value, out point2D))
                                    localList.Add(world.Value);

                            }
                        }
                        return localList;
                    },
                    localList => { lock (result) result.AddRange(localList); });
            }

            return result;
        }
        public List<Float2> ExtractProjectedPointsInsideVolumeFromImage(ushort[] depthImage)
        {
            var result = new List<OB.Float2>();
            int step = 2;
#if DEBUG ||TEST
            counter = 0;
            counterG = 0;
            depthValues = "";
            var maxDepth = 0f;
#endif
            lock (_depthLock)
            {
                var localLists = new List<OB.Float2>[Environment.ProcessorCount];
                Parallel.For(0, localLists.Length, i => localLists[i] = new List<OB.Float2>());

                Parallel.ForEach(
                    Partitioner.Create(_minSY, _maxSY),
                    new ParallelOptions { MaxDegreeOfParallelism = localLists.Length },
                    () => new List<OB.Float2>(),
                    (range, _, localList) =>
                    {
                        for (int y = range.Item1; y < range.Item2; y++)
                        {
                            if ((y - _minSY) % step != 0) continue;

                            for (int x = _minSX; x < _maxSX; x += step)
                            {
#if DEBUG || TEST
                                counterG++;
#endif
                                int index = y * _fw + x;
                                float d = depthImage[index];

#if DEBUG || TEST
                                if (d > maxDepth) maxDepth = d;
#endif
                                //if (d < _minD || d > _maxD) continue;
                                if (d < _minD) continue;
                                //if (d <= 0) continue;
#if DEBUG || TEST
                                counter++;
#endif
                                var world = _calibration.Convert2DTo3D(new(x, y), d, CalibrationGeometry.Depth, CalibrationGeometry.Depth);
                                if (world == null) continue;

                                var point2D = new Vector2?();
                                if (IsPointInVolume(world.Value, out point2D) && point2D != null)
                                    localList.Add(new (point2D.Value.X, point2D.Value.Y));

                            }
                        }
                        return localList;
                    },
                    localList => { lock (result) result.AddRange(localList); });
            }
#if DEBUG || TEST
            App.Log($"Counter of points between Offsets: {counter}");
            App.Log($"Counter of points in ROI: {counterG}");
            App.Log($"Result count: {result.Count}");
            App.Log($"Max depth value: {maxDepth}");
#endif
            return result;
        }
        Vector3 GetProjectionToPlane(Vector3 point)
        {
            var distanceToPlane = Vector3.Dot(WallNormal, point) + WallDistance;
            return point - WallNormal * distanceToPlane;
        }
        private Vector2 ProjectPlanePointToUV(Vector3 P)
        {
            Vector3 r = P - _origin;
            float s = Vector3.Dot(r, _uAxis); // coordinate along u 
            float t = Vector3.Dot(r, _vAxis); // coordinate along v
            return new Vector2(s, t);
        }

        public Vector2 GetHomographyCoordinatesFrom3D(Vector3 point)
        {
            var projectedPoint = GetProjectionToPlane(point);
            var point2D = ProjectPlanePointToUV(projectedPoint);
            return GetHomographyCoordinatesFrom2D(point2D);
        }
        public Vector2 GetHomographyCoordinatesFrom2D(Vector2 point2D)
        {
            Point2f projected2D = new(point2D.X, point2D.Y);
            Point2f[] mapped = Cv2.PerspectiveTransform(new[] { projected2D }, _homography);
            return new(mapped[0].X, mapped[0].Y);
        }

        public Vector3 Get3DPointFromLocal2DPoint(Vector2 point)
        {
            return _origin + _uAxis * point.X + _vAxis * point.Y;
        }

        /*
        public List<Float2> ExtractProjectedPointsInsideVolume(ushort[] depthImage)
        {
            var points3D = Extract3DPointsInsideVolume(depthImage);
            List<Float2> points2D = new List<Float2>();
            foreach (var point3D in points3D)
            {
                var pointOnPlane = GetProjectionToPlane(point3D.ToVector3());
                var point = ProjectPlanePointToUV(pointOnPlane);
                points2D.Add(new(point.X, point.Y));
            }
            return points2D;
        }
        */
    }

    /// <summary>
    /// Native depth-space LUT touch volume.
    ///
    /// For every depth pixel ray:
    /// precomputes [DMin,DMax] in millimeters
    /// where the ray intersects the touch slab volume.
    ///
    /// Runtime:
    ///     ushort depth compare only.
    ///
    /// Plane convention:
    ///     dot(N,P) + D = 0
    ///
    /// Units:
    ///     millimeters everywhere.
    /// </summary>
    public sealed class LutTouchVolume : ITouchVolume
    {
        //
        // Intrinsics
        //

        private readonly float _fx;
        private readonly float _fy;
        private readonly float _cx;
        private readonly float _cy;

        //
        // Frame size
        //

        private readonly int _fw;
        private readonly int _fh;

        //
        // Plane
        // dot(N,P)+D=0
        //

        private readonly Vector3 _wallNormal;
        private readonly float _wallD;

        public Plane WallPlane =>
            new(_wallNormal, _wallD);

        //
        // Offsets toward camera
        // in millimeters
        //

        private readonly float _minOffset;
        private readonly float _maxOffset;

        //
        // Quad corners ON WALL
        // in native depth space
        //

        private readonly Vector3[] _quadCorners;

        //
        // Convex volume planes
        //

        private readonly VolumePlane[] _planes;

        //
        // LUT
        //

        private readonly DepthRange?[] _lut;

        //
        // Tight bounds
        //

        private int _minX;
        private int _maxX;
        private int _minY;
        private int _maxY;

        //
        // Ready
        //

        public bool IsReady { get; private set; }

        //
        // Structs
        //

        private readonly struct VolumePlane
        {
            public readonly Vector3 Normal;
            public readonly float D;

            public VolumePlane(
                Vector3 normal,
                float d)
            {
                Normal = normal;
                D = d;
            }
        }

        private readonly struct DepthRange
        {
            public readonly ushort Min;
            public readonly ushort Max;

            public DepthRange(
                ushort min,
                ushort max)
            {
                Min = min;
                Max = max;
            }
        }

        //
        // Constructor
        //

        private LutTouchVolume(
            Vector3 wallNormal,
            float wallD,
            float minOffset,
            float maxOffset,
            Vector3[] quadCorners,
            int fw,
            int fh,
            Calibration calibration)
        {
            _fw = fw;
            _fh = fh;

            var intr =
                calibration
                    .DepthCameraCalibration
                    .Intrinsics
                    .Parameters;

            _cx = intr.Cx;
            _cy = intr.Cy;
            _fx = intr.Fx;
            _fy = intr.Fy;

            _wallNormal = Vector3.Normalize(wallNormal);

            _wallD = wallD;

            _minOffset = minOffset;
            _maxOffset = maxOffset;

            _quadCorners = quadCorners;

            _planes = BuildBoundingPlanes();

            _lut = new DepthRange?[fw * fh];
        }

        //
        // Factory
        //

        public static async Task<LutTouchVolume>
            CreateAsync(
                Vector3 wallNormal,
                float wallD,
                float minOffset,
                float maxOffset,
                Vector3[] quadCorners,
                int fw,
                int fh,
                Calibration calibration,
                IProgress<string>? progress = null)
        {
            var v = new LutTouchVolume(
                wallNormal,
                wallD,
                minOffset,
                maxOffset,
                quadCorners,
                fw,
                fh,
                calibration);

            await Task.Run(() =>
            {
                progress?.Report("Building LUT...");
                v.BuildLut();

                progress?.Report("Computing bounds...");
                (
                    v._minX,
                    v._maxX,
                    v._minY,
                    v._maxY
                ) = v.ComputeBounds();

                v.IsReady = true;
            });

            return v;
        }

        //
        // Build planes
        //

        private VolumePlane[] BuildBoundingPlanes()
        {
            var planes =
                new VolumePlane[6];

            //
            // Far face
            //

            Vector3 farPoint =
                _quadCorners[0]
                + _wallNormal * _minOffset;

            planes[0] =
                new VolumePlane(
                    _wallNormal,
                    -Vector3.Dot(
                        _wallNormal,
                        farPoint));

            //
            // Near face
            //

            Vector3 nearPoint =
                _quadCorners[0]
                + _wallNormal * _maxOffset;

            planes[1] =
                new VolumePlane(
                    -_wallNormal,
                    -Vector3.Dot(
                        -_wallNormal,
                        nearPoint));

            //
            // Side planes
            //

            for (int i = 0; i < 4; i++)
            {
                Vector3 A =
                    _quadCorners[i];

                Vector3 B =
                    _quadCorners[
                        (i + 1) % 4];

                Vector3 edge =
                    B - A;

                Vector3 sideNormal =
                    Vector3.Normalize(
                        Vector3.Cross(
                            edge,
                            _wallNormal));

                Vector3 opposite =
                    _quadCorners[
                        (i + 2) % 4];

                if (Vector3.Dot(
                        sideNormal,
                        opposite - A) < 0)
                {
                    sideNormal =
                        -sideNormal;
                }

                float d =
                    -Vector3.Dot(
                        sideNormal,
                        A);

                planes[2 + i] =
                    new VolumePlane(
                        sideNormal,
                        d);
            }

            return planes;
        }

        //
        // Ray/slab intersection
        //

        private bool ComputeDepthRange(
            int x,
            int y,
            out ushort dmin,
            out ushort dmax)
        {
            dmin = 0;
            dmax = ushort.MaxValue;

            //
            // IMPORTANT:
            // z=t in millimeters
            //

            Vector3 rayDir =
                new(
                    (x - _cx) / _fx,
                    (y - _cy) / _fy,
                    1.0f);

            float tNear = 0;
            float tFar = 10000;

            foreach (var p in _planes)
            {
                float denom =
                    Vector3.Dot(
                        p.Normal,
                        rayDir);

                //
                // dot(N,P)+D>=0
                //

                float numer = -p.D;

                if (MathF.Abs(denom) < 1e-6f)
                {
                    //
                    // Parallel
                    //

                    if (numer < 0)
                        return false;

                    continue;
                }

                float t = numer / denom;

                if (denom > 0)
                    tNear = MathF.Max(tNear, t);
                else
                    tFar =
                        MathF.Min(tFar, t);

                if (tNear > tFar)
                    return false;
            }

            if (tFar <= 0)
                return false;

            dmin = (ushort)MathF.Max(0, MathF.Round(tNear));

            dmax = (ushort)MathF.Round(tFar);

            return dmax > dmin;
        }

        //
        // Build LUT
        //

        private void BuildLut()
        {
            Parallel.For(0, _fh, y =>
                {
                    int row = y * _fw;

                    for (int x = 0; x < _fw; x++)
                    {
                        if (ComputeDepthRange(
                                x, y,
                                out ushort dmin,
                                out ushort dmax))
                        {
                            _lut[row + x] =
                                new DepthRange(dmin, dmax);
                        }
                    }
                });
        }

        //
        // Compute tight bounds
        //

        private (int MinX, int MaxX, int MinY, int MaxY) ComputeBounds()
        {
            int minX = int.MaxValue;
            int maxX = int.MinValue;

            int minY = int.MaxValue;
            int maxY = int.MinValue;

            for (int y = 0; y < _fh; y++)
            {
                int row = y * _fw;

                for (int x = 0; x < _fw; x++)
                {
                    if (_lut[row + x] == null)
                        continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;

                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            return (
                minX == int.MaxValue ? 0 : minX,
                maxX == int.MinValue ? _fw - 1 : maxX,
                minY == int.MaxValue ? 0 : minY,
                maxY == int.MinValue ? _fh - 1 : maxY
            );
        }

        //
        // Runtime
        //

        public List<(int X, int Y)>ExtractPixels(ushort[] depthImage)
        {
            var result =
                new List<(int X, int Y)>();

            for (int y = _minY; y <= _maxY; y++)
            {
                int row = y * _fw;

                for (int x = _minX; x <= _maxX; x++)
                {
                    var range =
                        _lut[row + x];

                    if (range == null)
                        continue;

                    ushort d =
                        depthImage[row + x];

                    if (d == 0)
                        continue;

                    if (d >= range.Value.Min &&
                        d <= range.Value.Max)
                    {
                        result.Add((x, y));
                    }
                }
            }

            return result;
        }

        Vector2 ITouchVolume.GetHomographyCoordinatesFrom2D(Vector2 center)
        {
            throw new NotImplementedException();
        }

        Vector3 ITouchVolume.Get3DPointFromLocal2DPoint(Vector2 center)
        {
            throw new NotImplementedException();
        }

        List<Float2> ITouchVolume.ExtractProjectedPointsInsideVolumeFromImage(ushort[] depthImage)
        {
            var pixels = ExtractPixels(depthImage);
            var result = new List<Float2>();

            foreach (var (x, y) in pixels)
            {
                result.Add(new Float2(x, y));
            }

            return result;
        }
    }



}

