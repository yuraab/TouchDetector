using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Timers;


namespace CPRTouchVision.Models
{
    public class TouchCluster
    {

        private Vector3 _center;
        private float _radius;
        private int _pointsCount;
        private long _timestampMicroseconds;

        public Vector3 Center => _center;        // 3D center point of the cluster
        public float Radius => _radius;          // Max radius from center
        public int Count => _pointsCount;
        public long TimestampMicroseconds         // Time of detection UTC
        {
            get => _timestampMicroseconds;
            set => _timestampMicroseconds = value;
        }

        

        public TouchCluster(List<Vector3> points, long timestamp = 0)
        {
            if (points == null || points.Count == 0)
            {
                _center = Vector3.Zero;
                _radius = 0f;
            }
            else
            {
                _center = CalculateCenter(points);
                _radius = CalculateRadius(points, Center);
                _pointsCount = points.Count;
            }

            _timestampMicroseconds = timestamp;
        }

        private Vector3 CalculateCenter(List<Vector3> points)
        {
            if (points.Count == 0) return Vector3.Zero;
            Vector3 sum = Vector3.Zero;
            foreach (var p in points)
                sum += p;
            return sum / points.Count;
        }

        private float CalculateRadius(List<Vector3> points, Vector3 center)
        {
            float maxDist = 0f;
            foreach (var p in points)
            {
                var dist = Vector3.Distance(p, center);
                if (dist > maxDist)
                    maxDist = dist;
            }
            return maxDist;
        }

        public override string ToString()
        {
            var milliseconds = _timestampMicroseconds / 1000;
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToLocalTime();
            return $"TouchCluster(Center: {Center}, Radius: {Radius:F2}, Timestamp: {dt:yyyy-MM-dd HH:mm:ss.fff})";
        }
    }

    public class TouchClusterManager
    {
        private readonly double _eps;
        private readonly int _minPoints;
        private readonly int _rateLimitMs;
        private DateTime _lastSentTime = DateTime.MinValue;

        // Optional: noise suppression (per cluster location)
        private readonly List<Vector3> _recentCenters = new();
        private readonly float _minClusterDistance = 10; // millimeters



        public TouchClusterManager(double eps = 40, int minPoints = 10, int rateLimitMs = 100)
        {
            _eps = eps;
            _minPoints = minPoints;
            _rateLimitMs = rateLimitMs;
        }


        public List<TouchCluster> DetectClusters(List<Vector3> points, long timestamp = 0)
        {
            // Step 1: Convert input points to DbscanCustomPoint list
            var dbscanPoints = points.Select(p => new DbscanCustomPoint(p.X, p.Y, p.Z)).ToList();

            // Step 2: Create DBSCAN instance with current parameters
            var dbscan = new DbscanCustom(_eps, _minPoints);

            // Step 3: Run DBSCAN clustering
            var clusters = dbscan.Fit(dbscanPoints);

            // Step 4: Convert clusters to your custom TouchCluster class
            var result = new List<TouchCluster>();

            foreach (var clusterPoints in clusters)
            { 
                if (clusterPoints.Count < _minPoints)
                    continue;

                var touchCluster = new TouchCluster(
                    clusterPoints.Select(p => new Vector3((float)p.Point[0], (float)p.Point[1], (float)p.Point[2])).ToList()
                );
                result.Add(touchCluster);
            }
            /*
            foreach (var clusterPoints in clusters)
            {
                if (clusterPoints.Count == 0)
                    continue;

                var touchCluster = new TouchCluster
                {
                    Timestamp = timestamp,
                    Points = clusterPoints.Select(p => new Vector3((float)p.Point[0], (float)p.Point[1], (float)p.Point[2])).ToList()
                };

                touchCluster.CalculateProperties(); // e.g., Center, Radius, BoundingBox, ConvexHull etc.
                result.Add(touchCluster);
            }
            */
            return result;
        }
        public List<TouchCluster> DetectClustersOld(List<Vector3> points, long timestamp = 0)
        {
            
            var dbscanPoints = points.Select((p, i) => new DbscanCustomPointOld
            {
                Id = i,
                Point = new double[] {p.X, p.Y, p.Z }
            }).ToList();
           
            
            var dbscan = new DbscanCustomOld(_eps, _minPoints);
            var clusters = dbscan.Fit(dbscanPoints);

            var result = new List<TouchCluster>();

            foreach (var cluster in clusters)
            {
                var clusterPoints = cluster.Select(dp => points[dp.Id]).ToList();
                if (clusterPoints.Count < _minPoints)
                    continue;

                result.Add(new TouchCluster(clusterPoints, timestamp));
            }

            return result;
        }


        private bool IsNewCluster(Vector3 center)
        {
            foreach (var prev in _recentCenters)
            {
                if (Vector3.Distance(prev, center) < _minClusterDistance)
                    return false; // too close to previous cluster
            }

            // Keep only last few recent cluster centers
            if (_recentCenters.Count > 10)
                _recentCenters.RemoveAt(0);

            return true;
        }
        public List<Vector3> FilterTouchPoints(List<Vector3> rawPoints, TouchVolume touchVolume)
        {
            var result = new List<Vector3>();

            foreach (var pt in rawPoints)
            {
                if (touchVolume.IsPointInVolume(pt))
                    result.Add(pt);
            }

            return result;
        }

    }

    public class DbscanCustomPointOld
    {
        public int Id;
        public double[] Point;
        public int ClusterId = -1;
        public bool Visited = false;
    }

