using NLog;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using HIDFader.Core;
using HIDFader.Input;

namespace HIDFader.UI
{
    public partial class frmBindingEditor : Form
    {
        private readonly Logger log = LogManager.GetCurrentClassLogger();
        private ApplicationConfig ApplicationConfig { get; set; }
        private InputDeviceManager InputDeviceManager { get; set; }
        private BindingConfiguration BindingConfiguration { get; set; }
        private Dictionary<Guid, Joystick> ActiveJoysticks { get; set; } = new Dictionary<Guid, Joystick>();
        private List<JoystickDevice> AttachedDevices { get; set; } = new List<JoystickDevice>();
        private string CurrentCaptureMode { get; set; } // "Main" or "Modifier"
        private string CurrentCaptureAction { get; set; } // "VolumeUp", "VolumeDown", "Mute"
        private string CurrentCaptureInputType { get; set; } // "HIDButton", "KeyboardKey"
        private Guid CurrentCaptureDeviceGuid { get; set; } // Device being used for capture
        private string CurrentCaptureDeviceName { get; set; } // Friendly device name
        private Guid CurrentCaptureProductGuid { get; set; } // Stable VID/PID-based GUID for the capturing device
        private Dictionary<Guid, Guid> DeviceProductGuids { get; set; } = new Dictionary<Guid, Guid>();
        private List<string> CapturedInputCodes { get; set; } = new List<string>();
        private List<string> CapturedModifierCodes { get; set; } = new List<string>();
        private Timer InputCaptureTimer { get; set; }
        private Timer JoystickPollTimer { get; set; }
        private JoystickState PreviousJoystickState { get; set; }
        private Dictionary<Guid, bool[]> PreviousButtonStatesMap { get; set; } = new Dictionary<Guid, bool[]>();
        private Dictionary<Guid, int> PreviousPOVStateMap { get; set; } = new Dictionary<Guid, int>();
        
        // Snapshot of each axis value at the moment axis capture begins, so we can detect which axis the user actually moved rather than any nonzero value.
        private Dictionary<Guid, Dictionary<string, int>> InitialAxisStatesMap { get; set; } = new Dictionary<Guid, Dictionary<string, int>>();
        
        // Movement (in raw DirectInput units, default range 0..65535) required to consider an axis "deflected enough" to treat as intentional capture.
        private const int AxisCaptureThreshold = 3000;
        
        private int AxisDebugTickCounter;
        private Dictionary<Guid, string> DeviceFriendlyNames { get; set; } = new Dictionary<Guid, string>();
        private Font FormFont { get; set; }
        private List<InputBinding> OriginalBindings { get; set; }

        public frmBindingEditor(ApplicationConfig applicationConfig, BindingConfiguration bindingConfiguration)
        {
            InitializeComponent();

            ApplicationConfig = applicationConfig;
            BindingConfiguration = bindingConfiguration;
            OriginalBindings = DeepCopyBindings(applicationConfig.InputBindings);
            this.Text = $"Configure Bindings - {applicationConfig.DisplayName}";
            this.StartPosition = FormStartPosition.CenterParent;

            FormFont = new Font("Tahoma", 8.25f);
            this.Font = FormFont;

            SetupHIDTab(tabHID);
            btnApply.Click += BtnApply_Click;
            btnReset.Click += BtnReset_Click;
            btnCancel.Click += BtnCancel_Click;
        }

        private void SetupHIDTab(TabPage tab)
        {
            CreateActionSection("Volume Up", "VolumeUp", "HIDButton", tab, 5);
            CreateActionSection("Volume Down", "VolumeDown", "HIDButton", tab, 110);
            CreateActionSection("Mute/Unmute", "Mute", "HIDButton", tab, 215);
            CreateActionSection("Volume Axis (direct level control)", "VolumeAxis", "HIDAxis", tab, 320);
        }

