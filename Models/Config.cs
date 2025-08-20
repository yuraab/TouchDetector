using System.Numerics;

namespace CPRTouchVision.Models
{
    // Class to hold configuration and calibration data for the application.
    internal class Config
    {
        public int? GameScreenWidth;
        public int? GameScreenHeight;
        public int? MinOffset;
        public int? MaxOffset;
        public ushort? MinWallDepth;
        public ushort? MaxWallDepth;
        public float? PlaneD;
        public Vector3? PlaneNormal;
        public DepthPoint[]? TouchZoneCorners;
        public Vector3? CameraPosition;
        public Quaternion? CameraRotation;
        public bool? FloorCamera;
        public ExclusionZone[]? ExclusionZones;

        public Config(
            int? gameScreenWidth,
            int? gameScreenHeight,
            int? minOffset, 
            int? maxOffset,
            ushort? minWallDepth,
            ushort? maxWallDepth,
            float? planeD,
            Vector3? planeNormal,
            //DepthPoint? touchZoneCorner1,
            //DepthPoint? touchZoneCorner2,
            DepthPoint[] touchZoneCorners,
            Vector3? cameraPosition,
            Quaternion? cameraRotation,
            bool? floorCamera,
            ExclusionZone[]? exclusionZones
        )

        {
            GameScreenWidth = gameScreenWidth;
            GameScreenHeight = gameScreenHeight;
            MinOffset = minOffset;
            MaxOffset = maxOffset;
            MinWallDepth = minWallDepth;
            MaxWallDepth = maxWallDepth;
            PlaneD = planeD;
            PlaneNormal = planeNormal;
            //TouchZoneCorner1 = touchZoneCorner1;
            //TouchZoneCorner2 = touchZoneCorner2;
            TouchZoneCorners = touchZoneCorners;
            CameraPosition = cameraPosition;
            CameraRotation = cameraRotation;
            FloorCamera = floorCamera;
            ExclusionZones = exclusionZones;
        }

    }
}
