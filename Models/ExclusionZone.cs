using Dbscan;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    public class ExclusionZone
    {
        public Vector2 Center { get; }
        public float Radius { get; }

        public ExclusionZone(Vector2 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public bool Contains(Vector2 point)
        {
            return Vector2.Distance(Center, point) <= Radius;
        }

        public bool Overlaps(Touch2DCluster cluster)
        {
            return Vector2.Distance(Center, cluster.Center) <= (Radius + cluster.Radius);
        }
    }

    public class ExclusionZoneManager
    {
        private readonly Dictionary<(int, int), List<ExclusionZone>> _grid = new();
        private readonly float _cellSize;

        public ExclusionZoneManager(float cellSize = 50f) // tune based on touch scale
        {
            _cellSize = cellSize;
        }

        private (int, int) ToCell(Vector2 pos)
        {
            int cx = (int)MathF.Floor(pos.X / _cellSize);
            int cy = (int)MathF.Floor(pos.Y / _cellSize);
            return (cx, cy);
        }

        public void AddZones(IEnumerable<Touch2DCluster> clusters)
        {
            foreach (var c in clusters)
            {
                var zone = new ExclusionZone(c.Center, c.Radius);
                var cell = ToCell(zone.Center);

                if (!_grid.TryGetValue(cell, out var list))
                {
                    list = new List<ExclusionZone>();
                    _grid[cell] = list;
                }

                list.Add(zone);
            }
        }

        public void ClearZones() => _grid.Clear();

        private IEnumerable<ExclusionZone> NearbyZones(Vector2 pos)
        {
            var (cx, cy) = ToCell(pos);

            // check current cell + 8 neighbors
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    var key = (cx + dx, cy + dy);
                    if (_grid.TryGetValue(key, out var list))
                        foreach (var z in list)
                            yield return z;
                }
            }
        }

        public bool IsExcluded(Vector2 point)
        {
            foreach (var z in NearbyZones(point))
            {
                if (z.Contains(point))
                    return true;
            }
            return false;
        }

        public IEnumerable<Touch2DCluster> FilterClusters(IEnumerable<Touch2DCluster> clusters)
        {
            foreach (var c in clusters)
            {
                bool excluded = NearbyZones(c.Center)
                    .Any(z => z.Overlaps(c));
#if DEBUG
                if (excluded)
                    App.Log($"Cluster at {c.Center} is excluded");
#endif
                if (!excluded)
                    yield return c;
            }
        }
    }



}
