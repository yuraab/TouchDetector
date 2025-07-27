//using Dbscan;
using Emgu.CV;
using OBSharp;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Timers;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
/*
    public class DbscanPoint : IPointData
    {
        public int Id { get; set; }

        public Dbscan.Point Point { get; set; }
    }
*/
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
            
            var dbscanPoints = points.Select((p, i) => new DbscanCustomPoint
            {
                Id = i,
                Point = new double[] {p.X, p.Y, p.Z }
            }).ToList();
           
            
            var dbscan = new DbscanCustom(_eps, _minPoints);
            var clusters = dbscan.Fit(dbscanPoints);
           /*
            //var dbscanPoints = points.Select(p => new double[] { p.X, p.Y, p.Z });
            double[][] points3D = points.Select(p => new double[] { p.X, p.Y, p.Z }).ToArray();
            var clusters = DbscanCustom.Fit(
                            points3D,
                            epsilon: 1.0,
                            minimumPointsPerCluster: 4);
            */
            var result = new List<TouchCluster>();

            foreach (var cluster in clusters)
            {
                var clusterPoints = cluster.Select(dp => points[dp.Id]).ToList();
                if (clusterPoints.Count == 0)
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

    public class DbscanCustomPoint
    {
        public int Id;
        public double[] Point;
        public int ClusterId = -1;
        public bool Visited = false;
    }

    public class DbscanCustom
    {
        private readonly double _eps;
        private readonly int _minPts;

        public DbscanCustom(double eps, int minPts)
        {
            _eps = eps;
            _minPts = minPts;
        }

        public List<List<DbscanCustomPoint>> Fit(List<DbscanCustomPoint> points)
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
                    ExpandCluster(points, point, neighbors, cluster, clusterId);
                    clusters.Add(cluster);
                    clusterId++;
                }
            }

            return clusters;
        }

        private void ExpandCluster(List<DbscanCustomPoint> points, DbscanCustomPoint point, List<DbscanCustomPoint> neighbors, List<DbscanCustomPoint> cluster, int clusterId)
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

        private List<DbscanCustomPoint> RegionQuery(List<DbscanCustomPoint> points, DbscanCustomPoint point)
        {
            var neighbors = new List<DbscanCustomPoint>();
            foreach (var p in points)
            {
                if (Distance(point.Point, p.Point) <= _eps)
                {
                    neighbors.Add(p);
                }
            }
            return neighbors;
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