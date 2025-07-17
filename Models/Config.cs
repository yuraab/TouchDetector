using System.Numerics;

namespace CPRTouchVision.Models
{
    // Class to hold configuration and calibration data for the application.
    internal class Config
    {
        public int? Offset;
        public float? PlaneD;
        public Vector3? PlaneNormal;
        public Vector3? CameraPosition;
        public Quaternion? CameraRotation;
        // public Trampoline[]? Trampolines;

        public Config(int? offset, float? planeD, Vector3? planeNormal, Vector3? cameraPosition, Quaternion? cameraRotation) // , Trampoline[]? trampolines
        {
            // TODO: - Instead of trampolines, use a single rectangle to represent projection touch area
            Offset = offset;
            PlaneD = planeD;
            PlaneNormal = planeNormal;
            CameraPosition = cameraPosition;
            CameraRotation = cameraRotation;
            // Trampolines = trampolines;
        }
    }
}
