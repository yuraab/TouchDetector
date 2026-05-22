using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Timers;
using OB = OBSharp;

namespace CPRTouchVision.Models
{
    public class TouchCluster
    {
        private List<Vector3> _points;
        private Vector3 _center;
        private Vector2 _normalizedCenter;
        private float _radius;
        private int _pointsCount;
        private DateTime _timestamp;
        
        public Vector3 Center => _center;        // 3D center point of the cluster
        public Vector2 NormalizedCenter { get { return _normalizedCenter; } set { _normalizedCenter = value; } }
        public float Radius => _radius;          // Max radius from center
        public int Count => _pointsCount;
        public List<Vector3> Points => _points;

        public DateTime Timestamp         // Time of detection UTC
        {
            get => _timestamp;
            set => _timestamp = value;
        }

        

        public TouchCluster(List<Vector3> points, DateTime? timestamp)
        {

            _points = points;
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

            //_timestamp = timestamp ?? DateTime.UtcNow;
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

            return $"TouchCluster(Center: {Center}, Radius: {Radius:F2})";
        }
    }

    public class TouchClusterManager
    {
        private readonly double _eps;
        private readonly int _minPoints;
        private readonly int _minRadius;
        private readonly int _rateLimitMs;
        private DateTime _lastSentTime = DateTime.MinValue;

        // Optional: noise suppression (per cluster location)
        private readonly List<Vector3> _recentCenters = new();
        private readonly float _minClusterDistance = 10; // millimeters
        private readonly float _mergeThreshold = 0.9f; // Relative to radius sum

        public int MinPoints => _minPoints;

        public TouchClusterManager(double eps = 25, int minPoints = 6, int minRadius = 10, int rateLimitMs = 100)
        {
            _eps = eps;
            _minPoints = minPoints;
            _minRadius = minRadius;
            _rateLimitMs = rateLimitMs;
        }


        public List<TouchCluster> DetectClusters(List<OB.Float3> points, DateTime timestamp)
        {
            // Step 1: Convert input points to DbscanCustomPoint list
            var dbscanPoints = points.Select(p => new DbscanCustomPoint(p.X, p.Y, p.Z)).ToList();

            // Step 2: Create DBSCAN instance with current parameters
            var dbscan = new DbscanCustom(_eps, _minPoints);

            // Step 3: Run DBSCAN clustering
            var clusters = dbscan.Fit(dbscanPoints);

            // Step 4: Convert clusters to your custom TouchCluster class
            var initial = new List<TouchCluster>();

            foreach (var cluster in clusters)
            { 
                if (cluster.Count < _minPoints)
                    continue;

                var touchCluster = new TouchCluster(
                    cluster.Select(p => new Vector3((float)p.Point[0], (float)p.Point[1], (float)p.Point[2])).ToList(),
                    timestamp
                );

                initial.Add(touchCluster);

            }

            return MergeClusters(initial);
        }

