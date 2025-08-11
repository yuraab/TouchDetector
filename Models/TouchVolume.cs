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

namespace CPRTouchVision.Models
{
    public class TouchVolumeRect
    {
        public Vector3 WallNormal { get; }
        public float WallDistance { get; }

        public float MinOffset { get; }
        public float MaxOffset { get; }

        public DepthPoint Corner1 { get; private set; }
        public DepthPoint Corner2 { get; private set; }
        public DepthPoint Corner3 { get; private set; }
        public DepthPoint Corner4 { get; private set; }

        public Vector3 WallOrigin;
        public Vector3 HorizontalVector;
        public Vector3 VerticalVector;

        public float SquaredHorizontal { get; private set; }
        public float SquaredVertical { get; private set; }
        public float HowAligned { get; private set; }
        public float Denom { get; private set; }
        public event Action WallNotAligned;
        public Func<Vector3, bool> IsProjectedPointInVolume;

        public  DepthPoint[] WallLayer { get; }
        private DepthPoint[] Layer1;
        private DepthPoint[] Layer2;

        private readonly object _depthLock = new object();
        private readonly Calibration _calibration;
        //private readonly CalibrationGeometry;
        private readonly int _fw;
        private readonly int _fh;
        private int _minSX;
        private int _maxSX;
        private int _minSY;
        private int _maxSY;
        private int _minD;
        private int _maxD;

        public int minSX => _minSX;
        public int maxSX => _maxSX;
        public int minSY => _minSY;
        public int maxSY => _maxSY;

        public TouchVolumeRect(Vector3 wallNormal, float wallDistance,
                           float minOffset, float maxOffset,
                           DepthPoint wallCorner1, DepthPoint wallCorner2,
                           int fw, int fh,
                           Calibration calibration
            )
        {
            _fw = fw;
            _fh = fh;

            _calibration = calibration;

            WallNormal = Vector3.Normalize(wallNormal);
            WallDistance = wallDistance;

            MinOffset = minOffset;
            MaxOffset = maxOffset;

            DepthPoint c1, c2, c3, c4;

            TouchZoneHelper.ComputeAllFourCorners(
                    wallCorner1,
                    wallCorner2,
                    WallNormal,
                    _calibration,
                    out c1,
                    out c2,
                    out c3,
                    out c4);

            (Corner1, Corner2, Corner3, Corner4) = (c1, c2, c3, c4);

            HorizontalVector = Corner2.World - Corner1.World;
            VerticalVector = Corner4.World - Corner1.World;
            SquaredHorizontal = Vector3.Dot(HorizontalVector, HorizontalVector);
            SquaredVertical = Vector3.Dot(VerticalVector, VerticalVector);
            HowAligned = Vector3.Dot(HorizontalVector, VerticalVector);
            Denom = HowAligned * HowAligned - SquaredHorizontal * SquaredVertical;
            
            if (Math.Abs(Denom) < 1e-5f)
            {
                WallNotAligned?.Invoke();  
            }

            Vector3 cameraForward = new Vector3(0, 0, -1);

            // Compute cosine of angle between normal and view
            float facing = Math.Abs(Vector3.Dot(WallNormal, cameraForward));
            if (facing >= 0.9f)
            {
                IsProjectedPointInVolume = IsProjectedPointInVolumeWhenWallAligned;
                App.Log($"Wall is almost aligned to camera ({facing}). Switch to simple calculation");
            }
            else
            {
                IsProjectedPointInVolume = IsProjectedPointInVolumeWhenWallNotAligned;
                App.Log($"Wall is not aligned to camera ({facing}). Switch to complex calculation");
            }

            WallLayer = new DepthPoint[] { Corner1, Corner2, Corner3, Corner4 };
            Layer1 = GetLayer(minOffset);
            Layer2 = GetLayer(maxOffset);
            _minSX = _fw;
            _maxSX = 0;
            _minSY = _fh;
            _maxSY = 0;
            _minD = int.MaxValue;
            _maxD = 0;
#if DEBUG
            App.Log($"Min/Max indexes: X => {_minSX}/{_maxSX}   Y => {_minSY}/{_maxSY}");
#endif
            // Narrow iteration indexes by defining min/max screen SX/SY

            UpdateScrenMinMax(Layer1);
            UpdateScrenMinMax(Layer2);
#if DEBUG
            App.Log($"Wall projection => Corner1: {Corner1.SX}:{Corner1.SY}  Corner2: {Corner3.SX}:{Corner3.SY}");
            App.Log($"Layer1 projection => Corner1: {Layer1[0].SX}:{Layer1[0].SY}  Corner2: {Layer1[2].SX}:{Layer1[2].SY}");
            App.Log($"Layer2 projection => Corner1: {Layer2[0].SX}:{Layer2[0].SY}  Corner2: {Layer2[2].SX}:{Layer2[2].SY}");
            App.Log($"Min/Max indexes: X => {_minSX}/{_maxSX}   Y => {_minSY}/{_maxSY}");
            App.Log($"Min/Max depth: {_minD}/{_maxD}");

            foreach (var point in WallLayer)
            {
                App.Log($"Point of WallLayer {point.World} Distance: {point.World.Length()}");
            }
            foreach (var point in Layer1)
            {
                App.Log($"Point of Layer1 {point.World} Distance: {point.World.Length()}");
            }
            foreach (var point in Layer2)
            {
                App.Log($"Point of Layer2 {point.World} Distance: {point.World.Length()}");
            }
            App.Log($"Processing size: {(maxSX - minSX)}*{(maxSY - minSY)} = {(maxSX - minSX) * (maxSY - minSY)}");

#endif

            // Precompute bounds for faster volume checks

            _minX = MathF.Min(Corner1.World.X, Corner3.World.X);
            _maxX = MathF.Max(Corner1.World.X, Corner3.World.X);
            _minY = MathF.Min(Corner1.World.Y, Corner3.World.Y);
            _maxY = MathF.Max(Corner1.World.Y, Corner3.World.Y);
            _minZ = MathF.Min(Corner1.World.Z, Corner3.World.Z);
            _maxZ = MathF.Max(Corner1.World.Z, Corner3.World.Z);
            
        }

