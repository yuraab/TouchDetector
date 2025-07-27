using System.Numerics;

namespace CPRTouchVision.Models
{
    // Class to hold configuration and calibration data for the application.
    internal class Config
    {
        public int? MinOffset;
        public int? MaxOffset;
        public float? PlaneD;
        public Vector3? PlaneNormal;
        public DepthPoint? TouchZoneCorner1;
        public DepthPoint? TouchZoneCorner2;
        public Vector3? CameraPosition;
        public Quaternion? CameraRotation;


        public Config(
            int? minOffset, 
            int? maxOffset, 
            float? planeD, 
            Vector3? planeNormal,
            DepthPoint? touchZoneCorner1,
            DepthPoint? touchZoneCorner2,
            Vector3? cameraPosition, 
            Quaternion? cameraRotation
        )

        {
            MinOffset = minOffset;
            MaxOffset = maxOffset; 
            PlaneD = planeD;
            PlaneNormal = planeNormal;
            TouchZoneCorner1 = touchZoneCorner1;
            TouchZoneCorner2 = touchZoneCorner2;
            CameraPosition = cameraPosition;
            CameraRotation = cameraRotation;
        }
    }
}
