using OBSharp.Sensor;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using OB = OBSharp;

namespace CPRTouchVision.Models
{
    public class TouchVolume

    {
        public Vector3 WallNormal { get; }
        public float WallDistance { get; }

        public float MinOffset { get; }
        public float MaxOffset { get; }

        public Vector3 Corner1 { get; }
        public Vector3 Corner2 { get; }

        public TouchVolume(Vector3 wallNormal, float wallDistance,
                           float minOffset, float maxOffset,
                           Vector3 wallCorner1, Vector3 wallCorner2)
        {
            WallNormal = Vector3.Normalize(wallNormal);
            WallDistance = wallDistance;

            MinOffset = minOffset;
            MaxOffset = maxOffset;

            // Store corners of the working area (rectangle on wall plane)
            Corner1 = wallCorner1;
            Corner2 = wallCorner2;

            // You can optionally precompute bounds for faster volume checks
            _minX = MathF.Min(Corner1.X, Corner2.X);
            _maxX = MathF.Max(Corner1.X, Corner2.X);
            _minY = MathF.Min(Corner1.Y, Corner2.Y);
            _maxY = MathF.Max(Corner1.Y, Corner2.Y);
            _minZ = MathF.Min(Corner1.Z, Corner2.Z);
            _maxZ = MathF.Max(Corner1.Z, Corner2.Z);
        }

        private readonly float _minX, _maxX, _minY, _maxY, _minZ, _maxZ;

        /// <summary>
        /// Checks if a given point lies inside the touchable volume.
        /// </summary>
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
    }

}

