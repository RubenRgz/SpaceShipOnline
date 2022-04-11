using System;
using System.Collections.Generic;
using UnityEngine;


namespace Unity.Services.Analytics
{
    public static class CS_AnalyticsService 
    {
        /// <summary>
        /// Records important data of the current device that is running the game
        /// </summary>
        public static void RecordDeviceData()
        {
            var parameters = new Dictionary<string, object>()
            {
                { "DeviceModel", SystemInfo.deviceModel },
                { "DeviceType", SystemInfo.deviceType.ToString() },
                { "GraphicsDeviceName", SystemInfo.graphicsDeviceName },
                { "OperatingSystem", SystemInfo.operatingSystem },
                { "ProcessorCount", SystemInfo.processorCount },
                { "SystemMemorySize", SystemInfo.systemMemorySize}
            };

            Events.CustomData("DeviceInfo", parameters);
        }

        /// <summary>
        /// Records important data of the game
        /// </summary>
        /// <param name="_timeSpent"></param>
        public static void RecordGameData(float _timeSpent)
        {
            var parameters = new Dictionary<string, object>()
            {
                { "Date", DateTime.UtcNow.ToString() },
                { "TimeSpent", _timeSpent }
            };

            Events.CustomData("GameInfo", parameters);
        }

        /// <summary>
        /// Sends the current queue data to the cloud
        /// </summary>
        public static void FlushQueueData()
        {
            Events.Flush();
        }
    }
}