        private readonly float _minX, _maxX, _minY, _maxY, _minZ, _maxZ;

        /// <summary>
        /// Checks if a given point lies inside the touchable volume.
        /// </summary>
        /// 

        void UpdateScrenMinMax(DepthPoint[] layer)
        {

            for (int i = 0; i < layer.Length; i++)
            {
                var p = layer[i];
#if DEBUG
                App.Log($"Layer index: X => {p.SX}   Y => {p.SY}");
#endif
                if (p.SX < _minSX) _minSX = p.SX;
                if (p.SX > _maxSX) _maxSX = p.SX;
                 
                if (p.SY < _minSY) _minSY = p.SY;
                if (p.SY > _maxSY) _maxSY = p.SY;

                if (p.World.Z < _minD) _minD = (int)Math.Floor(p.World.Z);
                if (p.World.Z > _maxD) _maxD = (int)Math.Ceiling(p.World.Z);
             }
        } 


        private Vector3 Recover3DPointFromProjection(Vector3 projection, float offset)
        {
            Vector3 unitNormal = Vector3.Normalize(WallNormal);
            return projection + unitNormal * offset; // toward the camera
        }

        private DepthPoint[] GetLayer(float offset)
        {
            DepthPoint[] result = new DepthPoint[4];

            int x, y;
            for (int i = 0; i < 4; i++)
            {
                var point3d = Recover3DPointFromProjection(WallLayer[i].World, offset);
                var point2d = _calibration.Convert3DTo2D(new(point3d.X, point3d.Y, point3d.Z), CalibrationGeometry.Depth, CalibrationGeometry.Color);
                if (point2d.HasValue)
                {
                    x = (int)point2d.Value.X;
                    y = (int)point2d.Value.Y;
                }
                else
                {
                    x = WallLayer[i].SX;
                    y = WallLayer[i].SY;
                }
                result[i] = DepthPoint.From(x, y, point3d.X, point3d.Y, point3d.Z);
            }
            return result;
        }

