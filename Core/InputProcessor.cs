using CSCore.CoreAudioAPI;
using NLog;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using VihorVolumeMixer.Input;

namespace VihorVolumeMixer.Core
{
    /// <summary>
    /// Processes input events and executes configured bindings for volume control
    /// Runs on a background thread listening for HID and keyboard input
    /// When input is detected, controls the volume of the application that has that binding
    /// </summary>
    public class InputProcessor : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public float VolumeStep = 0.01f;

        private BindingConfiguration bindingConfiguration;
        private AudioSessionManager audioSessionManager;
        private KeyboardHook keyboardHook;
        private InputDeviceManager inputDeviceManager;
        private Dictionary<Guid, Joystick> activeJoysticks = new Dictionary<Guid, Joystick>();
        private Dictionary<Guid, JoystickState> previousJoystickStates = new Dictionary<Guid, JoystickState>();
        private Dictionary<Guid, string> deviceFriendlyNames = new Dictionary<Guid, string>();
        private Thread joystickListenerThread;
        private volatile bool isRunning;
        

        public InputProcessor(string initialTargetProcessName = null)
        {
            bindingConfiguration = new BindingConfiguration();
            bindingConfiguration.Load();

            audioSessionManager = new AudioSessionManager();
            inputDeviceManager = new InputDeviceManager();
        }

        /// <summary>
        /// Get audio session for a specific process name
        /// </summary>
        private SimpleAudioVolume GetAudioForProcess(string processName)
        {
            try
            {
                return audioSessionManager.GetAudioSessionByProcessName(processName);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting audio for {0}", processName);
                return null;
            }
        }

        /// <summary>
        /// Start listening for input events
        /// </summary>
        public void Start()
        {
            if (isRunning)
                return;

            isRunning = true;

            try
            {
                // Setup keyboard hook
                SetupKeyboardHook();

                // Start joystick listener thread
                joystickListenerThread = new Thread(JoystickListenerThread)
                {
                    IsBackground = true,
                    Name = "InputProcessorJoystickListener"
                };
                joystickListenerThread.Start();

                Logger.Debug("Input listener started");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting listener");
                isRunning = false;
            }
        }

        /// <summary>
        /// Stop listening for input events
        /// </summary>
        public void Stop()
        {
            isRunning = false;

            try
            {
                // Stop keyboard hook
                if (keyboardHook != null)
                {
                    keyboardHook.Unhook();
                    keyboardHook = null;
                }

                // Stop joystick listener
                if (joystickListenerThread != null)
                {
                    joystickListenerThread.Join(1000);
                    joystickListenerThread = null;
                }

                // Release all joysticks
                foreach (var joystick in activeJoysticks.Values)
                {
                    try
                    {
                        joystick.Unacquire();
                        joystick.Dispose();
                    }
                    catch { }
                }
                activeJoysticks.Clear();

                Logger.Debug("Input listener stopped");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error stopping listener");
            }
        }

        private void SetupKeyboardHook()
        {
            try
            {
                keyboardHook = new KeyboardHook();
                keyboardHook.KeyDown += KeyboardHook_KeyDown;

                // Hook all keys
                foreach (Keys key in Enum.GetValues(typeof(Keys)))
                {
                    keyboardHook.HookedKeys.Add(key);
                }

                keyboardHook.Hook();
                Logger.Debug("Keyboard hook installed");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error installing keyboard hook");
            }
        }

