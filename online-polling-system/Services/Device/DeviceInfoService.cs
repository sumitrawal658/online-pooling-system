using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace PollSystem.Services.Device
{
    public class DeviceInfoService : IDeviceInfoService
    {
        private readonly ILogger<DeviceInfoService> _logger;
        private readonly Lazy<DeviceDetails> _deviceDetails;

        public DeviceInfoService(ILogger<DeviceInfoService> logger)
        {
            _logger = logger;
            _deviceDetails = new Lazy<DeviceDetails>(GatherDeviceDetails);
        }

        public DeviceDetails GetDeviceDetails() => _deviceDetails.Value;

        private DeviceDetails GatherDeviceDetails()
        {
            try
            {
                var details = new DeviceDetails
                {
                    // Device Info
                    Model = DeviceInfo.Current.Model,
                    Manufacturer = DeviceInfo.Current.Manufacturer,
                    Name = DeviceInfo.Name,
                    Platform = DeviceInfo.Current.Platform,
                    Idiom = DeviceInfo.Current.Idiom,
                    DeviceType = DeviceInfo.Current.DeviceType,

                    // OS Info
                    VersionString = DeviceInfo.Current.VersionString,
                    Version = DeviceInfo.Current.Version,

                    // Screen Metrics
                    ScreenMetrics = new ScreenMetrics
                    {
                        Density = DeviceDisplay.Current.MainDisplayInfo.Density,
                        Width = DeviceDisplay.Current.MainDisplayInfo.Width,
                        Height = DeviceDisplay.Current.MainDisplayInfo.Height,
                        Orientation = DeviceDisplay.Current.MainDisplayInfo.Orientation,
                        Rotation = DeviceDisplay.Current.MainDisplayInfo.Rotation,
                        RefreshRate = DeviceDisplay.Current.MainDisplayInfo.RefreshRate
                    },

                    // App Info
                    AppInfo = new AppDetails
                    {
                        Name = AppInfo.Current.Name,
                        PackageName = AppInfo.Current.PackageName,
                        Version = AppInfo.Current.VersionString,
                        Build = AppInfo.Current.BuildString
                    },

                    // System Info
                    SystemInfo = new SystemDetails
                    {
                        ProcessorCount = Environment.ProcessorCount,
                        OSArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                        OSDescription = RuntimeInformation.OSDescription,
                        FrameworkDescription = RuntimeInformation.FrameworkDescription,
                        Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
                        Is64BitProcess = Environment.Is64BitProcess
                    }
                };

                _logger.LogInformation("Device details gathered successfully");
                return details;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error gathering device details");
                return new DeviceDetails { Error = ex.Message };
            }
        }
    }

    public class DeviceDetails
    {
        // Device Info
        public string Model { get; set; }
        public string Manufacturer { get; set; }
        public string Name { get; set; }
        public DevicePlatform Platform { get; set; }
        public DeviceIdiom Idiom { get; set; }
        public DeviceType DeviceType { get; set; }

        // OS Info
        public string VersionString { get; set; }
        public Version Version { get; set; }

        // Screen Info
        public ScreenMetrics ScreenMetrics { get; set; }

        // App Info
        public AppDetails AppInfo { get; set; }

        // System Info
        public SystemDetails SystemInfo { get; set; }

        // Error tracking
        public string Error { get; set; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["Device.Model"] = Model,
                ["Device.Manufacturer"] = Manufacturer,
                ["Device.Name"] = Name,
                ["Device.Platform"] = Platform,
                ["Device.Idiom"] = Idiom,
                ["Device.Type"] = DeviceType,
                ["OS.Version"] = VersionString,
                ["Screen.Metrics"] = ScreenMetrics?.ToDictionary(),
                ["App.Info"] = AppInfo?.ToDictionary(),
                ["System.Info"] = SystemInfo?.ToDictionary(),
                ["Error"] = Error
            };
        }
    }

    public class ScreenMetrics
    {
        public double Density { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public DisplayOrientation Orientation { get; set; }
        public DisplayRotation Rotation { get; set; }
        public double RefreshRate { get; set; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["Density"] = Density,
                ["Width"] = Width,
                ["Height"] = Height,
                ["Orientation"] = Orientation,
                ["Rotation"] = Rotation,
                ["RefreshRate"] = RefreshRate
            };
        }
    }

    public class AppDetails
    {
        public string Name { get; set; }
        public string PackageName { get; set; }
        public string Version { get; set; }
        public string Build { get; set; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["Name"] = Name,
                ["PackageName"] = PackageName,
                ["Version"] = Version,
                ["Build"] = Build
            };
        }
    }

    public class SystemDetails
    {
        public int ProcessorCount { get; set; }
        public string OSArchitecture { get; set; }
        public string OSDescription { get; set; }
        public string FrameworkDescription { get; set; }
        public bool Is64BitOperatingSystem { get; set; }
        public bool Is64BitProcess { get; set; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["ProcessorCount"] = ProcessorCount,
                ["OSArchitecture"] = OSArchitecture,
                ["OSDescription"] = OSDescription,
                ["FrameworkDescription"] = FrameworkDescription,
                ["Is64BitOS"] = Is64BitOperatingSystem,
                ["Is64BitProcess"] = Is64BitProcess
            };
        }
    }
} 