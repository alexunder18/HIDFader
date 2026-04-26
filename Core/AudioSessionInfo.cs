using CSCore.CoreAudioAPI;
using System;
using System.Diagnostics;

namespace HIDMate.Core
{
    /// <summary>
    /// Represents information about an active audio session
    /// </summary>
    public class AudioSessionInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public bool IsSystemSound { get; set; }
        public SimpleAudioVolume SimpleAudioVolume { get; set; }
        public string DeviceId { get; set; }
        public string DeviceFriendlyName { get; set; }
        public bool IsActive { get; set; }

        public AudioSessionInfo(int processId, string processName, bool isSystemSound = false)
        {
            ProcessId = processId;
            ProcessName = processName;
            IsSystemSound = isSystemSound;
        }       
    }
}
