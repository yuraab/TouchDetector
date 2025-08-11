using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    public class SpatialGridIndex
    {
        private readonly double _cellSize;
        private readonly Dictionary<(int, int, int), List<DbscanCustomPoint>> _grid = new();

        public SpatialGridIndex(IEnumerable<DbscanCustomPoint> points, double cellSize)
        {
            _cellSize = cellSize;

            foreach (var p in points)
            {
                var key = GetKey(p.Point);
                if (!_grid.TryGetValue(key, out var list))
                {
                    list = new List<DbscanCustomPoint>();
                    _grid[key] = list;
                }
                list.Add(p);
            }
        }

        public List<DbscanCustomPoint> GetNeighbors(DbscanCustomPoint point, double eps)
        {
            var key = GetKey(point.Point);
            var neighbors = new List<DbscanCustomPoint>();

            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var neighborKey = (key.Item1 + dx, key.Item2 + dy, key.Item3 + dz);
                        if (_grid.TryGetValue(neighborKey, out var cellPoints))
                        {
                            foreach (var p in cellPoints)
                            {
                                if (Distance(point.Point, p.Point) <= eps)
                                    neighbors.Add(p);
                            }
                        }
                    }

            return neighbors;
        }

        private (int, int, int) GetKey(double[] point)
        {
            return (
                (int)Math.Floor(point[0] / _cellSize),
                (int)Math.Floor(point[1] / _cellSize),
                (int)Math.Floor(point[2] / _cellSize)
            );
        }

        private double Distance(double[] a, double[] b)
        {
            double dx = a[0] - b[0];
            double dy = a[1] - b[1];
            double dz = a[2] - b[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
    public class Spatial2DGridIndex
    {
        private readonly double _cellSize;
        private readonly Dictionary<(int, int), List<DbscanCustom2DPoint>> _grid = new();

        public Spatial2DGridIndex(IEnumerable<DbscanCustom2DPoint> points, double cellSize)
        {
            _cellSize = cellSize;

            foreach (var p in points)
            {
                var key = GetKey(p.Point);
                if (!_grid.TryGetValue(key, out var list))
                {
                    list = new List<DbscanCustom2DPoint>();
                    _grid[key] = list;
                }
                list.Add(p);
            }
        }

        public List<DbscanCustom2DPoint> GetNeighbors(DbscanCustom2DPoint point, double eps, double epsSquared)
        {
            var key = GetKey(point.Point);
            var neighbors = new List<DbscanCustom2DPoint>();

            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var neighborKey = (key.Item1 + dx, key.Item2 + dy);
                    if (_grid.TryGetValue(neighborKey, out var cellPoints))
                    {
                        foreach (var p in cellPoints)
                        {
                            if (DistanceSquared(point.Point, p.Point) <= epsSquared) // Squared values are used to avoid extra Sqrt calculations
                                neighbors.Add(p);
                        }
                    }
                }

            return neighbors;
        }

        private (int, int) GetKey(double[] point)
        {
            return (
                (int)Math.Floor(point[0] / _cellSize),
                (int)Math.Floor(point[1] / _cellSize)
            );
        }

        private double DistanceSquared(double[] a, double[] b)
        {
            double dx = a[0] - b[0];
            double dy = a[1] - b[1];
            return (dx * dx + dy * dy);
        }
    }
}
