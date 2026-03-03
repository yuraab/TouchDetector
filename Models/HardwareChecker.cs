using CommunityToolkit.Mvvm.ComponentModel;
using Emgu.CV.Mcc;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static Emgu.CV.Dai.OpenVino;

namespace CPRTouchVision.Models
{
    public enum StatusCode
    {
        Pending,
        Connected,
        NotConnected,
        Error
    }

    public partial class HardwareStatusItem : ObservableObject, INotifyPropertyChanged
    {
        [ObservableProperty]
        private string name;

        [ObservableProperty]
        private StatusCode status;
        /*
        public StatusCode Status
        {
            get => status;
            set
            {
                if (status != value)
                {
                    status = value;
                    OnPropertyChanged();
                }
            }
        }
        

        public event PropertyChangedEventHandler PropertyChanged;

        // This helper method notifies the UI to refresh the binding
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        */
    }

    public interface IHardwareChecker 
    { 
        string DeviceName { get; } 
        event Action<StatusCode> OnStatusChanged; 
        Task CheckConnection();
    }


    public class CameraChecker : IHardwareChecker
    {
        public event Action<StatusCode> OnStatusChanged;
        public string DeviceName { get; } = "Camera";


        public async Task CheckConnection()
        {
            Debug.WriteLine($"Camera checking");

            await Task.Run(() => {
                try
                { // Keywords for Orbbec Femto Bolt
                  var cameraKeywords = new[] { "Femto Bolt", "Orbbec" }; 
                    
                  // Query imaging devices
                  string query = 
                    "SELECT Caption, Status FROM Win32_PnPEntity " + 
                    "WHERE (PNPClass = 'Camera' OR PNPClass = 'Image')"; 

                    using var searcher = new ManagementObjectSearcher(query); 
                    bool found = false; 
                    foreach (ManagementObject device in searcher.Get()) 
                    { 
                        string deviceName = device["Caption"]?.ToString() ?? ""; 
                        if (cameraKeywords.Any(k => deviceName.Contains(k, StringComparison.OrdinalIgnoreCase))) 
                        { 
                            Debug.WriteLine($"Found Camera: {deviceName}"); 
                            OnStatusChanged?.Invoke(StatusCode.Connected); 
                            found = true; 
                            break; 
                        } 
                    } 
                    if (!found) 
                        OnStatusChanged?.Invoke(StatusCode.NotConnected); 
                } catch (Exception ex) 
                { 
                    Debug.WriteLine("Error checking camera: " + ex.Message); 
                    OnStatusChanged?.Invoke(StatusCode.Error); 
                } 
            });
                
        }
    }

    public class ProjectorChecker : IHardwareChecker
    {
        public string DeviceName => "Projector"; 
        public event Action<StatusCode>? OnStatusChanged; 
        public async Task CheckConnection()
        {
            Debug.WriteLine("Projector checking"); 
#if FAST_DEBUG 
            Debug.WriteLine("Fast Debug mode: Skipping projector checking"); 
            OnStatusChanged?.Invoke(StatusCode.Connected); 
            return; 
#endif 
            await Task.Run(() => 
            { 
            try 
                { 
                    // Load projector keywords from INI
                    var projectorKeywords = LoadProjectorKeywords(); 
#if DEBUG
                    Debug.WriteLine("Checking projectors: " + string.Join(", ", projectorKeywords)); 
#endif 
                    // Query HDMI connections
                    string hdmiQuery = 
                        "SELECT InstanceName FROM WmiMonitorConnectionParams " + 
                        "WHERE Active = True AND VideoOutputTechnology = 5"; 
                    using var searcher = new ManagementObjectSearcher(@"root\WMI", hdmiQuery); 
                    foreach (ManagementObject hdmiDevice in searcher.Get()) 
                    { 
                        string instanceName = hdmiDevice["InstanceName"]?.ToString() ?? ""; 
                        string escapedInstance = instanceName.Replace("\\", "\\\\"); 
                        // Query monitor ID info
                        string idQuery = "SELECT * FROM WmiMonitorID"; 
                        using var idSearcher = new ManagementObjectSearcher(@"root\WMI", idQuery); 
                        foreach (ManagementObject idObject in idSearcher.Get()) 
                        { 
                            string model = DecodeWmiField(idObject["UserFriendlyName"] as ushort[]); 
                            string manufacturer = DecodeWmiField(idObject["ManufacturerName"] as ushort[]); 
                            Debug.WriteLine($"Model: {model}; Manufacturer: {manufacturer}"); 
                            
                            // Match keywords
                            if (projectorKeywords.Any(k => 
                                    model.Contains(k, StringComparison.OrdinalIgnoreCase)) || 
                                projectorKeywords.Any(k => 
                                    manufacturer.Contains(k, StringComparison.OrdinalIgnoreCase))) 
                            {
#if DEBUG
                                var projector = projectorKeywords.FirstOrDefault(k =>
                                    model.Contains(k, StringComparison.OrdinalIgnoreCase));
                                projector ??=  projectorKeywords.FirstOrDefault(k =>
                                    manufacturer.Contains(k, StringComparison.OrdinalIgnoreCase));
                                Debug.WriteLine($"Projector found");
#endif
                                OnStatusChanged?.Invoke(StatusCode.Connected); 
                                return; 
                            } 
                        } 
                    } // No match found
                      OnStatusChanged?.Invoke(StatusCode.NotConnected); 
                } 
                catch (Exception ex) 
                { 
                    Debug.WriteLine("Error checking projectors: " + ex.Message); 
                    OnStatusChanged?.Invoke(StatusCode.Error); 
                } 
            }); 
        } 
        private List<string> LoadProjectorKeywords() 
        { 
            List<string> keywords = new(); 
#if DEBUG 
            string userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); 
            string homeDirectory = "C:\\ProgramData\\CPR Soft\\CPR Touch Vision";
#else 
            string homeDirectory = "C:\\ProgramData\\CPR Soft\\CPR Touch Vision"; 
#endif 
            string iniPath = Path.Combine(homeDirectory, "projectors.ini"); 
            if (!File.Exists(iniPath)) 
            { 
                keywords.Add("Optoma"); 
                return keywords; 
            } 
            return File.ReadAllLines(iniPath)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("["))
                .ToList(); 
        } 
        private static string DecodeWmiField(ushort[]? data) 
        { 
            if (data == null) 
                return string.Empty; 

            return new string(data
                .TakeWhile(v => v != 0)
                .Select(v => (char)v)
                .ToArray())
                .Trim(); 
        } 
    }

}