    public class DbscanCustomOld
    {
        private readonly double _eps;
        private readonly int _minPts;

        record GridKey(int X, int Y, int Z);
        Dictionary<GridKey, List<DbscanCustomPointOld>> _grid;

        public DbscanCustomOld(double eps, int minPts)
        {
            _eps = eps;
            _minPts = minPts;
        }

        private Dictionary<GridKey, List<DbscanCustomPointOld>> BuildGrid(List<DbscanCustomPointOld> points, double eps)
        {
            var grid = new Dictionary<GridKey, List<DbscanCustomPointOld>>();

            foreach (var p in points)
            {
                var key = GetGridKey(p.Point, eps);
                if (!grid.ContainsKey(key))
                    grid[key] = new List<DbscanCustomPointOld>();

                grid[key].Add(p);
            }

            return grid;
        }

        private GridKey GetGridKey(double[] point, double eps)
        {
            return new GridKey(
                (int)Math.Floor(point[0] / eps),
                (int)Math.Floor(point[1] / eps),
                (int)Math.Floor(point[2] / eps)
            );
        }

        private List<DbscanCustomPointOld> RegionQuery(Dictionary<GridKey, List<DbscanCustomPointOld>> grid, DbscanCustomPointOld point, double eps)
        {
            var neighbors = new List<DbscanCustomPointOld>();
            var baseKey = GetGridKey(point.Point, eps);

            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var key = new GridKey(baseKey.X + dx, baseKey.Y + dy, baseKey.Z + dz);
                        if (grid.TryGetValue(key, out var bucket))
                        {
                            foreach (var candidate in bucket)
                            {
                                if (Distance(candidate.Point, point.Point) <= eps)
                                    neighbors.Add(candidate);
                            }
                        }
                    }

            return neighbors;
        }
        public List<List<DbscanCustomPointOld>> Fit(List<DbscanCustomPointOld> points)
        {
            int clusterId = 0;
            var clusters = new List<List<DbscanCustomPointOld>>();
            var grid = BuildGrid(points, _eps);

            foreach (var point in points)
            {
                if (point.Visited)
                    continue;

                point.Visited = true;
                var neighbors = RegionQuery(grid, point, _eps);

                if (neighbors.Count < _minPts)
                {
                    point.ClusterId = -1; // noise
                }
                else
                {
                    var cluster = new List<DbscanCustomPointOld>();
                    ExpandCluster(grid, point, neighbors, cluster, clusterId);
                    clusters.Add(cluster);
                    clusterId++;
                }
            }

            return clusters;
        }
        public List<List<DbscanCustomPoint>> FitRough(List<DbscanCustomPoint> points)
        {
            int clusterId = 0;
            var clusters = new List<List<DbscanCustomPoint>>();

            foreach (var point in points)
            {
                if (point.Visited)
                    continue;

                point.Visited = true;
                var neighbors = RegionQuery(points, point);

                if (neighbors.Count < _minPts)
                {
                    point.ClusterId = -1; // noise
                }
                else
                {
                    var cluster = new List<DbscanCustomPoint>();
                    ExpandClusterRough(points, point, neighbors, cluster, clusterId);
                    clusters.Add(cluster);
                    clusterId++;
                }
            }

            return clusters;
        }

        private void ExpandCluster(
            Dictionary<GridKey, List<DbscanCustomPointOld>> grid,
            DbscanCustomPointOld point,
            List<DbscanCustomPointOld> neighbors,
            List<DbscanCustomPointOld> cluster,
            int clusterId)
        {
            point.ClusterId = clusterId;
            cluster.Add(point);

            for (int i = 0; i < neighbors.Count; i++)
            {
                var n = neighbors[i];

                if (!n.Visited)
                {
                    n.Visited = true;
                    var nNeighbors = RegionQuery(grid, n, _eps);
                    if (nNeighbors.Count >= _minPts)
                        neighbors.AddRange(nNeighbors.Where(nn => !neighbors.Contains(nn)));
                }

                if (n.ClusterId == -1)
                {
                    n.ClusterId = clusterId;
                    cluster.Add(n);
                }
            }
        }


        private void ExpandClusterRough(List<DbscanCustomPoint> points, DbscanCustomPoint point, List<DbscanCustomPoint> neighbors, List<DbscanCustomPoint> cluster, int clusterId)
        {
            point.ClusterId = clusterId;
            cluster.Add(point);

            for (int i = 0; i < neighbors.Count; i++)
            {
                var neighbor = neighbors[i];
                if (!neighbor.Visited)
                {
                    neighbor.Visited = true;
                    var neighborNeighbors = RegionQuery(points, neighbor);
                    if (neighborNeighbors.Count >= _minPts)
                    {
                        neighbors.AddRange(neighborNeighbors.Where(n => !neighbors.Contains(n)));
                    }
                }

                if (neighbor.ClusterId == -1)
                {
                    neighbor.ClusterId = clusterId;
                    cluster.Add(neighbor);
                }
            }
        }

        private List<DbscanCustomPoint> RegionQuery(List<DbscanCustomPoint> points, DbscanCustomPoint center)
        {

            return points.Where(p =>
                Distance(p.Point, center.Point) <= _eps
            ).ToList();
        }

        private double Distance(double[] a, double[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double d = a[i] - b[i];
                sum += Math.Pow(d,2);
            }
            return Math.Sqrt(sum);
        }


    }
}