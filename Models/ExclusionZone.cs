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
        private readonly float _clusterMargin = 10f;
        private readonly float _clusterMarginFactor = 1.1f;
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

        public void AddZones(List<ExclusionZone> zones)
        {
            foreach (var zone in zones)
            {
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

        public bool IsExcluded(Touch2DCluster cluster)
        {
            foreach (var z in NearbyZones(cluster.Center))
            {
                float dx = cluster.Center.X - z.Center.X;
                float dy = cluster.Center.Y - z.Center.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                // Exclude if cluster overlaps zone (with optional safety margin)
                if (dist < z.Radius + Math.Max(cluster.Radius * _clusterMarginFactor, cluster.Radius + _clusterMargin))// safety margin
                    return true;
            }
            return false;
        }

        public IEnumerable<Touch2DCluster> FilterClusters(IEnumerable<Touch2DCluster> clusters)
        {
            foreach (var c in clusters)
            {
                if (!IsExcluded(c))
                    yield return c;
            }
        }
    }

    public class ExclusionZoneManager_
    {
        private readonly List<ExclusionZone> _zones;
        private readonly float _cellSize;
        private readonly float _clusterMargin = 10f;
        private readonly float _clusterMarginFactor = 1.1f;
        public ExclusionZoneManager_(List<ExclusionZone> zones)
        {
            _zones = zones;
        }
        public ExclusionZoneManager_()
        {
            _zones = new List<ExclusionZone> ();
        }

        public void AddZones(List<ExclusionZone>? zones)
        {
            if (zones == null || zones.Count == 0) return;
            _zones.AddRange(zones);
        }   
        public void ClearZones() => _zones.Clear();
        public List<Touch2DCluster> FilterClusters(List<Touch2DCluster> clusters)
        {

            if (_zones.Count == 0) return clusters;

            List<Touch2DCluster> filtered = new List<Touch2DCluster>();

            foreach (var cluster in clusters)
            {
                bool excluded = false;
                foreach (var z in _zones)
                {
                    float dx = cluster.Center.X - z.Center.X;
                    float dy = cluster.Center.Y - z.Center.Y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    // Check for circle intersection
                    if (dist < cluster.Radius + z.Radius + Math.Max(cluster.Radius * _clusterMarginFactor, cluster.Radius + _clusterMargin)) // optional: + safetyMargin
                    {
                        excluded = true;
                        break; // no need to check other zones
                    }
                }

                if (!excluded)
                {
                    filtered.Add(cluster);
                }
            }
            return filtered;
        }
    }
}