        private void CreateActionSection(string actionDisplay, string actionName, string inputType, TabPage tab, int yPos)
        {
            var grp = new GroupBox();
            grp.Name = $"grp{actionName}_{inputType}";
            grp.Text = actionDisplay;
            grp.Location = new Point(5, yPos);
            grp.Size = new Size(600, 100);
            grp.Font = FormFont;
            grp.ForeColor = SystemColors.ControlText;

            int xPos = 10;

            // Main Input section
            var lblMain = new Label();
            lblMain.Text = "Input:";
            lblMain.Location = new Point(xPos, 25);
            lblMain.Size = new Size(60, 18);
            lblMain.Font = FormFont;
            lblMain.TextAlign = ContentAlignment.MiddleLeft;
            grp.Controls.Add(lblMain);

            var lblMainDisplay = new Label();
            lblMainDisplay.Name = $"lblMainDisplay_{actionName}_{inputType}";
            lblMainDisplay.Text = "(none)";
            lblMainDisplay.Location = new Point(xPos + 65, 25);
            lblMainDisplay.Size = new Size(450, 18);
            lblMainDisplay.BorderStyle = BorderStyle.Fixed3D;
            lblMainDisplay.Font = FormFont;
            lblMainDisplay.AutoEllipsis = true;
            lblMainDisplay.BackColor = Color.White;
            lblMainDisplay.ForeColor = Color.Black;
            lblMainDisplay.Cursor = Cursors.Hand;
            lblMainDisplay.Click += (s, e) => StartCapture(actionName, inputType, "Main");
            grp.Controls.Add(lblMainDisplay);

            var btnConfirmMain = new Button();
            btnConfirmMain.Name = $"btnConfirmMain_{actionName}_{inputType}";
            btnConfirmMain.Text = "✓";
            btnConfirmMain.Size = new Size(28, 22);
            btnConfirmMain.Location = new Point(xPos + 520, 23);
            btnConfirmMain.Enabled = false;
            btnConfirmMain.Click += (s, e) => BtnConfirmMain_Click(actionName, inputType);
            grp.Controls.Add(btnConfirmMain);

            var btnClearMain = new Button();
            btnClearMain.Name = $"btnClearMain_{actionName}_{inputType}";
            btnClearMain.Text = "✕";
            btnClearMain.Size = new Size(28, 22);
            btnClearMain.Location = new Point(xPos + 553, 23);
            btnClearMain.Enabled = false;
            btnClearMain.Click += (s, e) => BtnClearMain_Click(actionName, inputType);
            grp.Controls.Add(btnClearMain);

            // Modifiers section (same line)
            var lblMod = new Label();
            lblMod.Text = "Modifier:";
            lblMod.Location = new Point(xPos, 55);
            lblMod.Size = new Size(60, 18);
            lblMod.Font = FormFont;
            lblMod.TextAlign = ContentAlignment.MiddleLeft;
            grp.Controls.Add(lblMod);

            var lblModDisplay = new Label();
            lblModDisplay.Name = $"lblModDisplay_{actionName}_{inputType}";
            lblModDisplay.Text = "(none)";
            lblModDisplay.Location = new Point(xPos + 65, 55);
            lblModDisplay.Size = new Size(450, 18);
            lblModDisplay.BorderStyle = BorderStyle.Fixed3D;
            lblModDisplay.Font = FormFont;
            lblModDisplay.AutoEllipsis = true;
            lblModDisplay.BackColor = Color.White;
            lblModDisplay.ForeColor = Color.Black;
            lblModDisplay.Cursor = Cursors.Hand;
            lblModDisplay.Click += (s, e) => StartCapture(actionName, inputType, "Modifier");
            grp.Controls.Add(lblModDisplay);

            var btnConfirmMod = new Button();
            btnConfirmMod.Name = $"btnConfirmMod_{actionName}_{inputType}";
            btnConfirmMod.Text = "✓";
            btnConfirmMod.Size = new Size(28, 22);
            btnConfirmMod.Location = new Point(xPos + 520, 53);
            btnConfirmMod.Enabled = false;
            btnConfirmMod.Click += (s, e) => BtnConfirmMod_Click(actionName, inputType);
            grp.Controls.Add(btnConfirmMod);

            var btnClearMod = new Button();
            btnClearMod.Name = $"btnClearMod_{actionName}_{inputType}";
            btnClearMod.Text = "✕";
            btnClearMod.Size = new Size(28, 22);
            btnClearMod.Location = new Point(xPos + 553, 53);
            btnClearMod.Enabled = false;
            btnClearMod.Click += (s, e) => BtnClearMod_Click(actionName, inputType);
            grp.Controls.Add(btnClearMod);

            // Status label
            var lblStatus = new Label();
            lblStatus.Name = $"lblStatus_{actionName}_{inputType}";
            lblStatus.Text = "";
            lblStatus.Location = new Point(xPos, 77);
            lblStatus.Size = new Size(400, 20);
            lblStatus.ForeColor = Color.Blue;
            lblStatus.AutoSize = false;
            grp.Controls.Add(lblStatus);

            // Axis-only: invert checkbox. Persisted on the binding so throttles
            // that report physical-down as axis-zero can be flipped by the user.
            if (inputType == "HIDAxis")
            {
                var chkInvert = new CheckBox();
                chkInvert.Name = $"chkInvert_{actionName}_{inputType}";
                chkInvert.Text = "Invert axis";
                chkInvert.Location = new Point(xPos + 430, 77);
                chkInvert.Size = new Size(150, 20);
                chkInvert.Font = FormFont;
                chkInvert.CheckedChanged += (s, e) => InvertCheckbox_Changed(actionName, inputType, chkInvert.Checked);
                grp.Controls.Add(chkInvert);
            }

            tab.Controls.Add(grp);
        }

        private void InvertCheckbox_Changed(string actionName, string inputType, bool isChecked)
        {
            var binding = ApplicationConfig.GetBindingsForAction(actionName)
                .FirstOrDefault(b => b.InputType == inputType);
            
            if (binding == null)
                return;

            binding.Inverted = isChecked;
            log.Debug($"{actionName}/{inputType} Inverted set to {isChecked}");
        }

        private void StartCapture(string actionName, string inputType, string captureMode)
        {
            CurrentCaptureMode = captureMode;
            CurrentCaptureAction = actionName;
            CurrentCaptureInputType = inputType;

            (captureMode == "Main" 
                ? CapturedInputCodes 
                : CapturedModifierCodes).Clear();

            var displayLabelName = captureMode == "Main" 
                ? $"lblMainDisplay_{actionName}_{inputType}" 
                : $"lblModDisplay_{actionName}_{inputType}";

            var statusLabelName = $"lblStatus_{actionName}_{inputType}";

            var displayLabel = FindControlByName(displayLabelName) as Label;
            var statusLabel = FindControlByName(statusLabelName) as Label;

            if (displayLabel != null)
            {
                displayLabel.BackColor = Color.Yellow;
                displayLabel.Text = "...";
            }

            if (statusLabel != null)
            {
                statusLabel.Text = captureMode == "Main"
                    ? "Waiting for input"
                    : "Waiting for modifiers";
                statusLabel.ForeColor = Color.Red;
            }

            DisableGroupControls(actionName, inputType, captureMode, false);

            if (inputType == "HIDButton" || inputType == "HIDAxis")
            {
                InitializeJoystickCapture();
                InitializePreviousButtonStatesForAll();

                if (inputType == "HIDAxis")
                {
                    InitializeAxisSnapshotsForAll();
                }

                JoystickPollTimer.Start();
            }

            InputCaptureTimer.Start();
        }