        private void KeyboardHook_KeyDown(object sender, KeyEventArgs e)
        {
            if (!isRunning)
                return;

            try
            {
                string keyCode = $"Key_{e.KeyCode}";

                // Get all configurations for keyboard bindings
                var configs = bindingConfiguration.GetConfigurationsWithBindings();

                foreach (var config in configs)
                {
                    // Check keyboard bindings
                    var keyboardBindings = config.GetKeyboardBindings();
                    foreach (var binding in keyboardBindings)
                    {
                        if (binding.Enabled && binding.InputCodes.Contains(keyCode))
                        {
                            // Check if modifiers match (if any are required)
                            if (binding.ModifierCodes.Count == 0 || ModifiersPressed(binding.ModifierCodes))
                            {
                                // Get the audio session for this application
                                var audio = GetAudioForProcess(config.ProcessName);
                                if (audio != null)
                                {
                                    ExecuteBinding(binding, audio);
                                    e.Handled = true;
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error processing keyboard input");
            }
        }

        private void JoystickListenerThread()
        {
            try
            {
                InitializeJoysticks();

                while (isRunning)
                {
                    try
                    {
                        PollAllJoysticks();
                        Thread.Sleep(20); // 20ms polling interval
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error in joystick polling loop");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Joystick listener error");
            }
            finally
            {
                CleanupJoysticks();
            }
        }

        private void InitializeJoysticks()
        {
            try
            {
                var devices = inputDeviceManager.GetGameControlDevices();

                // Cache friendly device names
                deviceFriendlyNames.Clear();
                foreach (var device in devices)
                {
                    deviceFriendlyNames[device.InstanceGuid] = GetFriendlyDeviceName(device.ProductName);
                }

                // Create joystick objects
                foreach (var device in devices)
                {
                    if (!activeJoysticks.ContainsKey(device.InstanceGuid))
                    {
                        try
                        {
                            var joystick = inputDeviceManager.CreateJoystick(device.InstanceGuid);
                            joystick.Acquire();
                            activeJoysticks[device.InstanceGuid] = joystick;

                            // Initialize previous state
                            var state = joystick.GetCurrentState();
                            previousJoystickStates[device.InstanceGuid] = state;

                            Logger.Debug($"Joystick acquired: {deviceFriendlyNames[device.InstanceGuid]}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "Failed to acquire joystick");
                        }
                    }
                }

                Logger.Debug($"Initialized {activeJoysticks.Count} joysticks");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing joysticks");
            }
        }

        private void PollAllJoysticks()
        {
            foreach (var kvp in activeJoysticks)
            {
                Guid deviceGuid = kvp.Key;
                Joystick joystick = kvp.Value;

                try
                {
                    var currentState = joystick.GetCurrentState();
                    JoystickState previousState = previousJoystickStates.ContainsKey(deviceGuid) 
                        ? previousJoystickStates[deviceGuid] 
                        : null;

                    if (previousState != null)
                    {
                        DetectHIDInputChanges(deviceGuid, previousState, currentState);
                    }

                    previousJoystickStates[deviceGuid] = currentState;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error polling joystick {0}", deviceGuid);
                }
            }
        }

        private void DetectHIDInputChanges(Guid deviceGuid, JoystickState previousState, JoystickState currentState)
        {
            string deviceName = deviceFriendlyNames.TryGetValue(deviceGuid, out var name) ? name : "Unknown";

            // Check buttons
            if (currentState.Buttons != null && previousState.Buttons != null)
            {
                for (int i = 0; i < currentState.Buttons.Length; i++)
                {
                    // Detect button press (transition from false to true)
                    if (!previousState.Buttons[i] && currentState.Buttons[i])
                    {
                        string buttonCode = $"{deviceName}_Button_{i}";
                        ProcessHIDBinding(buttonCode);
                    }
                }
            }

            // Check POV hat
            if (currentState.PointOfViewControllers != null && currentState.PointOfViewControllers.Length > 0)
            {
                int currentPOV = currentState.PointOfViewControllers[0];
                int previousPOV = (previousState.PointOfViewControllers != null && previousState.PointOfViewControllers.Length > 0) 
                    ? previousState.PointOfViewControllers[0] 
                    : -1;

                if (previousPOV == -1 && currentPOV >= 0)
                {
                    string povCode = GetPOVCode(currentPOV);
                    if (!string.IsNullOrEmpty(povCode))
                    {
                        string fullCode = $"{deviceName}_{povCode}";
                        ProcessHIDBinding(fullCode);
                    }
                }
            }
        }

        private void ProcessHIDBinding(string inputCode)
        {
            var configs = bindingConfiguration.GetConfigurationsWithBindings();

            foreach (var config in configs)
            {
                var hidBindings = config.GetHidBindings();
                foreach (var binding in hidBindings)
                {
                    if (binding.Enabled && binding.InputCodes.Contains(inputCode))
                    {
                        // Check if modifiers match (if any are required)
                        if (binding.ModifierCodes.Count == 0 || ModifiersPressed(binding.ModifierCodes))
                        {
                            // Get the audio session for this application
                            var audio = GetAudioForProcess(config.ProcessName);
                            if (audio != null)
                            {
                                ExecuteBinding(binding, audio);
                            }
                            break;
                        }
                    }
                }
            }
        }

        private bool ModifiersPressed(List<string> modifierCodes)
        {
            // This is a simplified check - in a real scenario you'd need to track
            // which modifier buttons are currently pressed on each device
            // For now, we'll just check if any modifiers are specified but not pressed
            return true; // TODO: Implement proper modifier tracking
        }

        private void ExecuteBinding(InputBinding binding, SimpleAudioVolume audio)
        {
            try
            {
                if (audio == null)
                    return;

                switch (binding.Action)
                {
                    case "VolumeUp":
                        audio.MasterVolume = Math.Min(1f, audio.MasterVolume + VolumeStep);
                        Logger.Debug($"Volume UP: {audio.MasterVolume:P0}");
                        break;

                    case "VolumeDown":
                        audio.MasterVolume = Math.Max(0f, audio.MasterVolume - VolumeStep);
                        Logger.Debug($"Volume DOWN: {audio.MasterVolume:P0}");
                        break;

                    case "Mute":
                        // Mute is not directly supported by SimpleAudioVolume, 
                        // so we'll use a workaround by setting volume to 0 or restoring it
                        if (audio.MasterVolume > 0.01f)
                        {
                            audio.MasterVolume = 0f;
                            Logger.Debug($"Muted");
                        }
                        else
                        {
                            audio.MasterVolume = 0.5f;
                            Logger.Debug($"Unmuted to 50%");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error executing binding");
            }
        }

        private string GetPOVCode(int povValue)
        {
            if (povValue == 0) return "POV_North";
            if (povValue == 4500) return "POV_NorthEast";
            if (povValue == 9000) return "POV_East";
            if (povValue == 13500) return "POV_SouthEast";
            if (povValue == 18000) return "POV_South";
            if (povValue == 22500) return "POV_SouthWest";
            if (povValue == 27000) return "POV_West";
            if (povValue == 31500) return "POV_NorthWest";
            return null;
        }

        private string GetFriendlyDeviceName(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName))
                return "Unknown";

            string name = System.Text.RegularExpressions.Regex.Replace(productName, @"[^a-zA-Z0-9]", "_");
            name = name.Trim('_');

            return string.IsNullOrEmpty(name) ? "Device" : name;
        }

        private void CleanupJoysticks()
        {
            foreach (var joystick in activeJoysticks.Values)
            {
                try
                {
                    joystick.Unacquire();
                    joystick.Dispose();
                }
                catch { }
            }
            activeJoysticks.Clear();
            previousJoystickStates.Clear();
        }

        public void Dispose()
        {
            Stop();

            if (bindingConfiguration != null)
            {
                bindingConfiguration.Dispose();
                bindingConfiguration = null;
            }
        }
    }
}
