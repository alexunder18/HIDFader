using CSCore.CoreAudioAPI;
using NLog;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using HIDMate.Core;
using CoreAudioSessionManager = HIDMate.Core.AudioSessionManager;

namespace HIDMate.Input
{
    public partial class VolumeControlListener : IDisposable
    {
        private readonly Logger log = LogManager.GetCurrentClassLogger();

        private const float AxisVolumeTriggerSensitivity = 0.005f; // ~0.5% — smaller than any meaningful user move
        private const int RepeatIntervalMs = 100;
        private const int RescanIntervalMs = 2000;

        private volatile bool isListening;
        private long lastRescanTicks;

        private Thread joystickListenerThread;
        private BindingConfiguration bindingConfiguration;
        private CoreAudioSessionManager audioSessionManager;
        private Dictionary<Guid, Joystick> activeJoysticks = new Dictionary<Guid, Joystick>();
        private Dictionary<Guid, bool[]> previousButtonStates = new Dictionary<Guid, bool[]>();
        private Dictionary<Guid, int> previousPOVStates = new Dictionary<Guid, int>();
        private Dictionary<Guid, string> deviceFriendlyNames = new Dictionary<Guid, string>();
        private Dictionary<Guid, Guid> deviceProductGuids = new Dictionary<Guid, Guid>();
        private Dictionary<string, float> lastAxisVolumeByBinding = new Dictionary<string, float>();
        private Dictionary<string, bool> axisKeyboardArmedByBinding = new Dictionary<string, bool>();
        private Dictionary<string, float> preMuteVolumeByProcess = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private DirectInput directInput;
        private Timer repeatTimer;
        private Dictionary<string, HeldInputState> heldJoystickButtons;

        public float VolumeStep { get; set; } = 0.01f;
        public event Action<string> OnActionExecuted;

        public VolumeControlListener(BindingConfiguration bindingConfiguration, CoreAudioSessionManager audioSessionManager)
        {
            this.bindingConfiguration = bindingConfiguration;
            this.audioSessionManager = audioSessionManager;
            heldJoystickButtons = new Dictionary<string, HeldInputState>();
            repeatTimer = new Timer(OnRepeatTimerTick, null, Timeout.Infinite, RepeatIntervalMs);
        }

        public void Start()
        {
            if (isListening)
                return;

            try
            {
                isListening = true;

                try
                {
                    log.Debug("Starting joystick listener...");
                    StartJoystickListener();
                    log.Debug("Joystick listener thread started");
                }
                catch (Exception joystickEx)
                {
                    log.Warn(joystickEx, "Joystick listener failed");
                    // Don't re-throw - continue without joystick
                }
            }
            catch (Exception ex)
            {
                isListening = false;
                log.Error(ex, "Error in Start()");
                throw;
            }
        }