        private List<TouchCluster> MergeClusters(List<TouchCluster> clusters)
        {
            bool[] merged = new bool[clusters.Count];
            List<TouchCluster> result = new();

            for (int i = 0; i < clusters.Count; i++)
            {
                if (merged[i]) continue;

                var baseCluster = clusters[i];
                List<TouchCluster> toMerge = new() { baseCluster };
                merged[i] = true;

                for (int j = i + 1; j < clusters.Count; j++)
                {
                    if (merged[j]) continue;

                    var other = clusters[j];
                    float dist = (baseCluster.Center - other.Center).Length();
                    float combinedRadius = baseCluster.Radius + other.Radius;

                    if (dist < combinedRadius * _mergeThreshold)
                    {
                        toMerge.Add(other);
                        merged[j] = true;
                    }
                }

                result.Add(MergeGroup(toMerge));
            }

            return result;
        }
        private TouchCluster MergeGroup(List<TouchCluster> group)
        {
            var allPoints = group.SelectMany(g => g.Points).ToList();
            return new TouchCluster(allPoints, group.First().Timestamp);
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

    public class Touch2DCluster
    {
        private List<Vector2> _points;
        private Vector2 _center;
        private Vector3 _center3D;
        public Vector2 _normalizedCenter;
        private float _radius;
        private int _count;
        private readonly float _width;
        private readonly float _height;

        public float Width => _width;
        public float Height => _height;
        public Vector2 Center => _center;        // 2D center point of the cluster
                // 2D center point of the cluster

        public float Radius => _radius;          // Max radius from center
        public int Count => _count;
        public List<Vector2> Points => _points;

        public Vector2 NormalizedCenter { get { return _normalizedCenter; } set { _normalizedCenter = value; } }
        public Vector3 Center3D { get { return _center3D; } set { _center3D = value; } }
        public Touch2DCluster(List<Vector2> points)
        {
            _points = points;
            _center3D = Vector3.Zero; // this is calculated out of this class
            if (points == null || points.Count == 0)
            {
                _center = Vector2.Zero;
                _normalizedCenter = Vector2.Zero;
                _radius = 0f;
                _width = 0f;
                _height = 0f;
            }
            else
            {
                _center = CalculateCenter();
                (_width, _height, _radius) = CalculateDimensions();
                _count = points.Count;
            }

        }

        private Vector2 CalculateCenter()
        {
            if (_points.Count == 0) return Vector2.Zero;
            Vector2 sum = Vector2.Zero;
            foreach (var p in _points)
                sum += p;
            return sum / _points.Count;
        }

        private (float Width, float Height, float Radius) CalculateDimensions()
        {
            float minX = float.MaxValue;
            float maxX = float.MinValue;

            float minY = float.MaxValue;
            float maxY = float.MinValue;

            float maxDist = 0f;
            foreach (var p in _points)
            {
                var dist = Vector2.Distance(p, _center);
                if (dist > maxDist)
                    maxDist = dist;
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;

                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
            return (maxX - minX + 1, maxY - minY + 1, maxDist);
        }

        public override string ToString()
        {

            return $"TouchCluster(Center: {Center}, Radius: {Radius:F2})";
        }
    }

    public class Touch2DClusterManager
    {
        private readonly double _eps;
        private readonly int _minPoints;
        //private readonly int _minRadius;
        //private readonly int _rateLimitMs;
        //private DateTime _lastSentTime = DateTime.MinValue;

        // Optional: noise suppression (per cluster location)
        //private readonly List<Vector3> _recentCenters = new();
        private readonly float _minClusterRadius = 10; // millimeters
        private readonly float _mergeThreshold = 1.1f; // Relative to radius sum

        public int MinPoints => _minPoints;

        public Touch2DClusterManager(double eps = 30, int minPoints = 4)
        {
            _eps = eps;
            _minPoints = minPoints;
            //_minRadius = minRadius;
            //_rateLimitMs = rateLimitMs;
        }


        public List<Touch2DCluster> DetectClusters(List<OB.Float2> points)
        {
            // Step 1: Convert input points to DbscanCustomPoint list
            var dbscanPoints = points.Select(p => new DbscanCustom2DPoint(p.X, p.Y)).ToList();

            // Step 2: Create DBSCAN instance with current parameters
            var dbscan = new Dbscan2DCustom(_eps, _minPoints);

            // Step 3: Run DBSCAN clustering
            var clusters = dbscan.Fit(dbscanPoints);

            // Step 4: Convert clusters to your custom TouchCluster class
            var initial = new List<Touch2DCluster>();

            foreach (var cluster in clusters)
            {
                if (cluster.Count < _minPoints)
                    continue;

                var touchCluster = new Touch2DCluster(
                    cluster.Select(p => new Vector2((float)p.Point[0], (float)p.Point[1])).ToList()
                );

                initial.Add(touchCluster);

            }
            var mergedClusters = MergeClusters(initial);

            var finalClusters = mergedClusters.Where(c => c.Radius >= _minClusterRadius).ToList(); // Filter clusters by radius threshold 

            return finalClusters;
        }

        private List<Touch2DCluster> MergeClusters(List<Touch2DCluster> clusters)
        {
            bool[] merged = new bool[clusters.Count];
            List<Touch2DCluster> result = new();

            for (int i = 0; i < clusters.Count; i++)
            {
                if (merged[i]) continue;

                //var baseCluster = clusters[i];
                List<Touch2DCluster> toMerge = new() { clusters[i] };
                merged[i] = true;

                for (int j = i + 1; j < clusters.Count; j++)
                {
                    if (merged[j]) continue;

                    //var other = clusters[j];
                    float dist = (clusters[i].Center - clusters[j].Center).Length();
                    float combinedRadius = clusters[i].Radius + clusters[j].Radius;

                    if (dist < combinedRadius * _mergeThreshold)
                    {
                        toMerge.Add(clusters[j]);
                        merged[j] = true;
                    }
                }

                result.Add(MergeGroup(toMerge));
            }

            return result;
        }
        private Touch2DCluster MergeGroup(List<Touch2DCluster> group)
        {
            var allPoints = group.SelectMany(g => g.Points).ToList();
            return new Touch2DCluster(allPoints);
        }

    }

    public sealed class ConnectedComponentClusterManager
    {
        private readonly int _frameWidth;
        private readonly int _frameHeight; 
        private readonly int _minPoints;
        
        public ConnectedComponentClusterManager(
            int frameWidth,
            int frameHeight,
            int minPoints = 4)
        {
            _frameWidth = frameWidth;
            _frameHeight = frameHeight;
            _minPoints = minPoints;
        }


        public List<Touch2DCluster> DetectClusters(
            List<(int X, int Y)> points)
        {
            var result =
                new List<Touch2DCluster>();

            if (points.Count < _minPoints)
                return result;

            //
            // Build foreground lookup
            //

            var foreground =
                new HashSet<int>(points.Count);

            foreach (var p in points)
            {
                foreground.Add(
                    p.Y * _frameWidth + p.X);
            }

            //
            // Visited pixels
            //

            var visited =
                new HashSet<int>(points.Count);

            //
            // BFS
            //

            var queue =
                new Queue<int>();

            foreach (var start in foreground)
            {
                if (!visited.Add(start))
                    continue;

                queue.Clear();
                queue.Enqueue(start);

                var clusterPoints =
                    new List<Vector2>();

                while (queue.Count > 0)
                {
                    int idx =
                        queue.Dequeue();

                    int y =
                        idx / _frameWidth;

                    int x =
                        idx - y * _frameWidth;

                    clusterPoints.Add(
                        new Vector2(x, y));

                    //
                    // 8-connected neighbors
                    //

                    Visit(
                        idx - _frameWidth - 1,
                        foreground,
                        visited,
                        queue);

                    Visit(
                        idx - _frameWidth,
                        foreground,
                        visited,
                        queue);

                    Visit(
                        idx - _frameWidth + 1,
                        foreground,
                        visited,
                        queue);

                    Visit(
                        idx - 1,
                        foreground,
                        visited,
                        queue);

                    Visit(
                        idx + 1,
                        foreground,
                        visited,
                        queue);

                    Visit(
                        idx + _frameWidth - 1,
                        foreground,
                        visited,
                        queue);

                    Visit(
                        idx + _frameWidth,
                        foreground,
                        visited,
                        queue);

                    Visit(
                        idx + _frameWidth + 1,
                        foreground,
                        visited,
                        queue);
                }

                if (clusterPoints.Count >= _minPoints)
                {
                    result.Add(
                        new Touch2DCluster(
                            clusterPoints));
                }
            }

            return result;
        }

        public List<Touch2DCluster> DetectClusters(
            List<int> points)
        {
            var result =
                new List<Touch2DCluster>();

            if (points.Count < _minPoints)
                return result;

            var foreground =
                new HashSet<int>(points);

            //
            // Visited pixels
            //

            var visited =
                new HashSet<int>(points.Count);

            //
            // BFS
            //

            var queue =
                new Queue<int>();

            foreach (var start in foreground)
            {
                if (!visited.Add(start))
                    continue;

                queue.Clear();
                queue.Enqueue(start);

                var clusterPoints =
                    new List<Vector2>();

                while (queue.Count > 0)
                {
                    int idx =
                        queue.Dequeue();

                    int x =
                        idx % _frameWidth;

                    int y =
                        idx / _frameWidth;

                    clusterPoints.Add(
                        new Vector2(x, y));

                    //
                    // 8-connected neighbors
                    //

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = y + dy;

                        if (ny < 0 ||
                            ny >= _frameHeight)
                        {
                            continue;
                        }

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;

                            if (dx == 0 && dy == 0)
                                continue;

                            if (nx < 0 ||
                                nx >= _frameWidth)
                            {
                                continue;
                            }

                            int neighbor =
                                ny * _frameWidth + nx;

                            if (!foreground.Contains(neighbor))
                                continue;

                            if (!visited.Add(neighbor))
                                continue;

                            queue.Enqueue(neighbor);
                        }
                    }
                }

                if (clusterPoints.Count >= _minPoints)
                {
                    result.Add(
                        new Touch2DCluster(
                            clusterPoints));
                }
            }


            return result;
        }


        private static void Visit(
            int neighbor,
            HashSet<int> foreground,
            HashSet<int> visited,
            Queue<int> queue)
        {
            if (!foreground.Contains(neighbor))
                return;

            if (!visited.Add(neighbor))
                return;

            queue.Enqueue(neighbor);
        }
    }



}