        private void DisableGroupControls(string actionName, string inputType, string captureMode, bool enable)
        {
            string activeGroupName = $"grp{actionName}_{inputType}";

            // Disable/enable bottom form buttons
            btnApply.Enabled = enable;
            btnReset.Enabled = enable;
            btnCancel.Enabled = enable;

            foreach (var control in this.Controls)
            {
                if (control is TabControl tabControl)
                {
                    foreach (TabPage tab in tabControl.TabPages)
                    {
                        foreach (Control tabChild in tab.Controls)
                        {
                            if (!(tabChild is GroupBox grp))
                            {
                                continue;
                            }

                            bool isActiveGroup = grp.Name == activeGroupName;

                            if (!isActiveGroup)
                            {
                                // Non-active groups: disable entirely during capture, re-enable on confirm/clear
                                grp.Enabled = enable;
                            }
                            else
                            {
                                // Active group: selectively enable/disable controls
                                foreach (Control ctrl in grp.Controls)
                                {
                                    if (captureMode == "Main")
                                    {
                                        // Confirm/clear for Main: enabled during capture, disabled after
                                        if (ctrl.Name == $"btnConfirmMain_{actionName}_{inputType}" ||
                                            ctrl.Name == $"btnClearMain_{actionName}_{inputType}")
                                        {
                                            ctrl.Enabled = !enable;
                                        }
                                        // All modifier controls in this group: disabled during capture
                                        else if (ctrl.Name.Contains("Mod"))
                                        {
                                            ctrl.Enabled = enable;
                                        }
                                        // Main display label stays as-is (clickable for capture)
                                        // Status label stays enabled
                                    }
                                    else // Modifier
                                    {
                                        // Confirm/clear for Modifier: enabled during capture, disabled after
                                        if (ctrl.Name == $"btnConfirmMod_{actionName}_{inputType}" ||
                                            ctrl.Name == $"btnClearMod_{actionName}_{inputType}")
                                        {
                                            ctrl.Enabled = !enable;
                                        }
                                        // All main controls in this group: disabled during capture
                                        else if (ctrl.Name.Contains("Main"))
                                        {
                                            ctrl.Enabled = enable;
                                        }
                                        // Modifier display label stays as-is
                                        // Status label stays enabled
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            try
            {
                InputDeviceManager = new InputDeviceManager();
                
                InputCaptureTimer = new Timer();
                InputCaptureTimer.Interval = 50;
                InputCaptureTimer.Tick += InputCaptureTimer_Tick;

                
                JoystickPollTimer = new Timer();
                JoystickPollTimer.Interval = 20;
                JoystickPollTimer.Tick += JoystickPollTimer_Tick;

                LoadAttachedDevices();

                log.Debug($"Current bindings for {ApplicationConfig.DisplayName}:");

                foreach (var binding in ApplicationConfig.InputBindings)
                {
                    log.Debug($"  {binding.Action} ({binding.InputType}): {string.Join(",", binding.InputCodes)} [Enabled: {binding.Enabled}]");
                }
                
                PopulateBindingsFromConfig();
            }
            catch (Exception ex)
            {
                log.Debug($"Error: {ex}");
            }
        }

        private void LoadAttachedDevices()
        {
            AttachedDevices = InputDeviceManager.GetGameControlDevices();

            DeviceFriendlyNames.Clear();
            DeviceProductGuids.Clear();

            foreach (var device in AttachedDevices)
            {
                DeviceFriendlyNames[device.InstanceGuid] = GetFriendlyDeviceName(device.ProductName);
                DeviceProductGuids[device.InstanceGuid] = device.ProductGuid;
            }

            log.Debug($"Loaded {AttachedDevices.Count} HID devices");
        }

        private List<Control> FindAllControls(Control parent, string namePrefix)
        {
            var results = new List<Control>();
            foreach (Control child in parent.Controls)
            {
                if (child.Name.StartsWith(namePrefix))
                {
                    results.Add(child);
                }
                results.AddRange(FindAllControls(child, namePrefix));
            }
            return results;
        }

        private void PopulateBindingsFromConfig()
        {
            foreach (var binding in ApplicationConfig.InputBindings)
            {
                // Only populate valid, non-empty bindings
                if (binding.InputCodes != null && binding.InputCodes.Count > 0)
                {
                    UpdateBindingDisplay(binding);
                }
            }
        }

        private void UpdateBindingDisplay(InputBinding binding)
        {
            string mainLabelName = $"lblMainDisplay_{binding.Action}_{binding.InputType}";
            string modLabelName = $"lblModDisplay_{binding.Action}_{binding.InputType}";

            if (binding.InputType == "HIDAxis")
            {
                var chk = FindControlByName($"chkInvert_{binding.Action}_{binding.InputType}") as CheckBox;
                if (chk != null)
                    chk.Checked = binding.Inverted;
            }

            foreach (Control control in this.Controls)
            {
                if (control is TabControl tabControl)
                {
                    foreach (TabPage tab in tabControl.TabPages)
                    {
                        Control mainFound = FindControlRecursive(tab, mainLabelName);
                        if (mainFound is Label mainLbl && binding.InputCodes.Count > 0)
                        {
                            mainLbl.Text = string.Join(" | ", binding.InputCodes);
                            mainLbl.BackColor = Color.FromArgb(200, 255, 190);;
                        }

                        Control modFound = FindControlRecursive(tab, modLabelName);
                        if (modFound is Label modLbl)
                        {
                            if (binding.ModifierCodes.Count > 0)
                            {
                                modLbl.Text = string.Join(" | ", binding.ModifierCodes);
                                modLbl.BackColor = Color.FromArgb(200, 255, 190);;
                            }
                            else
                            {
                                modLbl.Text = "(none)";
                                modLbl.BackColor = Color.White;
                            }
                        }
                    }
                }
            }
        }

        private Control FindControlRecursive(Control parent, string name)
        {
            if (parent.Name == name) return parent;

            foreach (Control child in parent.Controls)
            {
                Control result = FindControlRecursive(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private string GetFriendlyDeviceName(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName))
                return "Unknown";

            var name = System.Text.RegularExpressions.Regex.Replace(productName, @"[^a-zA-Z0-9]", "_");
            name = name.Trim('_');

            return string.IsNullOrEmpty(name) ? "Device" : name;
        }

        private void InitializeJoystickCapture()
        {
            try
            {
                if (CurrentCaptureInputType == "HIDButton" || CurrentCaptureInputType == "HIDAxis")
                {
                    foreach (var device in AttachedDevices)
                    {
                        if (!ActiveJoysticks.ContainsKey(device.InstanceGuid))
                        {
                            try
                            {
                                var joystick = InputDeviceManager.CreateJoystick(device.InstanceGuid);
                                joystick.Acquire();
                                ActiveJoysticks[device.InstanceGuid] = joystick;

                                string friendlyName = GetFriendlyDeviceName(device.ProductName);
                                log.Debug($"Acquired device: {friendlyName} ({device.InstanceGuid})");
                            }
                            catch (Exception ex)
                            {
                                log.Error(ex, "Failed to acquire device {0}", device.ProductName);
                            }
                        }
                    }
                    
                    if (CurrentCaptureDeviceGuid == Guid.Empty && AttachedDevices.Count > 0)
                    {
                        CurrentCaptureDeviceGuid = AttachedDevices[0].InstanceGuid;
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error initializing joystick capture");
            }
        }

        private void InitializePreviousButtonStatesForAll()
        {
            try
            {
                PreviousButtonStatesMap.Clear();
                PreviousPOVStateMap.Clear();

                // Initialize state snapshots for all active joysticks
                // Some buttons (switches) may already pre on/pressed so we need to capture their initial state to avoid false positives on first poll
                foreach (var kvp in ActiveJoysticks)
                {
                    Guid deviceGuid = kvp.Key;
                    var joystick = kvp.Value;

                    try
                    {                        
                        System.Threading.Thread.Sleep(50);
                        var state = joystick.GetCurrentState();
                        
                        if (state.Buttons != null)
                        {
                            var buttonStates = new bool[state.Buttons.Length];
                            Array.Copy(state.Buttons, buttonStates, state.Buttons.Length);
                            PreviousButtonStatesMap[deviceGuid] = buttonStates;
                        }

                        // Initialize POV state for this device
                        if (state.PointOfViewControllers != null && state.PointOfViewControllers.Length > 0)
                        {
                            PreviousPOVStateMap[deviceGuid] = state.PointOfViewControllers[0];
                        }
                        else
                        {
                            PreviousPOVStateMap[deviceGuid] = -1;
                        }

                        var friendlyName = DeviceFriendlyNames.TryGetValue(deviceGuid, out var name) ? name : "Unknown";
                        log.Debug($"Device '{friendlyName}' initialized - Buttons: {state.Buttons?.Length ?? 0}, POV: {PreviousPOVStateMap[deviceGuid]}");
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex, "Error initializing device {0}", deviceGuid);
                    }
                }

                log.Debug($"Initialized state maps for {PreviousButtonStatesMap.Count} devices");
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error initializing previous button states for all devices");
            }
        }

        private void InitializeAxisSnapshotsForAll()
        {
            InitialAxisStatesMap.Clear();
            foreach (var kvp in ActiveJoysticks)
            {
                try
                {
                    // Poll before sampling — some drivers don't update axis values
                    // until Poll() is called, which would otherwise freeze our
                    // snapshot at stale values and make delta detection impossible.
                    try { kvp.Value.Poll(); } catch { }
                    var state = kvp.Value.GetCurrentState();
                    var values = ReadAxisValues(state);
                    InitialAxisStatesMap[kvp.Key] = values;

                    var friendlyName = DeviceFriendlyNames.TryGetValue(kvp.Key, out var fn) ? fn : "Unknown";
                    var snapshot = string.Join(", ", values.Select(p => $"{p.Key}={p.Value}"));
                    log.Debug($"Axis snapshot for {friendlyName}: {snapshot}");
                }
                catch (Exception ex)
                {
                    log.Error(ex, "Error snapshotting axes for device {0}", kvp.Key);
                }
            }
            log.Debug($"Axis snapshot initialized for {InitialAxisStatesMap.Count} device(s)");
        }

        /// <summary>
        /// Reads every named axis off a JoystickState into a dictionary keyed by
        /// the short axis name ("Axis_X", "Slider_0", ...). Used both for the
        /// initial snapshot and for live comparison during capture.
        /// </summary>
        private static Dictionary<string, int> ReadAxisValues(JoystickState state)
        {
            var axes = new Dictionary<string, int>
            {
                { "Axis_X", state.X },
                { "Axis_Y", state.Y },
                { "Axis_Z", state.Z },
                { "Axis_RX", state.RotationX },
                { "Axis_RY", state.RotationY },
                { "Axis_RZ", state.RotationZ },
            };

            if (state.Sliders != null)
            {
                for (int i = 0; i < state.Sliders.Length; i++)
                    axes[$"Slider_{i}"] = state.Sliders[i];
            }

            return axes;
        }

        private void JoystickPollTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (CurrentCaptureInputType != "HIDButton" && CurrentCaptureInputType != "HIDAxis")
                    return;
                
                foreach (var kvp in ActiveJoysticks)
                {
                    Guid deviceGuid = kvp.Key;
                    var joystick = kvp.Value;

                    try
                    {
                        try { joystick.Poll(); } catch { }
                        var state = joystick.GetCurrentState();
                        PollJoystickState(state, deviceGuid);

                        // For HIDAxis we stop after the first detection so the user
                        // can confirm the captured axis. For HIDButton / POV capture
                        // we keep polling so the display updates live as the user
                        // presses a different button to correct a mispress.
                        if (CurrentCaptureInputType == "HIDAxis"
                            && CurrentCaptureMode == "Main"
                            && CapturedInputCodes.Count > 0)
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex, "Error polling device {0}", deviceGuid);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error in joystick poll timer");
            }
        }

        private void PollJoystickState(JoystickState state, Guid deviceGuid)
        {
            // Detect STATE CHANGES - only capture buttons that transitioned from unpressed to pressed
            
            var friendlyName = DeviceFriendlyNames.TryGetValue(deviceGuid, out var name) ? name : "Unknown";

            // Axis capture: compare current axis values against the snapshot taken
            // at StartCapture; the first axis to move past the threshold wins.
            if (CurrentCaptureInputType == "HIDAxis"
                && CurrentCaptureMode == "Main"
                && CapturedInputCodes.Count == 0)
            {
                if (!InitialAxisStatesMap.TryGetValue(deviceGuid, out var initialAxes))
                {
                    log.Debug($"No axis snapshot for device {friendlyName} ({deviceGuid}) — was InitializeAxisSnapshotsForAll called?");
                    return;
                }

                var currentAxes = ReadAxisValues(state);

                // Throttled trace so we can see live axis values in the debug log
                // while the user is wiggling the stick. One device per tick is fine.
                AxisDebugTickCounter++;
                if (AxisDebugTickCounter % 25 == 0)
                {
                    var deltas = string.Join(", ", currentAxes.Select(p =>
                    {
                        initialAxes.TryGetValue(p.Key, out int init);
                        return $"{p.Key}={p.Value}(Δ{p.Value - init})";
                    }));
                    log.Debug($"{friendlyName} axes: {deltas}");
                }

                foreach (var pair in currentAxes)
                {
                    if (!initialAxes.TryGetValue(pair.Key, out int initialValue))
                        continue;
                    if (Math.Abs(pair.Value - initialValue) < AxisCaptureThreshold)
                        continue;

                    string axisCode = $"{friendlyName}_{pair.Key}";
                    CapturedInputCodes.Add(axisCode);
                    CurrentCaptureDeviceName = friendlyName;
                    CurrentCaptureProductGuid = DeviceProductGuids.TryGetValue(deviceGuid, out var pgAxis) ? pgAxis : Guid.Empty;
                    log.Debug($"Axis detected from {friendlyName}: {axisCode} (value {pair.Value}, initial {initialValue}, Δ {pair.Value - initialValue})");
                    UpdateCaptureDisplay();
                    return;
                }
                return;
            }

            // Get per-device previous states
            if (!PreviousButtonStatesMap.TryGetValue(deviceGuid, out var previousButtonStates))
                return;
            if (!PreviousPOVStateMap.TryGetValue(deviceGuid, out var previousPOVState))
                return;

            // For main input: replace the captured code on every new press so the
            // user can correct a mispress by just pressing the intended button.
            if (CurrentCaptureMode == "Main")
            {
                var buttons = state.Buttons;
                if (buttons != null && previousButtonStates != null)
                {
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        if (!previousButtonStates[i] && buttons[i])
                        {
                            string buttonCode = $"{friendlyName}_Button_{i}";
                            CapturedInputCodes.Clear();
                            CapturedInputCodes.Add(buttonCode);
                            log.Debug($"Main input updated from {friendlyName}: {buttonCode}");

                            previousButtonStates[i] = true;
                            CurrentCaptureDeviceName = friendlyName;
                            CurrentCaptureProductGuid = DeviceProductGuids.TryGetValue(deviceGuid, out var pgBtn) ? pgBtn : Guid.Empty;
                            UpdateCaptureDisplay();
                            return;
                        }
                    }

                    Array.Copy(buttons, previousButtonStates, buttons.Length);
                }

                var pov = state.PointOfViewControllers;
                if (pov != null && pov.Length > 0)
                {
                    int povValue = pov[0];
                    if (previousPOVState == -1 && povValue >= 0)
                    {
                        string povCode = GetPOVCode(povValue);
                        if (!string.IsNullOrEmpty(povCode))
                        {
                            string fullPOVCode = $"{friendlyName}_{povCode}";
                            CapturedInputCodes.Clear();
                            CapturedInputCodes.Add(fullPOVCode);
                            log.Debug($"Main input updated from {friendlyName}: {fullPOVCode}");
                            PreviousPOVStateMap[deviceGuid] = povValue;
                            CurrentCaptureDeviceName = friendlyName;
                            CurrentCaptureProductGuid = DeviceProductGuids.TryGetValue(deviceGuid, out var pgPov) ? pgPov : Guid.Empty;
                            UpdateCaptureDisplay();
                            return; // Stop after first input in this tick
                        }
                    }
                    // POV direction changed without releasing to neutral — treat
                    // as a new press so the user can swap directions live.
                    else if (previousPOVState >= 0 && povValue >= 0 && previousPOVState != povValue)
                    {
                        string povCode = GetPOVCode(povValue);
                        if (!string.IsNullOrEmpty(povCode))
                        {
                            string fullPOVCode = $"{friendlyName}_{povCode}";
                            CapturedInputCodes.Clear();
                            CapturedInputCodes.Add(fullPOVCode);
                            log.Debug($"Main input updated from {friendlyName}: {fullPOVCode}");
                            PreviousPOVStateMap[deviceGuid] = povValue;
                            CurrentCaptureDeviceName = friendlyName;
                            CurrentCaptureProductGuid = DeviceProductGuids.TryGetValue(deviceGuid, out var pgPovChg) ? pgPovChg : Guid.Empty;
                            UpdateCaptureDisplay();
                            return;
                        }
                    }
                    PreviousPOVStateMap[deviceGuid] = povValue;
                }
            }
            // For modifiers: allow multiple buttons, but only detect state changes
            else if (CurrentCaptureMode == "Modifier")
            {
                // Check buttons for state changes (false -> true)
                var buttons = state.Buttons;
                if (buttons != null && previousButtonStates != null)
                {
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        // Detect transition: was NOT pressed before, IS pressed now
                        if (!previousButtonStates[i] && buttons[i])
                        {
                            string buttonCode = $"{friendlyName}_Button_{i}";
                            if (!CapturedModifierCodes.Contains(buttonCode))
                            {
                                CapturedModifierCodes.Add(buttonCode);
                                log.Debug($"Modifier detected from {friendlyName}: {buttonCode}");
                            }
                        }
                    }
                    // Update all button states for next comparison
                    Array.Copy(buttons, previousButtonStates, buttons.Length);
                }

                // Check POV hat for state changes
                var pov = state.PointOfViewControllers;
                if (pov != null && pov.Length > 0)
                {
                    int povValue = pov[0];
                    if (previousPOVState == -1 && povValue >= 0)
                    {
                        string povCode = GetPOVCode(povValue);
                        if (!string.IsNullOrEmpty(povCode))
                        {
                            string fullPOVCode = $"{friendlyName}_{povCode}";
                            if (!CapturedModifierCodes.Contains(fullPOVCode))
                            {
                                CapturedModifierCodes.Add(fullPOVCode);
                                log.Debug($"Modifier detected from {friendlyName}: {fullPOVCode}");
                            }
                        }
                        PreviousPOVStateMap[deviceGuid] = povValue;
                    }
                    else if (previousPOVState >= 0 && povValue >= 0 && previousPOVState != povValue)
                    {
                        // POV hat changed to different direction
                        string povCode = GetPOVCode(povValue);
                        if (!string.IsNullOrEmpty(povCode))
                        {
                            string fullPOVCode = $"{friendlyName}_{povCode}";
                            if (!CapturedModifierCodes.Contains(fullPOVCode))
                            {
                                CapturedModifierCodes.Add(fullPOVCode);
                                log.Debug($"Modifier detected from {friendlyName}: {fullPOVCode}");
                            }
                        }
                        PreviousPOVStateMap[deviceGuid] = povValue;
                    }
                }
            }

            UpdateCaptureDisplay();
        }

        private string GetPOVCode(int povValue)
        {
            // POV values: 0=N, 4500=NE, 9000=E, 13500=SE, 18000=S, 22500=SW, 27000=W, 31500=NW

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

        private void InputCaptureTimer_Tick(object sender, EventArgs e)
        {
            UpdateCaptureDisplay();
        }

        private void UpdateCaptureDisplay()
        {
            if (CurrentCaptureMode == "Main")
            {
                var mainLabelName = $"lblMainDisplay_{CurrentCaptureAction}_{CurrentCaptureInputType}";
                var mainLabel = FindControlByName(mainLabelName) as Label;
                
                if (mainLabel != null && CapturedInputCodes.Count > 0)
                {
                    mainLabel.Text = string.Join(" | ", CapturedInputCodes);
                }
            }
            else if (CurrentCaptureMode == "Modifier")
            {
                var modLabelName = $"lblModDisplay_{CurrentCaptureAction}_{CurrentCaptureInputType}";
                var modLabel = FindControlByName(modLabelName) as Label;

                if (modLabel != null && CapturedModifierCodes.Count > 0)
                {
                    modLabel.Text = string.Join(" | ", CapturedModifierCodes);
                }
            }
        }

        private void BtnConfirmMain_Click(string actionName, string inputType)
        {
            if (CapturedInputCodes.Count == 0)
            {
                var mainLabelName = $"lblMainDisplay_{actionName}_{inputType}";
                var mainLabel = FindControlByName(mainLabelName) as Label;
                
                if (mainLabel != null)
                {
                    mainLabel.BackColor = Color.FromArgb(200, 255, 190);;
                }

                return;
            }

            InputCaptureTimer.Stop();
            JoystickPollTimer.Stop();

            var binding = ApplicationConfig.GetBindingsForAction(actionName)
                .FirstOrDefault(b => b.InputType == inputType);

            if (binding == null)
            {
                binding = ApplicationConfig.CreateBinding(actionName, inputType);
            }

            binding.InputCodes = new List<string>(CapturedInputCodes);
            binding.Description = $"{actionName} ({string.Join(" | ", CapturedInputCodes)})";

            if ((inputType == "HIDButton" || inputType == "HIDAxis") && CurrentCaptureProductGuid != Guid.Empty)
            {
                binding.ProductGuid = CurrentCaptureProductGuid.ToString();
                log.Debug($"Stored ProductGuid {binding.ProductGuid} on binding {binding.Description}");
            }

            if (inputType == "HIDAxis")
            {
                var chk = FindControlByName($"chkInvert_{actionName}_{inputType}") as CheckBox;
                if (chk != null)
                    binding.Inverted = chk.Checked;
            }

            UpdateBindingDisplay(binding);

            var statusLabelName = $"lblStatus_{actionName}_{inputType}";
            var statusLabel = FindControlByName(statusLabelName) as Label;
            
            if (statusLabel != null)
            {
                statusLabel.Text = "Input saved";
                statusLabel.ForeColor = Color.Green;
            }

            CapturedInputCodes.Clear();
            CurrentCaptureProductGuid = Guid.Empty;
            DisableGroupControls(actionName, inputType, "Main", true);
        }

        private void BtnConfirmMod_Click(string actionName, string inputType)
        {
            InputCaptureTimer.Stop();
            JoystickPollTimer.Stop();

            if (CapturedModifierCodes.Count == 0)
            {
                var modLabelName = $"lblModDisplay_{actionName}_{inputType}";
                var modLabel = FindControlByName(modLabelName) as Label;

                if (modLabel != null)
                {
                    modLabel.BackColor = Color.LightCoral;
                }

                DisableGroupControls(actionName, inputType, "Modifier", true);
                return;
            }

            var binding = ApplicationConfig.GetBindingsForAction(actionName)
                .FirstOrDefault(b => b.InputType == inputType);

            if (binding != null)
            {
                binding.ModifierCodes = new List<string>(CapturedModifierCodes);
                UpdateBindingDisplay(binding);

                var statusLabelName = $"lblStatus_{actionName}_{inputType}";
                var statusLabel = FindControlByName(statusLabelName) as Label;

                if (statusLabel != null)
                {
                    statusLabel.Text = "Modifiers saved";
                    statusLabel.ForeColor = Color.Green;
                }
            }

            CapturedModifierCodes.Clear();            
            DisableGroupControls(actionName, inputType, "Modifier", true);
        }

        private void BtnClearMain_Click(string actionName, string inputType)
        {
            InputCaptureTimer.Stop();
            JoystickPollTimer.Stop();

            var binding = ApplicationConfig.GetBindingsForAction(actionName)
                .FirstOrDefault(b => b.InputType == inputType);

            if (binding != null)
            {
                binding.InputCodes.Clear();

                var mainLabelName = $"lblMainDisplay_{actionName}_{inputType}";
                var mainLabel = FindControlByName(mainLabelName) as Label;
                
                if (mainLabel != null)
                {
                    mainLabel.Text = "(none)";
                    mainLabel.BackColor = Color.White;
                }
            }

            CapturedInputCodes.Clear();

            var statusLabelName = $"lblStatus_{actionName}_{inputType}";
            var statusLabel = FindControlByName(statusLabelName) as Label;
            
            if (statusLabel != null)
            {
                statusLabel.Text = "";
                statusLabel.ForeColor = Color.Blue;
            }
            
            DisableGroupControls(actionName, inputType, "Main", true);
        }

        private void BtnClearMod_Click(string actionName, string inputType)
        {
            InputCaptureTimer.Stop();
            JoystickPollTimer.Stop();

            var binding = ApplicationConfig.GetBindingsForAction(actionName)
                .FirstOrDefault(b => b.InputType == inputType);

            if (binding != null)
            {
                binding.ModifierCodes.Clear();

                var modLabelName = $"lblModDisplay_{actionName}_{inputType}";
                var modLabel = FindControlByName(modLabelName) as Label;

                if (modLabel != null)
                {
                    modLabel.Text = "(none)";
                    modLabel.BackColor = Color.White;
                }
            }

            CapturedModifierCodes.Clear();

            var statusLabelName = $"lblStatus_{actionName}_{inputType}";
            var statusLabel = FindControlByName(statusLabelName) as Label;

            if (statusLabel != null)
            {
                statusLabel.Text = "";
                statusLabel.ForeColor = Color.Blue;
            }
            
            DisableGroupControls(actionName, inputType, "Modifier", true);
        }       

        private Control FindControlByName(string name)
        {
            foreach (Control control in this.Controls)
            {
                if (control is TabControl tabControl)
                {
                    foreach (TabPage tab in tabControl.TabPages)
                    {
                        var found = FindControlRecursive(tab, name);
                        if (found != null) return found;
                    }
                }
            }
            return null;
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            try
            {
                InputCaptureTimer.Stop();
                JoystickPollTimer.Stop();

                ApplicationConfig.InputBindings.Clear();
                ResetAllBindingDisplays();
            }
            catch (Exception ex)
            {
                log.Debug($"Reset error: {ex}");
                MessageBox.Show($"Error resetting bindings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ResetAllBindingDisplays()
        {
            string[] actions = { "VolumeUp", "VolumeDown", "Mute", "VolumeAxis" };
            string[] inputTypes = { "HIDButton", "HIDAxis" };

            foreach (var action in actions)
            {
                foreach (var type in inputTypes)
                {
                    var mainLabel = FindControlByName($"lblMainDisplay_{action}_{type}") as Label;
                    if (mainLabel != null)
                    {
                        mainLabel.Text = "(none)";
                        mainLabel.BackColor = Color.White;
                    }

                    var modLabel = FindControlByName($"lblModDisplay_{action}_{type}") as Label;
                    if (modLabel != null)
                    {
                        modLabel.Text = "(none)";
                        modLabel.BackColor = Color.White;
                    }

                    var statusLabel = FindControlByName($"lblStatus_{action}_{type}") as Label;
                    if (statusLabel != null)
                    {
                        statusLabel.Text = "";
                        statusLabel.ForeColor = Color.Blue;
                    }

                    var chkInvert = FindControlByName($"chkInvert_{action}_{type}") as CheckBox;
                    if (chkInvert != null)
                        chkInvert.Checked = false;
                }
            }
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            try
            {
                InputCaptureTimer.Stop();
                JoystickPollTimer.Stop();

                var conflicts = FindAllBindingConflicts();
                if (conflicts.Count > 0)
                {
                    var message = "The following bindings conflict with other applications:\n\n";
                    foreach (var c in conflicts)
                    {
                        message += $"  • \"{c.Item1}\" ({c.Item2}) is also bound to \"{c.Item3}\" ({c.Item4})\n";
                    }
                    message += "\nDo you want to save anyway?";

                    var result = MessageBox.Show(message, "Binding Conflicts",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (result != DialogResult.Yes)
                        return;
                }

                BindingConfiguration.UpdateConfiguration(ApplicationConfig);
                BindingConfiguration.Save();

                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                log.Debug($"Save error: {ex}");
            }
        }

        private List<Tuple<string, string, string, string>> FindAllBindingConflicts()
        {
            var conflicts = new List<Tuple<string, string, string, string>>();
            var allConfigs = BindingConfiguration.GetAllConfigurations();

            for (int i = 0; i < ApplicationConfig.InputBindings.Count; i++)
            {
                var binding = ApplicationConfig.InputBindings[i];
                if (binding.InputCodes == null || binding.InputCodes.Count == 0)
                    continue;

                var myMods = binding.ModifierCodes ?? new List<string>();

                for (int j = i + 1; j < ApplicationConfig.InputBindings.Count; j++)
                {
                    var other = ApplicationConfig.InputBindings[j];
                    if (other.InputCodes == null || other.InputCodes.Count == 0)
                        continue;

                    var otherMods = other.ModifierCodes ?? new List<string>();

                    if (other.InputCodes.Count == binding.InputCodes.Count &&
                        !other.InputCodes.Except(binding.InputCodes).Any() &&
                        myMods.Count == otherMods.Count &&
                        !otherMods.Except(myMods).Any())
                    {
                        conflicts.Add(Tuple.Create(
                            string.Join(" | ", binding.InputCodes),
                            binding.Action,
                            ApplicationConfig.DisplayName,
                            other.Action));
                    }
                }

                foreach (var otherConfig in allConfigs)
                {
                    if (otherConfig.ConfigId == ApplicationConfig.ConfigId)
                        continue;

                    foreach (var otherBinding in otherConfig.InputBindings)
                    {
                        if (!otherBinding.Enabled || otherBinding.InputCodes == null || otherBinding.InputCodes.Count == 0)
                            continue;

                        var otherMods = otherBinding.ModifierCodes ?? new List<string>();

                        if (otherBinding.InputCodes.Count == binding.InputCodes.Count &&
                            !otherBinding.InputCodes.Except(binding.InputCodes).Any() &&
                            myMods.Count == otherMods.Count &&
                            !otherMods.Except(myMods).Any())
                        {
                            conflicts.Add(Tuple.Create(
                                string.Join(" | ", binding.InputCodes),
                                binding.Action,
                                otherConfig.DisplayName,
                                otherBinding.Action));
                        }
                    }
                }
            }

            return conflicts;
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (InputCaptureTimer != null)
            {
                InputCaptureTimer.Stop();
            }

            if (JoystickPollTimer != null)
            {
                JoystickPollTimer.Stop();
            }

            ApplicationConfig.InputBindings.Clear();
            ApplicationConfig.InputBindings.AddRange(OriginalBindings);

            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private static List<InputBinding> DeepCopyBindings(List<InputBinding> bindings)
        {
            var copy = new List<InputBinding>();
            foreach (var b in bindings)
            {
                copy.Add(new InputBinding
                {
                    BindingId = b.BindingId,
                    Action = b.Action,
                    InputType = b.InputType,
                    InputCodes = new List<string>(b.InputCodes),
                    ModifierCodes = new List<string>(b.ModifierCodes),
                    Description = b.Description,
                    Enabled = b.Enabled,
                    Inverted = b.Inverted,
                    ProductGuid = b.ProductGuid
                });
            }
            return copy;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            if (this.DialogResult != DialogResult.OK)
            {
                ApplicationConfig.InputBindings.Clear();
                ApplicationConfig.InputBindings.AddRange(OriginalBindings);
            }

            if (InputCaptureTimer != null)
            {
                InputCaptureTimer.Stop();
                InputCaptureTimer.Dispose();
            }

            if (JoystickPollTimer != null)
            {
                JoystickPollTimer.Stop();
                JoystickPollTimer.Dispose();
            }

            foreach (var joystick in ActiveJoysticks.Values)
            {
                try
                {
                    joystick.Unacquire();
                    joystick.Dispose();
                }
                catch { }
            }
            ActiveJoysticks.Clear();

            if (InputDeviceManager != null)
            {
                InputDeviceManager.Dispose();
            }

            if (FormFont != null)
            {
                FormFont.Dispose();
            }
        }
    }
}