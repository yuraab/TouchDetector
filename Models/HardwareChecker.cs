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
            App.Log("Projector checking");
#if FAST_DEBUG
    Debug.WriteLine("Fast Debug mode: Skipping projector checking");
    OnStatusChanged?.Invoke(StatusCode.Connected);
    return;
#endif

            await Task.Run(() =>
            {
                try
                {
                    var projectorKeywords = LoadProjectorKeywords();
                    bool foundMatch = false;

                    // 1. Get ALL active monitor IDs first
                    // Removing the Technology filter ensures we see DisplayPort, VGA, and USB-C
                    string idQuery = "SELECT * FROM WmiMonitorID WHERE Active = True";

                    using var idSearcher = new ManagementObjectSearcher(@"root\WMI", idQuery);
                    var monitorResults = idSearcher.Get();

                    App.Log($"Scanning {monitorResults.Count} active monitors...");

                    foreach (ManagementObject idObject in monitorResults)
                    {
                        string model = DecodeWmiField(idObject["UserFriendlyName"] as ushort[]);
                        string manufacturer = DecodeWmiField(idObject["ManufacturerName"] as ushort[]);
                        string instanceName = idObject["InstanceName"]?.ToString() ?? "";

                        // LOG EVERYTHING to debug why it might be failing
                        App.Log($"[Checking Device] Manuf: '{manufacturer}', Model: '{model}', ID: {instanceName}");

                        // 2. Check against keywords
                        bool isMatch = projectorKeywords.Any(k =>
                            (model?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false) ||
                            (manufacturer?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false));

                        if (isMatch)
                        {
                            App.Log($"🎯 MATCH FOUND: {model} matches a keyword.");
                            foundMatch = true;
                            break; // Exit loop since we found our projector
                        }
                    }

                    if (foundMatch)
                    {
                        OnStatusChanged?.Invoke(StatusCode.Connected);
                    }
                    else
                    {
                        App.Log("No monitors matched the loaded keywords.");
                        OnStatusChanged?.Invoke(StatusCode.NotConnected);
                    }
                }
                catch (Exception ex)
                {
                    App.Log("Error checking projectors: " + ex.Message);
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
                keywords.Add("BenQ");
                keywords.Add("Panasonic");
                keywords.Add("Epson");
                keywords.Add("ViewSonic");
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