        public void Stop()
        {
            isListening = false;

            try
            {
                if (repeatTimer != null)
                {
                    repeatTimer.Change(Timeout.Infinite, RepeatIntervalMs);
                    log.Debug("Repeat timer stopped");
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error stopping repeat timer");
            }

            try
            {
                if (joystickListenerThread != null && joystickListenerThread.IsAlive)
                {
                    if (!joystickListenerThread.Join(2000))
                    {
                        log.Debug("Joystick listener thread did not exit in time");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error stopping joystick thread");
            }

            heldJoystickButtons.Clear();

            CleanupJoysticks();
        }

        private void StartJoystickListener()
        {
            try
            {
                joystickListenerThread = new Thread(JoystickListenerThread)
                {
                    IsBackground = true,
                    Name = "VolumeControlJoystickListener"
                };

                joystickListenerThread.Start();
                log.Debug("Joystick listener thread created and started");
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error starting joystick listener thread");
                // Don't throw - joystick is optional
            }
        }

        /// <summary>
        /// Get the most up-to-date configurations (called for each input to ensure we have latest bindings)
        /// </summary>
        private List<ApplicationConfig> GetCurrentConfigurations()
        {
            try
            {
                return bindingConfiguration.GetAllConfigurations();
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error getting configurations");
                return new List<ApplicationConfig>();
            }
        }

        private void JoystickListenerThread()
        {
            try
            {
                directInput = new DirectInput();
                RescanDevices();
                lastRescanTicks = Environment.TickCount;

                while (isListening)
                {
                    try
                    {
                        PollAllJoysticks();

                        // Periodically re-enumerate so that devices added or moved between USB ports (new InstanceGuid) are picked up at runtime.
                        long now = Environment.TickCount;

                        if (now - lastRescanTicks >= RescanIntervalMs)
                        {
                            RescanDevices();
                            lastRescanTicks = now;
                        }

                        Thread.Sleep(10);
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex, "Error in joystick polling loop");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Joystick listener error");
            }
            finally
            {
                CleanupJoysticks();
            }
        }

        /// <summary>
        ///     Enumerates currently attached game controllers and synchronizes our live
        ///     joystick dictionary: adds newly-connected devices (including the same HID
        ///     reappearing on a different USB port with a new InstanceGuid), and drops
        ///     devices that are no longer present. Called periodically from the polling
        ///     thread so USB replugs are handled at runtime without restarting the app.
        /// </summary>
        private void RescanDevices()
        {
            IList<DeviceInstance> devices;

            try
            {
                devices = directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error enumerating devices");
                return;
            }

            var presentGuids = new HashSet<Guid>();
            foreach (var device in devices)
            {
                presentGuids.Add(device.InstanceGuid);

                if (activeJoysticks.ContainsKey(device.InstanceGuid))
                {
                    continue;
                }

                try
                {
                    var joystick = new Joystick(directInput, device.InstanceGuid);
                    joystick.Acquire();
                    activeJoysticks[device.InstanceGuid] = joystick;

                    var friendlyName = GetFriendlyDeviceName(device.ProductName);
                    deviceFriendlyNames[device.InstanceGuid] = friendlyName;
                    deviceProductGuids[device.InstanceGuid] = device.ProductGuid;

                    var state = joystick.GetCurrentState();
                    previousButtonStates[device.InstanceGuid] = state.Buttons ?? new bool[0];
                    previousPOVStates[device.InstanceGuid] = (state.PointOfViewControllers != null && state.PointOfViewControllers.Length > 0)
                        ? state.PointOfViewControllers[0] : -1;

                    log.Debug($"Acquired joystick: {friendlyName} (ProductGuid: {device.ProductGuid}, InstanceGuid: {device.InstanceGuid})");
                }
                catch (Exception ex)
                {
                    log.Error(ex, "Failed to acquire device '{0}'", device.ProductName);
                }
            }

            // Drop devices that are no longer attached (e.g., unplugged, or moved to a different USB port which reassigns the InstanceGuid).
            var stale = new List<Guid>();
            foreach (var guid in activeJoysticks.Keys)
            {
                if (!presentGuids.Contains(guid))
                {
                    stale.Add(guid);
                }
            }

            foreach (var guid in stale)
            {
                RemoveJoystick(guid, "device no longer attached");
            }
        }

        private void RemoveJoystick(Guid instanceGuid, string reason)
        {
            if (activeJoysticks.TryGetValue(instanceGuid, out var joystick))
            {
                try { joystick.Unacquire(); } catch { }
                try { joystick.Dispose(); } catch { }

                activeJoysticks.Remove(instanceGuid);
            }

            previousButtonStates.Remove(instanceGuid);
            previousPOVStates.Remove(instanceGuid);
            deviceFriendlyNames.TryGetValue(instanceGuid, out var name);
            deviceFriendlyNames.Remove(instanceGuid);
            deviceProductGuids.Remove(instanceGuid);

            log.Debug($"Removed joystick {name ?? instanceGuid.ToString()} ({reason})");
        }

        private void PollAllJoysticks()
        {
            List<ApplicationConfig> configurations = GetCurrentConfigurations();

            // Snapshot so we can safely remove a stale joystick mid-iteration (e.g., the device vanished between rescans).
            List<KeyValuePair<Guid, Joystick>> snapshot = new List<KeyValuePair<Guid, Joystick>>(activeJoysticks);
            List<Guid> deadDevices = null;

            foreach (var kvp in snapshot)
            {
                var deviceGuid = kvp.Key;
                var joystick = kvp.Value;
                var deviceName = deviceFriendlyNames.TryGetValue(deviceGuid, out var name) ? name : "Unknown";

                try
                {
                    joystick.Poll();
                    var state = joystick.GetCurrentState();
                    bool[] buttons = state.Buttons;
                    bool[] previousButtons = previousButtonStates.ContainsKey(deviceGuid)
                        ? previousButtonStates[deviceGuid]
                        : new bool[0];

                    int buttonCount = Math.Min(buttons.Length, 128);

                    for (int i = 0; i < buttonCount; i++)
                    {
                        var wasPressed = i < previousButtons.Length && previousButtons[i];

                        // Detect button press (transition from false to true)
                        if (buttons[i] && !wasPressed)
                        {
                            ProcessJoystickButtonPress(i, deviceName, deviceGuid, configurations);
                        }

                        // Detect button release (transition from true to false)
                        if (!buttons[i] && wasPressed)
                        {
                            ProcessJoystickButtonRelease(i, deviceName);
                        }
                    }

                    previousButtonStates[deviceGuid] = buttons;

                    // Poll POV hat switches
                    var pov = state.PointOfViewControllers;
                    int previousPOV = previousPOVStates.ContainsKey(deviceGuid) ? previousPOVStates[deviceGuid] : -1;

                    if (pov != null && pov.Length > 0)
                    {
                        int currentPOV = pov[0];

                        // Detect POV press (transition from neutral to a direction)
                        if (previousPOV == -1 && currentPOV >= 0)
                        {
                            var povCode = GetPOVCode(currentPOV);
                            if (povCode != null)
                            {
                                ProcessPOVPress(povCode, deviceName, deviceGuid, configurations);
                            }
                        }
                        // Detect POV direction change (hat moved to a different direction)
                        else if (previousPOV >= 0 && currentPOV >= 0 && previousPOV != currentPOV)
                        {
                            // Release previous direction
                            var prevPovCode = GetPOVCode(previousPOV);
                            if (prevPovCode != null)
                            {
                                ProcessPOVRelease(prevPovCode, deviceName);
                            }
                            // Press new direction
                            string povCode = GetPOVCode(currentPOV);
                            if (povCode != null)
                            {
                                ProcessPOVPress(povCode, deviceName, deviceGuid, configurations);
                            }
                        }
                        // Detect POV release (transition from a direction to neutral)
                        else if (previousPOV >= 0 && currentPOV == -1)
                        {
                            string prevPovCode = GetPOVCode(previousPOV);
                            if (prevPovCode != null)
                            {
                                ProcessPOVRelease(prevPovCode, deviceName);
                            }
                        }

                        previousPOVStates[deviceGuid] = currentPOV;
                    }

                    // Axis bindings: continuous mapping of the axis position
                    // directly to application volume. VolumeStep is intentionally
                    // ignored for HID Axis — the axis value is the volume.
                    ProcessAxisBindings(state, deviceName, deviceGuid, configurations);
                }
                catch (Exception ex)
                {
                    log.Error(ex, "Error polling joystick {0}", deviceName);

                    // Stale handle (e.g., USB unplug, computer reboot). 
                    // Rescan will re-add the device on its next enumeration if it comes back.
                    if (deadDevices == null)
                    {
                        deadDevices = new List<Guid>();
                    }

                    deadDevices.Add(deviceGuid);
                }
            }

            if (deadDevices != null)
            {
                foreach (var guid in deadDevices)
                {
                    RemoveJoystick(guid, "poll failed");
                }
            }

            // Drive held mouse movement off the poll cadence so cursor speed is
            // roughly independent of how many devices are attached.
            ProcessHeldMouseMovements();
        }

        private static bool ButtonCodeMatches(InputBinding binding, int buttonIndex)
        {
            foreach (var code in binding.InputCodes)
            {
                if (code == buttonIndex.ToString())
                    return true;
                if (code.EndsWith($"Button_{buttonIndex}"))
                    return true;
                if (code.EndsWith($"_{buttonIndex}"))
                    return true;
            }
            return false;
        }

        private void ProcessJoystickButtonPress(int buttonIndex, string deviceName, Guid deviceInstanceGuid, List<ApplicationConfig> configurations)
        {
            var heldKey = $"{deviceName}_Button_{buttonIndex}";
            var currentProductGuid = deviceProductGuids.TryGetValue(deviceInstanceGuid, out var pg)
                ? pg.ToString()
                : null;

            // Per-app pass: volume + keyboard emit. Mouse actions live in the
            // global MouseBindings list now and are handled in the second pass.
            foreach (var config in configurations)
            {
                if (!config.Enabled)
                {
                    continue;
                }

                foreach (var binding in config.InputBindings)
                {
                    if (!binding.Enabled || binding.InputType != "HIDButton")
                    {
                        continue;
                    }

                    if (IsMouseAction(binding.Action))
                    {
                        continue;
                    }

                    if (!BindingMatchesDevice(binding, currentProductGuid, deviceName))
                    {
                        continue;
                    }

                    if (!ButtonCodeMatches(binding, buttonIndex))
                    {
                        continue;
                    }

                    if (!AreModifiersPressed(binding.ModifierCodes))
                    {
                        continue;
                    }

                    if (!heldJoystickButtons.ContainsKey(heldKey))
                    {
                        heldJoystickButtons[heldKey] = new HeldInputState
                        {
                            PressTime = Stopwatch.GetTimestamp(),
                            Action = binding.Action,
                            Config = config,
                            HasExecutedInitial = false
                        };

                        // Keyboard emit fires once on press; only volume actions use hold-to-repeat.
                        if (repeatTimer != null && !IsKeyboardEmit(binding))
                        {
                            repeatTimer.Change(RepeatIntervalMs, RepeatIntervalMs);
                        }

                        DispatchBindingAction(binding, config);
                    }
                }
            }

            // Global mouse pass — runs even if a per-app binding already grabbed
            // this button (heldJoystickButtons.ContainsKey will skip the duplicate
            // hold registration). The cursor moves system-wide, so there's no
            // ApplicationConfig context to attach.
            int mouseSensitivity = ResolveMouseSensitivity();
            foreach (var binding in bindingConfiguration.MouseBindings)
            {
                if (!binding.Enabled || binding.InputType != "HIDButton")
                {
                    continue;
                }
                if (!IsMouseAction(binding.Action))
                {
                    continue;
                }
                if (!BindingMatchesDevice(binding, currentProductGuid, deviceName))
                {
                    continue;
                }
                if (!ButtonCodeMatches(binding, buttonIndex))
                {
                    continue;
                }
                if (!AreModifiersPressed(binding.ModifierCodes))
                {
                    continue;
                }

                if (heldJoystickButtons.ContainsKey(heldKey))
                {
                    // A per-app binding already claimed this press for hold tracking.
                    // Still fire the mouse action one-shot so users can layer.
                    ExecuteMouseAction(binding.Action, mouseSensitivity, "Mouse");
                    continue;
                }

                heldJoystickButtons[heldKey] = new HeldInputState
                {
                    PressTime = Stopwatch.GetTimestamp(),
                    Action = binding.Action,
                    Config = null,
                    MouseSensitivity = mouseSensitivity,
                    HasExecutedInitial = false,
                };

                if (repeatTimer != null)
                {
                    repeatTimer.Change(RepeatIntervalMs, RepeatIntervalMs);
                }

                ExecuteMouseAction(binding.Action, mouseSensitivity, "Mouse");
            }
        }

        private void ProcessJoystickButtonRelease(int buttonIndex, string deviceName)
        {
            var heldKey = $"{deviceName}_Button_{buttonIndex}";

            if (heldJoystickButtons.ContainsKey(heldKey))
            {
                heldJoystickButtons.Remove(heldKey);

                if (heldJoystickButtons.Count == 0)
                {
                    if (repeatTimer != null)
                    {
                        repeatTimer.Change(Timeout.Infinite, RepeatIntervalMs);
                    }
                }
            }
        }

        private static bool POVCodeMatches(InputBinding binding, string heldKey, string povCode)
        {
            foreach (var code in binding.InputCodes)
            {
                if (code.Equals(heldKey, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (code.EndsWith(povCode, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void ProcessPOVPress(string povCode, string deviceName, Guid deviceInstanceGuid, List<ApplicationConfig> configurations)
        {
            var heldKey = $"{deviceName}_{povCode}";
            var currentProductGuid = deviceProductGuids.TryGetValue(deviceInstanceGuid, out var pg)
                ? pg.ToString()
                : null;

            // Per-app pass.
            foreach (var config in configurations)
            {
                if (!config.Enabled)
                {
                    continue;
                }

                foreach (var binding in config.InputBindings)
                {
                    if (!binding.Enabled || binding.InputType != "HIDButton")
                    {
                        continue;
                    }

                    if (IsMouseAction(binding.Action))
                    {
                        continue;
                    }

                    if (!BindingMatchesDevice(binding, currentProductGuid, deviceName))
                    {
                        continue;
                    }

                    if (!POVCodeMatches(binding, heldKey, povCode))
                    {
                        continue;
                    }

                    if (!AreModifiersPressed(binding.ModifierCodes))
                    {
                        continue;
                    }

                    log.Debug($"{heldKey} matched binding: {binding.Description}");

                    if (!heldJoystickButtons.ContainsKey(heldKey))
                    {
                        heldJoystickButtons[heldKey] = new HeldInputState
                        {
                            PressTime = Stopwatch.GetTimestamp(),
                            Action = binding.Action,
                            Config = config,
                            HasExecutedInitial = false
                        };

                        if (repeatTimer != null && !IsKeyboardEmit(binding))
                        {
                            repeatTimer.Change(RepeatIntervalMs, RepeatIntervalMs);
                        }

                        DispatchBindingAction(binding, config);
                    }
                }
            }

            // Global mouse pass.
            int mouseSensitivity = ResolveMouseSensitivity();
            foreach (var binding in bindingConfiguration.MouseBindings)
            {
                if (!binding.Enabled || binding.InputType != "HIDButton")
                {
                    continue;
                }
                if (!IsMouseAction(binding.Action))
                {
                    continue;
                }
                if (!BindingMatchesDevice(binding, currentProductGuid, deviceName))
                {
                    continue;
                }
                if (!POVCodeMatches(binding, heldKey, povCode))
                {
                    continue;
                }
                if (!AreModifiersPressed(binding.ModifierCodes))
                {
                    continue;
                }

                if (heldJoystickButtons.ContainsKey(heldKey))
                {
                    ExecuteMouseAction(binding.Action, mouseSensitivity, "Mouse");
                    continue;
                }

                heldJoystickButtons[heldKey] = new HeldInputState
                {
                    PressTime = Stopwatch.GetTimestamp(),
                    Action = binding.Action,
                    Config = null,
                    MouseSensitivity = mouseSensitivity,
                    HasExecutedInitial = false,
                };

                if (repeatTimer != null)
                {
                    repeatTimer.Change(RepeatIntervalMs, RepeatIntervalMs);
                }

                ExecuteMouseAction(binding.Action, mouseSensitivity, "Mouse");
            }
        }

        private void ProcessPOVRelease(string povCode, string deviceName)
        {
            string heldKey = $"{deviceName}_{povCode}";

            if (heldJoystickButtons.ContainsKey(heldKey))
            {
                heldJoystickButtons.Remove(heldKey);

                if (heldJoystickButtons.Count == 0)
                {
                    if (repeatTimer != null)
                    {
                        repeatTimer.Change(Timeout.Infinite, RepeatIntervalMs);
                    }
                }
            }
        }

        /// <summary>
        /// For every enabled HIDAxis binding that targets the current device,
        /// read the referenced axis, normalize to 0..1 against the DirectInput
        /// default range (0..65535), and push that value straight onto the audio
        /// session's MasterVolume. VolumeStep does not apply here — axis position
        /// is the volume level.
        /// </summary>
        private void ProcessAxisBindings(JoystickState state, string deviceName, Guid deviceInstanceGuid, List<ApplicationConfig> configurations)
        {
            string currentProductGuid = deviceProductGuids.TryGetValue(deviceInstanceGuid, out var pg)
                ? pg.ToString()
                : null;

            foreach (var config in configurations)
            {
                if (!config.Enabled)
                {
                    continue;
                }

                foreach (var binding in config.InputBindings)
                {
                    if (!binding.Enabled || binding.InputType != "HIDAxis")
                    {
                        continue;
                    }

                    if (!BindingMatchesDevice(binding, currentProductGuid, deviceName))
                    {
                        continue;
                    }

                    if (binding.InputCodes == null || binding.InputCodes.Count == 0)
                    {
                        continue;
                    }

                    if (!AreModifiersPressed(binding.ModifierCodes))
                    {
                        continue;
                    }

                    // A binding stores "DeviceName_Axis_X" etc. We only need the
                    // axis portion ("Axis_X", "Slider_0") to read the value.
                    var axisName = ExtractAxisName(binding.InputCodes[0]);
                    if (axisName == null)
                    {
                        continue;
                    }

                    if (!TryReadAxisValue(state, axisName, out int raw))
                    {
                        continue;
                    }

                    float normalized = Math.Max(0f, Math.Min(1f, raw / 65535f));
                    if (binding.Inverted)
                    {
                        normalized = 1f - normalized;
                    }

                    if (IsMouseAction(binding.Action))
                    {
                        // Per-app pass no longer dispatches mouse — handled by
                        // ProcessGlobalMouseAxisBindings against the global list.
                        continue;
                    }

                    if (IsKeyboardEmit(binding))
                    {
                        // Edge-triggered: fire once when the axis crosses into the "high"
                        // zone, re-arm after it returns to the "low" zone. Hysteresis
                        // prevents chatter around the threshold.
                        const float HighThreshold = 0.55f;
                        const float LowThreshold = 0.45f;

                        if (!axisKeyboardArmedByBinding.ContainsKey(binding.BindingId))
                        {
                            // First observation: only arm if the axis is currently below
                            // the low threshold so we don't fire immediately on startup
                            // when a throttle is already forward.
                            axisKeyboardArmedByBinding[binding.BindingId] = normalized <= LowThreshold;
                            continue;
                        }

                        bool armed = axisKeyboardArmedByBinding[binding.BindingId];

                        if (armed && normalized >= HighThreshold)
                        {
                            axisKeyboardArmedByBinding[binding.BindingId] = false;
                            EmitKeyboardKeys(binding, config);
                        }
                        else if (!armed && normalized <= LowThreshold)
                        {
                            axisKeyboardArmedByBinding[binding.BindingId] = true;
                        }

                        continue;
                    }

                    if (lastAxisVolumeByBinding.TryGetValue(binding.BindingId, out float last) && Math.Abs(last - normalized) < AxisVolumeTriggerSensitivity)
                    {
                        continue; // axis is essentially unchanged — skip the write
                    }

                    lastAxisVolumeByBinding[binding.BindingId] = normalized;
                    SetVolumeDirect(normalized, config);
                }
            }

            ProcessGlobalMouseAxisBindings(state, deviceName, currentProductGuid);
        }

        /// <summary>
        /// Mirror of ProcessAxisBindings but for the global MouseBindings list.
        /// MouseAxisX/Y do continuous center-rest movement; other Mouse*
        /// actions on an axis are edge-triggered with hysteresis.
        /// </summary>
        private void ProcessGlobalMouseAxisBindings(JoystickState state, string deviceName, string currentProductGuid)
        {
            int sensitivity = ResolveMouseSensitivity();

            foreach (var binding in bindingConfiguration.MouseBindings)
            {
                if (!binding.Enabled || binding.InputType != "HIDAxis")
                {
                    continue;
                }
                if (!IsMouseAction(binding.Action))
                {
                    continue;
                }
                if (!BindingMatchesDevice(binding, currentProductGuid, deviceName))
                {
                    continue;
                }
                if (binding.InputCodes == null || binding.InputCodes.Count == 0)
                {
                    continue;
                }
                if (!AreModifiersPressed(binding.ModifierCodes))
                {
                    continue;
                }

                var axisName = ExtractAxisName(binding.InputCodes[0]);
                if (axisName == null)
                {
                    continue;
                }
                if (!TryReadAxisValue(state, axisName, out int raw))
                {
                    continue;
                }

                float normalized = Math.Max(0f, Math.Min(1f, raw / 65535f));
                if (binding.Inverted)
                {
                    normalized = 1f - normalized;
                }

                if (IsMouseAxisMovementAction(binding.Action))
                {
                    const float Deadzone = 0.05f;
                    float deflection = (normalized - 0.5f) * 2f; // -1..1

                    if (Math.Abs(deflection) < Deadzone)
                    {
                        continue;
                    }

                    int pixels = (int)Math.Round(deflection * sensitivity * 2);
                    if (pixels == 0)
                    {
                        continue;
                    }

                    if (binding.Action == "MouseAxisX")
                    {
                        MouseEmitter.Move(pixels, 0);
                    }
                    else
                    {
                        MouseEmitter.Move(0, pixels);
                    }
                }
                else
                {
                    const float HighThreshold = 0.55f;
                    const float LowThreshold = 0.45f;

                    if (!axisKeyboardArmedByBinding.ContainsKey(binding.BindingId))
                    {
                        axisKeyboardArmedByBinding[binding.BindingId] = normalized <= LowThreshold;
                        continue;
                    }

                    bool armed = axisKeyboardArmedByBinding[binding.BindingId];

                    if (armed && normalized >= HighThreshold)
                    {
                        axisKeyboardArmedByBinding[binding.BindingId] = false;
                        ExecuteMouseAction(binding.Action, sensitivity, "Mouse");
                    }
                    else if (!armed && normalized <= LowThreshold)
                    {
                        axisKeyboardArmedByBinding[binding.BindingId] = true;
                    }
                }
            }
        }

        private static bool IsKeyboardEmit(InputBinding binding)
        {
            return string.Equals(binding.Action, "KeyboardEmit", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMouseAction(string action)
        {
            return ApplicationConfig.IsMouseAction(action);
        }

        private static bool IsMouseMovementAction(string action)
        {
            return action == "MouseUp" || action == "MouseDown"
                || action == "MouseLeft" || action == "MouseRight";
        }

        private static bool IsMouseClickAction(string action)
        {
            return action == "MouseLeftClick" || action == "MouseRightClick";
        }

        private static bool IsMouseAxisMovementAction(string action)
        {
            return action == "MouseAxisX" || action == "MouseAxisY";
        }

        /// <summary>
        /// Reads the global mouse sensitivity, defaulting to 5 if the value on
        /// disk was zero/missing. Cheap to call per tick — just two field reads.
        /// </summary>
        private int ResolveMouseSensitivity()
        {
            int s = bindingConfiguration.MouseSensitivity;
            return s > 0 ? s : 5;
        }

        /// <summary>
        /// Per-app dispatcher. Mouse actions used to land here too, but mouse is
        /// now global and dispatched directly by the press/POV/axis handlers, so
        /// this only sees volume + keyboard bindings.
        /// </summary>
        private void DispatchBindingAction(InputBinding binding, ApplicationConfig config)
        {
            if (IsKeyboardEmit(binding))
            {
                EmitKeyboardKeys(binding, config);
            }
            else
            {
                ExecuteVolumeAction(binding.Action, config);
            }
        }

        /// <summary>
        /// One-shot dispatch for mouse actions. Movement actions move the cursor
        /// by sensitivity*2 pixels; clicks fire one button press/release; scroll
        /// fires one notch. Held movement is driven by ProcessHeldMouseMovements
        /// off the poll loop, not by the repeat timer.
        /// </summary>
        private void ExecuteMouseAction(string action, int sensitivity, string label)
        {
            try
            {
                int pixels = (sensitivity > 0 ? sensitivity : 5) * 2;
                string source = string.IsNullOrEmpty(label) ? "Mouse" : label;

                switch (action)
                {
                    case "MouseUp":
                        MouseEmitter.Move(0, -pixels);
                        OnActionExecuted?.Invoke($"Mouse up ({source})");
                        break;
                    case "MouseDown":
                        MouseEmitter.Move(0, pixels);
                        OnActionExecuted?.Invoke($"Mouse down ({source})");
                        break;
                    case "MouseLeft":
                        MouseEmitter.Move(-pixels, 0);
                        OnActionExecuted?.Invoke($"Mouse left ({source})");
                        break;
                    case "MouseRight":
                        MouseEmitter.Move(pixels, 0);
                        OnActionExecuted?.Invoke($"Mouse right ({source})");
                        break;
                    case "MouseLeftClick":
                        MouseEmitter.LeftClick();
                        OnActionExecuted?.Invoke($"Mouse left click ({source})");
                        break;
                    case "MouseRightClick":
                        MouseEmitter.RightClick();
                        OnActionExecuted?.Invoke($"Mouse right click ({source})");
                        break;
                    case "MouseScrollUp":
                        MouseEmitter.Scroll(1);
                        OnActionExecuted?.Invoke($"Scroll up ({source})");
                        break;
                    case "MouseScrollDown":
                        MouseEmitter.Scroll(-1);
                        OnActionExecuted?.Invoke($"Scroll down ({source})");
                        break;
                    default:
                        log.Debug($"Unknown mouse action: {action}");
                        break;
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error executing mouse action {0}", action);
            }
        }

        /// <summary>
        /// Emits relative cursor movement for every currently-held mouse-movement
        /// button. Called at the end of each PollAllJoysticks tick (~10ms), giving
        /// roughly 100Hz movement updates — smooth without flooding mouse_event.
        /// Sensitivity comes from each held button's snapshot, so a slider change
        /// mid-hold doesn't alter the speed of an already-pressed button.
        /// </summary>
        private void ProcessHeldMouseMovements()
        {
            if (heldJoystickButtons.Count == 0)
                return;

            int dx = 0, dy = 0;
            bool any = false;

            foreach (var kvp in heldJoystickButtons)
            {
                var action = kvp.Value.Action;
                if (!IsMouseMovementAction(action))
                    continue;

                int sens = kvp.Value.MouseSensitivity > 0 ? kvp.Value.MouseSensitivity : 5;
                int pixels = sens * 2;
                any = true;

                switch (action)
                {
                    case "MouseUp": dy -= pixels; break;
                    case "MouseDown": dy += pixels; break;
                    case "MouseLeft": dx -= pixels; break;
                    case "MouseRight": dx += pixels; break;
                }
            }

            if (any)
            {
                MouseEmitter.Move(dx, dy);
            }
        }

        private void EmitKeyboardKeys(InputBinding binding, ApplicationConfig config)
        {
            if (binding.OutputKeys == null || binding.OutputKeys.Count == 0)
            {
                return;
            }

            // Keystrokes go to the foreground window, so a per-app binding is only
            // meaningful when that app IS the foreground window. This lets the same
            // physical button send different keys depending on which app is focused.
            var foregroundProcess = GetForegroundProcessName();
            if (!string.Equals(foregroundProcess, config.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                log.Debug($"Keyboard emit skipped: foreground is '{foregroundProcess}', binding targets '{config.ProcessName}'");
                return;
            }

            // Snapshot so the joystick thread can't mutate the list mid-emit.
            var chords = new List<string>(binding.OutputKeys);

            // Offload to the thread pool so inter-chord sleeps don't stall polling.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    KeyboardEmitter.EmitSequence(chords);
                    OnActionExecuted?.Invoke($"Keys: {string.Join(", ", chords)} ({config.DisplayName})");
                }
                catch (Exception ex)
                {
                    log.Error(ex, "Error emitting keyboard sequence for {0}", config.DisplayName);
                }
            });
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        /// <summary>
        /// Returns Process.ProcessName (e.g. "discord", "DCS" — no .exe) of the window
        /// that currently has focus, or null if it can't be resolved.
        /// </summary>
        private string GetForegroundProcessName()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return null;

                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0)
                    return null;

                using (var process = Process.GetProcessById((int)pid))
                {
                    return process.ProcessName;
                }
            }
            catch (Exception ex)
            {
                log.Debug($"Could not resolve foreground process: {ex.Message}");
                return null;
            }
        }

        private static string ExtractAxisName(string inputCode)
        {
            if (string.IsNullOrEmpty(inputCode))
            {
                return null;
            }

            int idx = inputCode.LastIndexOf("Axis_", StringComparison.Ordinal);

            if (idx < 0)
            {
                idx = inputCode.LastIndexOf("Slider_", StringComparison.Ordinal);
            }

            if (idx < 0)
            {
                return null;
            }

            return inputCode.Substring(idx);
        }

        private static bool TryReadAxisValue(JoystickState state, string axisName, out int value)
        {
            switch (axisName)
            {
                case "Axis_X": value = state.X; return true;
                case "Axis_Y": value = state.Y; return true;
                case "Axis_Z": value = state.Z; return true;
                case "Axis_RX": value = state.RotationX; return true;
                case "Axis_RY": value = state.RotationY; return true;
                case "Axis_RZ": value = state.RotationZ; return true;
            }

            if (axisName.StartsWith("Slider_") && state.Sliders != null)
            {
                if (int.TryParse(axisName.Substring("Slider_".Length), out int idx) && idx >= 0 && idx < state.Sliders.Length)
                {
                    value = state.Sliders[idx];
                    return true;
                }
            }

            value = 0;
            return false;
        }

        private void SetVolumeDirect(float volume, ApplicationConfig config)
        {
            try
            {
                var audioSession = audioSessionManager.GetAudioSessionByProcessName(config.ProcessName);
                if (audioSession == null)
                {
                    return;
                }

                var simpleVolume = audioSession.SimpleAudioVolume;
                if (simpleVolume == null)
                {
                    return;
                }

                simpleVolume.MasterVolume = volume;
                OnActionExecuted?.Invoke($"Volume (axis): {volume:P0} ({config.DisplayName})");
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error setting axis volume");
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

        /// <summary>
        /// Checks if all modifier codes for a binding are currently pressed.
        /// Modifier codes can be HID buttons (e.g., "DeviceName_Button_3"),
        /// POV directions (e.g., "DeviceName_POV_East"), or keyboard keys (e.g., "Add").
        /// </summary>
        private bool AreModifiersPressed(List<string> modifierCodes)
        {
            if (modifierCodes == null || modifierCodes.Count == 0)
            {
                return true;
            }

            foreach (var modifier in modifierCodes)
            {
                if (!IsModifierPressed(modifier))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsModifierPressed(string modifierCode)
        {
            // Check HID buttons and POV across all devices
            foreach (var kvp in activeJoysticks)
            {
                Guid deviceGuid = kvp.Key;
                var deviceName = deviceFriendlyNames.TryGetValue(deviceGuid, out var name) ? name : "Unknown";

                if (previousButtonStates.TryGetValue(deviceGuid, out var buttons))
                {
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        if (!buttons[i])
                        {
                            continue;
                        }

                        var fullCode = $"{deviceName}_Button_{i}";
                        if (modifierCode.Equals(fullCode, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        if (modifierCode.Equals($"Button_{i}", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        if (modifierCode == i.ToString())
                        {
                            return true;
                        }

                        if (modifierCode.EndsWith($"Button_{i}", StringComparison.OrdinalIgnoreCase) && modifierCode.Contains(deviceName))
                        {
                            return true;
                        }
                    }
                }

                // Check POV: modifier could be "DeviceName_POV_North" or "POV_North"
                if (previousPOVStates.TryGetValue(deviceGuid, out var povValue) && povValue >= 0)
                {
                    var povCode = GetPOVCode(povValue);
                    if (povCode != null)
                    {
                        string fullPovCode = $"{deviceName}_{povCode}";
                        if (modifierCode.Equals(fullPovCode, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        if (modifierCode.Equals(povCode, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        if (modifierCode.EndsWith(povCode, StringComparison.OrdinalIgnoreCase) && modifierCode.Contains(deviceName))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Decides whether a binding belongs to the device that fired the current input.
        /// If the binding has a stored ProductGuid (captured with this field), only accept
        /// the matching device — this is stable across USB ports and reboots.
        /// Legacy bindings with no ProductGuid fall back to the existing device-name /
        /// suffix matching performed by the caller.
        /// </summary>
        private bool BindingMatchesDevice(InputBinding binding, string currentProductGuid, string currentDeviceName)
        {
            if (string.IsNullOrEmpty(binding.ProductGuid))
            {
                return true;
            }

            if (string.IsNullOrEmpty(currentProductGuid))
            {
                return true;
            }

            return string.Equals(binding.ProductGuid, currentProductGuid, StringComparison.OrdinalIgnoreCase);
        }

        private string GetFriendlyDeviceName(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName))
            {
                return "Unknown";
            }

            var name = System.Text.RegularExpressions.Regex.Replace(productName, @"[^a-zA-Z0-9]", "_");
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
            previousButtonStates.Clear();
            previousPOVStates.Clear();
            deviceFriendlyNames.Clear();
            deviceProductGuids.Clear();

            if (directInput != null)
            {
                try
                {
                    directInput.Dispose();
                    directInput = null;
                }
                catch { }
            }
        }

        private void ExecuteVolumeAction(string action, ApplicationConfig config)
        {
            try
            {
                // log.Debug($"ExecuteVolumeAction: Action={action}, Config={config.DisplayName}");

                // Get the audio session for this application (searches across all audio devices)
                var audioSession = audioSessionManager.GetAudioSessionByProcessName(config.ProcessName);
                // log.Debug($"Audio session retrieved: {(audioSession != null ? "Success" : "NULL")}");

                if (audioSession == null)
                {
                    log.Debug($"ERROR: No audio session found for process: {config.ProcessName}");
                    return;
                }

                SimpleAudioVolume volume = audioSession.SimpleAudioVolume;
                // log.Debug($"SimpleAudioVolume retrieved: {(volume != null ? "Success" : "NULL")}");

                if (volume == null)
                {
                    // log.Debug($"ERROR: SimpleAudioVolume is null");
                    return;
                }

                switch (action.ToLower())
                {
                    case "volumeup":
                        var oldVolumeUp = volume.MasterVolume;
                        volume.MasterVolume = Math.Min(1f, volume.MasterVolume + VolumeStep);
                        var newVolumeUp = volume.MasterVolume;
                        log.Debug($"Volume up: {oldVolumeUp:P0} → {newVolumeUp:P0} ({config.DisplayName})");
                        OnActionExecuted?.Invoke($"Volume up: {newVolumeUp:P0} ({config.DisplayName})");
                        break;

                    case "volumedown":
                        var oldVolDown = volume.MasterVolume;
                        volume.MasterVolume = Math.Max(0f, volume.MasterVolume - VolumeStep);
                        var newVolDown = volume.MasterVolume;
                        log.Debug($"Volume down: {oldVolDown:P0} → {newVolDown:P0} ({config.DisplayName})");
                        OnActionExecuted?.Invoke($"Volume down: {newVolDown:P0} ({config.DisplayName})");
                        break;

                    case "mute":
                        var currentVolume = volume.MasterVolume;
                        if (currentVolume > 0)
                        {
                            preMuteVolumeByProcess[config.ProcessName] = currentVolume;
                            volume.MasterVolume = 0f;
                            log.Debug($"Mute: ON ({config.DisplayName})");
                            OnActionExecuted?.Invoke($"Mute: ON ({config.DisplayName})");
                        }
                        else
                        {
                            var restore = preMuteVolumeByProcess.TryGetValue(config.ProcessName, out var saved) && saved > 0f
                                ? saved
                                : 0.5f;
                            volume.MasterVolume = restore;
                            log.Debug($"Mute: OFF ({config.DisplayName}) restored to {restore:P0}");
                            OnActionExecuted?.Invoke($"Mute: OFF ({config.DisplayName})");
                        }
                        break;

                    default:
                        log.Debug($"Unknown action: {action}");
                        break;
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error executing action {0}", action);
            }
        }

        private void OnRepeatTimerTick(object state)
        {
            try
            {
                // Execute held joystick buttons. The 100ms cadence is right for
                // volume ticks and wheel notches; toggles fire once on press, and
                // mouse movement runs off the 10ms poll loop.
                foreach (var kvp in heldJoystickButtons)
                {
                    var action = kvp.Value.Action;

                    if (string.Equals(action, "Mute", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "KeyboardEmit", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (IsMouseMovementAction(action) || IsMouseClickAction(action))
                    {
                        continue;
                    }

                    try
                    {
                        if (IsMouseAction(action))
                        {
                            // Only scroll lands here — movement and clicks were
                            // filtered out above. Sensitivity was snapshotted
                            // at press-time on the HeldInputState.
                            ExecuteMouseAction(action, kvp.Value.MouseSensitivity, "Mouse");
                        }
                        else
                        {
                            ExecuteVolumeAction(action, kvp.Value.Config);
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex, "Error executing repeat action for button {0}", kvp.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error in repeat timer");
            }
        }

        public void Dispose()
        {
            Stop();

            if (repeatTimer != null)
            {
                try
                {
                    repeatTimer.Dispose();
                    repeatTimer = null;
                }
                catch { }
            }
        }
    }
}