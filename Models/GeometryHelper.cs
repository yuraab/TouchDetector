using System;
using System.Collections.Generic;
using System.Numerics;

namespace CPRTouchVision.Models
{
    internal static class GeometryHelper
    {
        /// <summary>
        /// Determines if the specified point is inside the polygon.
        /// The polygon is represented as an ordered list of SKPoints.
        /// The algorithm uses ray-casting (crossing number) method.
        /// </summary>
        /// <param name="polygon">List of SKPoints defining the polygon vertices (closed or open, but vertices should be ordered).</param>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is inside the polygon; otherwise, false.</returns>
        public static bool IsPointInPolygon(IList<Vector3> polygon, Vector3 point)
        {
            bool inside = false;
            int count = polygon.Count;
            // Use the ray-casting algorithm: cast a horizontal ray from the point to the right
            // and count how many times it intersects with polygon edges.
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                // Check if the point's Y is between the Y values of the edge endpoints.
                bool intersect = ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                                 (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X);
                if (intersect)
                    inside = !inside;
            }
            return inside;
        }
    }
}