        public bool IsPointInVolume(Vector3 point)
        {
            // Distance from point to wall plane
            float distanceToPlane = Vector3.Dot(WallNormal, point) + WallDistance;

            if (distanceToPlane < MinOffset || distanceToPlane > MaxOffset)
                return false;

            // Project point onto wall plane
            Vector3 projected = point - WallNormal * distanceToPlane;

            return IsProjectedPointInVolume(projected);
        }

        public bool IsProjectedPointInVolumeWhenWallNotAligned(Vector3 p)
        {
            Vector3 w = p - Corner1.World;       // vector from origin to point

            float wu = Vector3.Dot(w, HorizontalVector);
            float wv = Vector3.Dot(w, VerticalVector);

            float s = (HowAligned * wv - SquaredVertical * wu) / Denom;
            float t = (HowAligned * wu - SquaredHorizontal * wv) / Denom;
            return s >= 0 && s <= 1 && t >= 0 && t <= 1;
        }

        public bool IsProjectedPointInVolumeWhenWallAligned(Vector3 projected)
        {
            // Check if projected point lies within rectangular bounds
            return projected.X >= _minX && projected.X <= _maxX &&
                   projected.Y >= _minY && projected.Y <= _maxY &&
                   projected.Z >= _minZ && projected.Z <= _maxZ;
        }

        public List<Vector3> Extract3DPointsInsideVolume(ushort[] depthImage, CalibrationGeometry calibrationGeometry)
        {
            
            var result = new List<Vector3>();
            int step = 2;
            int w = maxSX - minSX;
            int h = maxSY - minSY;
            var size = w * h;
            lock (_depthLock)
            {
                var localLists = new List<Vector3>[Environment.ProcessorCount];

                Parallel.For(0, localLists.Length, i => localLists[i] = new List<Vector3>());

                Parallel.ForEach(
                    Partitioner.Create(minSY, maxSY),
                    new ParallelOptions { MaxDegreeOfParallelism = localLists.Length },
                    () => new List<Vector3>(),
                    (range, _, localList) =>
                    {
                        for (int y = range.Item1; y < range.Item2; y++)
                        {
                            if ((y - minSY) % step != 0)
                                continue;

                            for (int x = minSX; x < maxSX; x += step)
                            {
                                int index = y * _fw + x;
                                float d = (float)depthImage[index];
                                if (d <= 0 || d < _minD || d > _maxD) continue;

                                var world = _calibration.Convert2DTo3D(new(x, y), d, CalibrationGeometry.Color, CalibrationGeometry.Depth);
                                if (world == null) continue;

                                var vector = new Vector3(world.Value.X, world.Value.Y, world.Value.Z);
                                if (IsPointInVolume(vector)) localList.Add(vector);
                            }
                        }
                        return localList;
                    },
                    localList => {
                        lock (result)
                        {
                            result.AddRange(localList);
                        }
                    });

            }
            return result;
        }
    }

