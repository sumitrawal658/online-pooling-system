namespace PollSystem.Services.Logging
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Extensions.Logging;
    using PollSystem.Services.Device;

    public class StructuredLoggingService
    {
        private readonly ILogger _logger;
        private readonly IDeviceInfoService _deviceInfo;
        private readonly Dictionary<string, object> _baseProperties;

        public StructuredLoggingService(
            ILogger logger,
            IDeviceInfoService deviceInfo)
        {
            _logger = logger;
            _deviceInfo = deviceInfo;
            _baseProperties = CreateBaseProperties();
        }

        private Dictionary<string, object> CreateBaseProperties()
        {
            var deviceDetails = _deviceInfo.GetDeviceDetails();
            var properties = new Dictionary<string, object>
            {
                ["Timestamp"] = DateTime.UtcNow,
                ["AppVersion"] = deviceDetails.AppInfo.Version,
                ["DevicePlatform"] = deviceDetails.Platform.ToString(),
                ["DeviceModel"] = deviceDetails.Model,
                ["OSVersion"] = deviceDetails.VersionString
            };

            // Add screen metrics if available
            if (deviceDetails.ScreenMetrics != null)
            {
                properties["Screen.Width"] = deviceDetails.ScreenMetrics.Width;
                properties["Screen.Height"] = deviceDetails.ScreenMetrics.Height;
                properties["Screen.Density"] = deviceDetails.ScreenMetrics.Density;
            }

            return properties;
        }

        public IDisposable BeginScope(string operationName, Dictionary<string, object> additionalProperties = null)
        {
            var scopeProperties = new Dictionary<string, object>(_baseProperties)
            {
                ["OperationName"] = operationName,
                ["StartTime"] = DateTime.UtcNow
            };

            if (additionalProperties != null)
            {
                foreach (var prop in additionalProperties)
                {
                    scopeProperties[prop.Key] = prop.Value;
                }
            }

            return _logger.BeginScope(scopeProperties);
        }

        public void LogEvent(
            string eventName,
            LogLevel level,
            Dictionary<string, object> properties = null,
            Exception exception = null)
        {
            var eventProperties = new Dictionary<string, object>(_baseProperties)
            {
                ["EventName"] = eventName,
                ["Timestamp"] = DateTime.UtcNow
            };

            if (properties != null)
            {
                foreach (var prop in properties)
                {
                    eventProperties[prop.Key] = prop.Value;
                }
            }

            // Add memory usage information
            var memoryInfo = GetMemoryInfo();
            foreach (var item in memoryInfo)
            {
                eventProperties[item.Key] = item.Value;
            }

            using (_logger.BeginScope(eventProperties))
            {
                if (exception != null)
                {
                    _logger.Log(level, exception, "{EventName} occurred", eventName);
                }
                else
                {
                    _logger.Log(level, "{EventName} occurred", eventName);
                }
            }
        }

        private Dictionary<string, object> GetMemoryInfo()
        {
            return new Dictionary<string, object>
            {
                ["Memory.WorkingSet"] = GC.GetTotalMemory(false),
                ["Memory.GCGen0Collections"] = GC.CollectionCount(0),
                ["Memory.GCGen1Collections"] = GC.CollectionCount(1),
                ["Memory.GCGen2Collections"] = GC.CollectionCount(2)
            };
        }
    }
} 