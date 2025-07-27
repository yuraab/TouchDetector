using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Numerics;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    /// <summary>
    /// Defines a 3D axis-aligned bounding box (AABB) volume.
    /// </summary>
    public class Volume3D
    {
        public Vector3 Min { get; }
        public Vector3 Max { get; }

        public Volume3D(Vector3 corner1, Vector3 corner2)
        {
            Min = Vector3.Min(corner1, corner2);
            Max = Vector3.Max(corner1, corner2);
        }

        /// <summary>
        /// Returns true if the given point is inside the volume.
        /// </summary>
        public bool IsPointInside(Vector3 point)
        {
            return point.X >= Min.X && point.X <= Max.X &&
                   point.Y >= Min.Y && point.Y <= Max.Y &&
                   point.Z >= Min.Z && point.Z <= Max.Z;
        }

        public override string ToString() => $"Volume from {Min} to {Max}";
    }
}
