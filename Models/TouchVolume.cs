using OBSharp.Sensor;
using OpenCvSharp;
using System;
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
    public class TouchVolume

    {
        public Vector3 WallNormal { get; }
        public float WallDistance { get; }

        public float MinOffset { get; }
        public float MaxOffset { get; }

        public DepthPoint Corner1 { get; }
        public DepthPoint Corner2 { get; set; }
        public DepthPoint Corner3 { get; }
        public DepthPoint Corner4 { get; set; }

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

        public TouchVolume(Vector3 wallNormal, float wallDistance,
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

            // Store corners of the working area (rectangle on wall plane) on clockwise
            if (wallCorner1.SX < wallCorner2.SX)
            {
                Corner1 = wallCorner1;
                Corner3 = wallCorner2;
            }
            else
            {
                Corner3 = wallCorner1;
                Corner1 = wallCorner2;
            }

            //alculate Corner2 and Corner$
            Vector3 diag = Corner3.World - Corner1.World;
            // Pick any vector not parallel to normal
            Vector3 arbitrary = Math.Abs(WallNormal.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            // First direction in the plane
            Vector3 dir1 = Vector3.Normalize(Vector3.Cross(WallNormal, arbitrary));

            // Second direction orthogonal to both normal and dir1
            Vector3 dir2 = Vector3.Normalize(Vector3.Cross(WallNormal, dir1));

            //project diag onto dir1 and dir2
            float d1 = Vector3.Dot(diag, dir1);
            float d2 = Vector3.Dot(diag, dir2);

            Vector3 half1 = dir1 * (d1 / 2);
            Vector3 half2 = dir2 * (d2 / 2);

            Vector3 center = (Corner1.World + Corner3.World) / 2;

            Vector3 c2 = center + half1 - half2;
            Vector3 c4 = center - half1 + half2;

            // Calculate 2D projection of Corner2
            var p = _calibration.Convert3DTo2D(new(c2.X, c2.Y, c2.Z), CalibrationGeometry.Depth, CalibrationGeometry.Color);
            int x, y;
            if (p.HasValue)
            {
                x = (int)p.Value.X;
                y = (int)p.Value.Y;
            }
            else
            {
                // assume from corner1 and corner3
                x = Corner3.SX;
                y = Corner1.SY;
            }
            Corner2 = DepthPoint.From(x, y, c2.X, c2.Y, c2.Z);

            // Calculate 2D projection of Corner4
            p = _calibration.Convert3DTo2D(new(c4.X, c4.Y, c4.Z), CalibrationGeometry.Depth, CalibrationGeometry.Color);
            if (p.HasValue)
            {
                x = (int)p.Value.X;
                y = (int)p.Value.Y;
            }
            else
            {
                // assume from corner1 and corner3
                x = Corner1.SX;
                y = Corner3.SY;
            }
            Corner4 = DepthPoint.From(x, y, c4.X, c4.Y, c4.Z);
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
            /*
            int sx = Corner3.SX; 
            int sy = Corner1.SY;
            Vector3 p = _calibration.Convert2DTo3D(new(sx, sy), CalibrationGeometry.Color, CalibrationGeometry.Depth);
            Corner2 = DepthPoint.From(sx, sy, p.X, p.Y, p.Z);

            int sx = Corner1.SX;
            int sy = Corner3.SY;
            Vector3 p = _calibration.Convert2DTo3D(new(sx, sy), CalibrationGeometry.Color, CalibrationGeometry.Depth);
            Corner4 = DepthPoint.From(sx, sy, p.X, p.Y, p.Z);
            */
            // Otionally precompute bounds for faster volume checks

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
                App.Log($"Layer indexe: X => {p.SX}   Y => {p.SY}");
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

                Parallel.For(minSY, maxSY, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, y =>
                {
                    if ((y - minSY) % step != 0)
                        return;
                    for (int x = minSX; x < maxSX; x += step)
                    {
                        int index = y * _fw + x;
                        float d = (float)depthImage[index];
                        if (d <= 0 || d < _minD || d > _maxD) continue;

                        var world = _calibration.Convert2DTo3D(new(x, y), d, CalibrationGeometry.Color, CalibrationGeometry.Depth);
                        if (world == null) continue;

                        var vector = new Vector3(
                            world.Value.X,
                            world.Value.Y,
                            world.Value.Z
                        );
                        if (IsPointInVolume(vector)) result.Add(vector);
                    }
                });
                
            }
            return result;
        }
    }
}

