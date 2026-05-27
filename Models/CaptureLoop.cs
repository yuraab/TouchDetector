using OBSharp.Sensor;
using System;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    internal class CaptureLoop : IDisposable
    {
        private Device _device;
        private DeviceConfiguration _config;
        private bool _isIMURunning = false;
        private Vector3 _gyroVector = Vector3.Zero;
        private Vector3 _accelVector = Vector3.Zero;
        private int _imuIndex = 0;
        private readonly int _requiredSamples = 30;

        protected readonly Thread _thread;
        protected volatile bool _isRunning = false;

        public bool ShouldCollectIMU = false;
        private bool _shouldGetCameraPosition = false;
        private Vector3 _accelSum;
        private int _sampleCount = 0;
        private object _lock;
        private bool isCalibrationImageSaved = false;

        public event EventHandler<CaptureLoopEventArgs>? CaptureReady;
        public event EventHandler<LoopFailedEventArgs>? LoopFailed;
        public event EventHandler<bool>? CameraPositionReady;

        public TaskCompletionSource<CalibrationFrame>? PendingRequest { get; set; }

        public Task<CalibrationFrame> RequestFrame(CancellationToken token)
        {
            App.Log("Create the TCS with the async flag");
            // Create the TCS with the async flag
            var tcs = new TaskCompletionSource<CalibrationFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

            // If the user cancels the operation, tell the TCS to stop waiting
            token.Register(() => tcs.TrySetCanceled());

            // Assign it so the BackgroundLoop can see it
            PendingRequest = tcs;

            return tcs.Task;
        }


        public CaptureLoop(Device device, bool shouldGetCameraPosition = false)
        {
            _device = device;
            _thread = new Thread(BackgroundLoop) { IsBackground = true };

            _config = new DeviceConfiguration()
            {
                CameraFps = FrameRate.Thirty,
                //DepthMode = DepthMode.NarrowViewUnbinned,
                DepthMode = DepthMode.WideView2x2Binned,
                //DepthMode = DepthMode.WideViewUnbinned,
                ColorResolution = ColorResolution.R720p,
                ColorFormat = ImageFormat.ColorBgra32,
                WiredSyncMode = WiredSyncMode.Standalone,
            };
            _shouldGetCameraPosition = shouldGetCameraPosition;

            App.Log($"Depth Mode: Width = {_config.DepthMode.WidthPixels()};  Height = {_config.DepthMode.HeightPixels()}");
            App.Log($"Color Mode: Width = {_config.ColorResolution.WidthPixels()};  Height = {_config.ColorResolution.HeightPixels()}");
        }

        public (int width, int height) GetColorResolution() => (_config.ColorResolution.WidthPixels(), _config.ColorResolution.HeightPixels());
        public (int width, int height) GetDepthResolution() => (_config.DepthMode.WidthPixels(), _config.DepthMode.HeightPixels());

        public void Run()
        {
            if (_isRunning)
                throw new InvalidOperationException("Could not run capture loop. It's already running.");
            _isRunning = true;
            _thread.Start();
        }

        public void GetCalibration(out Calibration calibration)
        {
            _device.GetCalibration(_config.DepthMode, _config.ColorResolution, out calibration);
        }


        private void BackgroundLoop()
        {
            try
            {
                _device.StartCameras(_config);
                App.Log("Camera started. Entering loop...");

#if TEST2
                bool isCalibrationImageSaved = false;

#endif


                while (_isRunning)
                {

                    if ((ShouldCollectIMU || _shouldGetCameraPosition) && !_isIMURunning)
                    {
                        _device.StartImu();
                        _isIMURunning = true;
                        _imuIndex = 0;
                        _gyroVector = Vector3.Zero;
                        _accelVector = Vector3.Zero;
                    }
                    else if (!(ShouldCollectIMU || _shouldGetCameraPosition) && _isIMURunning)
                    {
                        _device.StopImu();
                        _isIMURunning = false;
                    }

                    bool success = _device.TryGetCapture(out var capture, TimeSpan.FromMilliseconds(100));

                    if (success && capture != null)
                    {
                        using (capture)
                        {
                            // 1. Check if the Service requested a frame
                            if (PendingRequest != null && !PendingRequest.Task.IsCompleted)
                            {
                                App.Log("Receive the Service requested a frame");
                                    
                                using var colorImage = capture.ColorImage;
                                if (colorImage != null)
                                {
                                    byte[] managedData = new byte[colorImage.SizeBytes];
                                    System.Runtime.InteropServices.Marshal.Copy(colorImage.Buffer, managedData, 0, managedData.Length);

#if TEST2
                                    if (!isCalibrationImageSaved)
                                    {
                                        var filePath = Path.Combine(Constants.LOG_FOLDER, "calibration_frame.bin");
                                        System.IO.File.WriteAllBytes(filePath, managedData);
                                        App.Log("Calibration frame saved to " + filePath);
                                        isCalibrationImageSaved = true;
                                    }

#endif

                                    PendingRequest.TrySetResult(new CalibrationFrame
                                    {
                                        ColorData = managedData,
                                        Width = colorImage.WidthPixels,
                                        Height = colorImage.HeightPixels
                                    });

                                    PendingRequest = null; // Reset
                                    App.Log("BackgroundLoop captured calibration frame!");
                                }
                            }
                            // -----------------
                            var timestamp = DateTime.Now;
                            CaptureReady?.Invoke(this, new(capture, timestamp));
                        }
                    }
                    else
                    {
                        if (!_device.IsConnected)
                            throw new DeviceConnectionLostException(_device.DeviceIndex);
                        Thread.Sleep(1);
                    }

                    if (ShouldCollectIMU && _isIMURunning && _device.TryGetImuSample(out var sample))
                    {
                        _gyroVector.X = _gyroVector.X + (sample.GyroSample.X - _gyroVector.X) / (_imuIndex + 1);
                        _gyroVector.Y = _gyroVector.Y + (sample.GyroSample.Y - _gyroVector.Y) / (_imuIndex + 1);
                        _gyroVector.Z = _gyroVector.Z + (sample.GyroSample.Z - _gyroVector.Z) / (_imuIndex + 1);

                        _accelVector.X = _accelVector.X + (sample.AccelerometerSample.X - _accelVector.X) / (_imuIndex + 1);
                        _accelVector.Y = _accelVector.Y + (sample.AccelerometerSample.Y - _accelVector.Y) / (_imuIndex + 1);
                        _accelVector.Z = _accelVector.Z + (sample.AccelerometerSample.Z - _accelVector.Z) / (_imuIndex + 1);

                        _imuIndex++;

                    }
                    else if (_shouldGetCameraPosition && _isIMURunning && _device.TryGetImuSample(out var sampleC))
                    {
                        _accelSum += new Vector3(
                            sampleC.AccelerometerSample.X,
                            sampleC.AccelerometerSample.Y,
                            sampleC.AccelerometerSample.Z
                            );
                        _sampleCount++;

                        if (_sampleCount >= _requiredSamples)
                        {
                            Vector3 avgAccel = _accelSum / _sampleCount;
                            _accelVector = Vector3.Normalize(avgAccel);
                            // Compare dot product with camera forward vector (0,0,1)
                            float dot = Vector3.Dot(_accelVector, Vector3.UnitZ);
#if DEBUG
                            App.Log($"Compare dot product with camera forward vector (0,0,1) => {dot}");
#endif
                            bool isFloorMounted = dot < 0;
                            CameraPositionReady?.Invoke(this, isFloorMounted);

                            // Reset for next detection cycle
                            _accelSum = Vector3.Zero;
                            _sampleCount = 0;
                            _shouldGetCameraPosition = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log(ex.ToString());
                LoopFailed?.Invoke(this, new(ex));
            }
        }

        public Vector3 GetCameraRotation()
        {
            if (_accelVector == Vector3.Zero)
                return Vector3.Zero;

            _accelVector = Vector3.Normalize(_accelVector);

            //float roll = MathF.Atan2(_accelVector.Y, _accelVector.Z);
            //float pitch = MathF.Atan2(-_accelVector.X, MathF.Sqrt(_accelVector.Y * _accelVector.Y + _accelVector.Z * _accelVector.Z));


            // Calculate Pitch and Roll from the accelerometer data
            float pitch = (float)Math.Asin(-_accelVector.X); // Convert radians to degrees
            float roll = (float)Math.Atan2(_accelVector.Y, _accelVector.Z); // Convert radians to degrees
            float halfPI = MathF.PI / 2;


            if (roll > halfPI || roll < -halfPI)
            {
                roll = 0f; // When roll is close to 180 or -180 degrees, reset to 0 (forward tilt)
            }

            return new Vector3(pitch, 0f, roll);
        }

        private float WrapAngleDegrees(float angle)
        {
            // Get a modulo value between 0 (inclusive) and 360 (exclusive)
            angle = angle % 360f;

            if (angle < 0)
            {
                angle += 360f;
            }

            // If angle is greater than or equal to 180, subtract 360 to bring it within [-180, 180)
            if (angle >= 180f)
            {
                angle -= 360f;
            }

            return angle;
        }

        public void Dispose()
        {
            if (_isRunning)
            {
                _isRunning = false;
                if (_thread != null && _thread.ThreadState != System.Threading.ThreadState.Unstarted)
                    _thread.Join();
            }

            _device.Dispose();
        }
    }

    internal class CaptureLoopEventArgs : EventArgs
    {
        public Capture? Capture { get; }
        public DateTime Timestamp { get; } // Use raw DateTime

        public CaptureLoopEventArgs(Capture? capture, DateTime timestamp)
        {
            Capture = capture;
            Timestamp = timestamp;
        }
    }

    internal class LoopFailedEventArgs : EventArgs
    {
        public LoopFailedEventArgs(Exception exception)
            => Exception = exception;

        public Exception Exception { get; }
    }
}