    public static class TouchZoneHelper
    {
        /// <summary>
        /// Calculates the four corners of a parallelogram on a wall plane, given two diagonal points.
        /// </summary>
        /// <param name="wallCorner1">First corner (in screen + world space).</param>
        /// <param name="wallCorner2">Opposite corner (in screen + world space).</param>
        /// <param name="wallNormal">Normal vector of the wall plane.</param>
        /// <param name="calibration">Calibration object to project 3D to 2D.</param>
        /// <param name="corner1">Output corner 1</param>
        /// <param name="corner2">Output corner 2</param>
        /// <param name="corner3">Output corner 3</param>
        /// <param name="corner4">Output corner 4</param>
        public static void ComputeAllFourCorners(
            DepthPoint wallCorner1,
            DepthPoint wallCorner2,
            Vector3 wallNormal,
            Calibration calibration,
            out DepthPoint corner1,
            out DepthPoint corner2,
            out DepthPoint corner3,
            out DepthPoint corner4)
        {
            // Assign corners based on screen X to maintain consistent winding
            if (wallCorner1.SX < wallCorner2.SX)
            {
                corner1 = wallCorner1;
                corner3 = wallCorner2;
            }
            else
            {
                corner3 = wallCorner1;
                corner1 = wallCorner2;
            }

            Vector3 diag = corner3.World - corner1.World;

            // Arbitrary vector not aligned with the normal
            Vector3 arbitrary = Math.Abs(wallNormal.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;

            // Two perpendicular vectors in the plane
            Vector3 dir1 = Vector3.Normalize(Vector3.Cross(wallNormal, arbitrary));
            Vector3 dir2 = Vector3.Normalize(Vector3.Cross(wallNormal, dir1));

            // Project the diagonal onto dir1 and dir2 to find the sides
            float d1 = Vector3.Dot(diag, dir1);
            float d2 = Vector3.Dot(diag, dir2);

            Vector3 half1 = dir1 * (d1 / 2);
            Vector3 half2 = dir2 * (d2 / 2);
            Vector3 center = (corner1.World + corner3.World) / 2;

            // Compute world positions for corner2 and corner4
            Vector3 c2 = center + half1 - half2;
            Vector3 c4 = center - half1 + half2;

            // Project corner2
            var p = calibration.Convert3DTo2D(new(c2.X, c2.Y, c2.Z), CalibrationGeometry.Depth, CalibrationGeometry.Color);
            int x2 = p.HasValue ? (int)p.Value.X : corner3.SX;
            int y2 = p.HasValue ? (int)p.Value.Y : corner1.SY;
            corner2 = DepthPoint.From(x2, y2, c2.X, c2.Y, c2.Z);

            // Project corner4
            p = calibration.Convert3DTo2D(new(c4.X, c4.Y, c4.Z), CalibrationGeometry.Depth, CalibrationGeometry.Color);
            int x4 = p.HasValue ? (int)p.Value.X : corner1.SX;
            int y4 = p.HasValue ? (int)p.Value.Y : corner3.SY;
            corner4 = DepthPoint.From(x4, y4, c4.X, c4.Y, c4.Z);
        }
    }

    public class TouchVolume
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
        private OB.Float3 _uF3, _vF3;
        private float _uu, _vv, _uv, _denom;

        private Vector3 _axisU, _axisV; // local axises on the wall

        private int _minSX, _maxSX, _minSY, _maxSY;
        private ushort _minD, _maxD;

        public Func<OB.Float3, bool> IsProjectedPointInVolume;
        private float _triangleArea012;
        private readonly float _triangleArea023;

        public event Action? WallNotAligned;

        private readonly object _depthLock = new();
        private readonly Calibration _calibration;
        private readonly int _fw, _fh;
        int counter = 0;
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

            counter = 0;

            Polygon = polygon;
            Polygon2D = new Vector2[4];
            MinOffset = minOffset;
            MaxOffset = maxOffset;
            _minD = minWalDepth > maxOffset ? (ushort)(minWalDepth - maxOffset) : ushort.MinValue;
            _maxD = maxWalDepth > minOffset ? (ushort)(maxWalDepth - minOffset) : maxWalDepth;
            _fw = fw;
            _fh = fh;
            _calibration = calibration;

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


            UpdateScreenMinMax(Polygon, minOffset);
            UpdateScreenMinMax(Polygon, maxOffset);
#if DEBUG
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
#if DEBUG
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

        private void UpdateScreenMinMax(DepthPoint[] poly, float offset)
        {
            foreach (var p in poly)
            {
                var shifted = p.World - WallNormal * offset; // point shifted from wall on offset
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

            }
        }

        public bool IsPointInVolume(OB.Float3 point, out Vector2? point2D)
        {
            point2D = new Vector2?();
            float distanceToPlane = WallNormal.X * point.X
                       + WallNormal.Y * point.Y
                       + WallNormal.Z * point.Z
                       + WallDistance;

            if (distanceToPlane > MaxOffset || distanceToPlane < MinOffset)
                return false;

            counter++;

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

        public List<OB.Float3> Extract3DPointsInsideVolume(ushort[] depthImage)
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
        public List<Float2> ExtractProjectedPointsInsideVolume(ushort[] depthImage)
        {
            var result = new List<OB.Float2>();
            int step = 2;
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
                                int index = y * _fw + x;
                                float d = depthImage[index];

                                if (d <= 0 || d < _minD || d > _maxD) continue;

                                var world = _calibration.Convert2DTo3D(new(x, y), d, CalibrationGeometry.Color, CalibrationGeometry.Depth);
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

}

