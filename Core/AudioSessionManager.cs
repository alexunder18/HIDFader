using CSCore.CoreAudioAPI;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace HIDMate.Core
{
    /// <summary>
    /// Manages audio sessions and volume control for applications across all audio devices
    /// </summary>
    public class AudioSessionManager
    {
        private readonly Logger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Gets all audio sessions across all active render devices.
        /// Skips expired sessions and prefers active sessions when the same process
        /// appears on multiple devices (e.g. after switching the default output device).
        /// </summary>
        public List<AudioSessionInfo> GetAllAudioSessions()
        {
            var sessions = new List<AudioSessionInfo>();
            // Maps PID to index in sessions list, so we can replace inactive with active
            var pidToIndex = new Dictionary<int, int>();

            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                using (var deviceCollection = enumerator.EnumAudioEndpoints(DataFlow.Render, DeviceState.Active))
                {
                    foreach (var device in deviceCollection)
                    {
                        try
                        {
                            var deviceId = device.DeviceID;
                            var deviceName = device.FriendlyName;

                            using (var manager = AudioSessionManager2.FromMMDevice(device))
                            using (var sessionEnumerator = manager.GetSessionEnumerator())
                            {
                                foreach (var session in sessionEnumerator)
                                {
                                    try
                                    {
                                        using (var control = session.QueryInterface<AudioSessionControl2>())
                                        {
                                            var state = control.SessionState;

                                            // Skip expired sessions
                                            if (state == AudioSessionState.AudioSessionStateExpired)
                                                continue;

                                            var pid = control.ProcessID;
                                            var isActive = state == AudioSessionState.AudioSessionStateActive;

                                            if (pid == 0)
                                            {
                                                if (!pidToIndex.ContainsKey(0))
                                                {
                                                    var sysInfo = new AudioSessionInfo(0, "System Sounds", isSystemSound: true);
                                                    sysInfo.DeviceId = deviceId;
                                                    sysInfo.DeviceFriendlyName = deviceName;
                                                    sysInfo.IsActive = isActive;
                                                    pidToIndex[0] = sessions.Count;
                                                    sessions.Add(sysInfo);
                                                }
                                                else if (isActive && !sessions[pidToIndex[0]].IsActive)
                                                {
                                                    // Replace inactive with active
                                                    var sysInfo = new AudioSessionInfo(0, "System Sounds", isSystemSound: true);
                                                    sysInfo.DeviceId = deviceId;
                                                    sysInfo.DeviceFriendlyName = deviceName;
                                                    sysInfo.IsActive = true;
                                                    sessions[pidToIndex[0]] = sysInfo;
                                                }
                                                continue;
                                            }

                                            try
                                            {
                                                var process = Process.GetProcessById(pid);

                                                if (!pidToIndex.ContainsKey(pid))
                                                {
                                                    var sessionInfo = new AudioSessionInfo(pid, process.ProcessName);
                                                    sessionInfo.DeviceId = deviceId;
                                                    sessionInfo.DeviceFriendlyName = deviceName;
                                                    sessionInfo.IsActive = isActive;
                                                    pidToIndex[pid] = sessions.Count;
                                                    sessions.Add(sessionInfo);
                                                }
                                                else if (isActive && !sessions[pidToIndex[pid]].IsActive)
                                                {
                                                    // Replace inactive session with active one on new device
                                                    var sessionInfo = new AudioSessionInfo(pid, process.ProcessName);
                                                    sessionInfo.DeviceId = deviceId;
                                                    sessionInfo.DeviceFriendlyName = deviceName;
                                                    sessionInfo.IsActive = true;
                                                    sessions[pidToIndex[pid]] = sessionInfo;
                                                }
                                            }
                                            catch (ArgumentException)
                                            {
                                                // Process no longer exists
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        log.Error(ex, "Error querying session on {0}", deviceName);
                                    }
                                    finally
                                    {
                                        session.Dispose();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(ex, "Error reading sessions from device");
                        }
                        finally
                        {
                            device.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error getting audio sessions");
            }

            return sessions;
        }

        /// <summary>
        /// Gets an audio session by process name, searching across all render devices.
        /// Prefers active sessions over inactive ones. Skips expired sessions.
        /// </summary>
        public AudioSessionInfo GetAudioSessionByProcessName(string processName)
        {
            AudioSessionInfo bestMatch = null;

            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                using (var deviceCollection = enumerator.EnumAudioEndpoints(DataFlow.Render, DeviceState.Active))
                {
                    foreach (var device in deviceCollection)
                    {
                        try
                        {
                            using (var manager = AudioSessionManager2.FromMMDevice(device))
                            using (var sessionEnumerator = manager.GetSessionEnumerator())
                            {
                                foreach (var session in sessionEnumerator)
                                {
                                    try
                                    {
                                        using (var control = session.QueryInterface<AudioSessionControl2>())
                                        {
                                            var state = control.SessionState;

                                            if (state == AudioSessionState.AudioSessionStateExpired)
                                                continue;

                                            var isActive = state == AudioSessionState.AudioSessionStateActive;
                                            var pid = control.ProcessID;

                                            if (pid == 0)
                                            {
                                                if (processName.Equals("System Sounds", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (bestMatch == null || (isActive && !bestMatch.IsActive))
                                                    {
                                                        bestMatch?.SimpleAudioVolume?.Dispose();
                                                        bestMatch = new AudioSessionInfo(0, "System Sounds", isSystemSound: true);
                                                        bestMatch.SimpleAudioVolume = session.QueryInterface<SimpleAudioVolume>();
                                                        bestMatch.DeviceId = device.DeviceID;
                                                        bestMatch.DeviceFriendlyName = device.FriendlyName;
                                                        bestMatch.IsActive = isActive;
                                                        if (isActive) return bestMatch;
                                                    }
                                                }
                                                continue;
                                            }

                                            try
                                            {
                                                var process = Process.GetProcessById(pid);
                                                if (process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (bestMatch == null || (isActive && !bestMatch.IsActive))
                                                    {
                                                        bestMatch?.SimpleAudioVolume?.Dispose();
                                                        bestMatch = new AudioSessionInfo(pid, process.ProcessName);
                                                        bestMatch.SimpleAudioVolume = session.QueryInterface<SimpleAudioVolume>();
                                                        bestMatch.DeviceId = device.DeviceID;
                                                        bestMatch.DeviceFriendlyName = device.FriendlyName;
                                                        bestMatch.IsActive = isActive;
                                                        if (isActive) return bestMatch;
                                                    }
                                                }
                                            }
                                            catch (ArgumentException)
                                            {
                                                // Process no longer exists
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        log.Error(ex, "Error querying session");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(ex, "Error searching device");
                        }
                        finally
                        {
                            device.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error getting audio session by process name");
            }

            return bestMatch;
        }
    }
}
