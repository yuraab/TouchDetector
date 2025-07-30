using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    public class DbscanCustom
    {
        private readonly double _eps;
        private readonly int _minPts;
        private readonly object _lock = new();

        public DbscanCustom(double eps, int minPts)
        {
            _eps = eps;
            _minPts = minPts;
        }

        public List<List<DbscanCustomPoint>> Fit(List<DbscanCustomPoint> points)
        {
            int clusterId = 0;
            var clusters = new ConcurrentBag<List<DbscanCustomPoint>>();
            var index = new SpatialGridIndex(points, _eps);
            var visited = new ConcurrentDictionary<DbscanCustomPoint, byte>();

            Parallel.ForEach(points, point =>
            {
                if (visited.ContainsKey(point))
                    return;

                lock (point)
                {
                    if (!visited.TryAdd(point, 0))
                        return;

                    var neighbors = index.GetNeighbors(point, _eps);
                    if (neighbors.Count < _minPts)
                    {
                        point.ClusterId = -1; // noise
                        return;
                    }

                    var cluster = new List<DbscanCustomPoint>();
                    int assignedClusterId;

                    lock (_lock)
                    {
                        assignedClusterId = clusterId++;
                    }

                    ExpandCluster(point, neighbors, cluster, assignedClusterId, index, visited);
                    clusters.Add(cluster);
                }
            });

            return clusters.ToList();
        }

        private void ExpandCluster(
            DbscanCustomPoint point,
            List<DbscanCustomPoint> neighbors,
            List<DbscanCustomPoint> cluster,
            int clusterId,
            SpatialGridIndex index,
            ConcurrentDictionary<DbscanCustomPoint, byte> visited)
        {
            point.ClusterId = clusterId;
            cluster.Add(point);

            var neighborQueue = new Queue<DbscanCustomPoint>(neighbors);

            while (neighborQueue.Count > 0)
            {
                var current = neighborQueue.Dequeue();

                if (!visited.ContainsKey(current))
                {
                    visited.TryAdd(current, 0);
                    var currentNeighbors = index.GetNeighbors(current, _eps);
                    if (currentNeighbors.Count >= _minPts)
                    {
                        foreach (var n in currentNeighbors)
                            neighborQueue.Enqueue(n);
                    }
                }

                if (current.ClusterId == null || current.ClusterId == -1)
                {
                    current.ClusterId = clusterId;
                    cluster.Add(current);
                }
            }
        }
    }

}
