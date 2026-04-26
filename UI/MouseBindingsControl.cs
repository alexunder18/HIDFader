using NLog;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using HIDMate.Core;
using HIDMate.Input;

namespace HIDMate.UI
{
    /// <summary>
    /// Self-contained UserControl for the Mouse tab on frmApplicationSelector.
    /// Binds HID inputs (buttons, POV hats, axes) to global mouse actions —
    /// movement, clicks, scroll. Mutates BindingConfiguration.MouseBindings
    /// and BindingConfiguration.MouseSensitivity directly and persists each
    /// change immediately (preferences-style auto-save, no apply/cancel).
    ///
    /// Capture state machine and polling are duplicated from frmBindingEditor
    /// rather than shared — extracting an engine would be a separate refactor.
    /// </summary>
    public class MouseBindingsControl : UserControl
    {
        private readonly Logger log = LogManager.GetCurrentClassLogger();

        private static readonly (string Display, string Action, string InputType)[] Sections =
        {
            ("Move Up", "MouseUp", "HIDButton"),
            ("Move Down", "MouseDown", "HIDButton"),
            ("Move Left", "MouseLeft", "HIDButton"),
            ("Move Right", "MouseRight", "HIDButton"),
            ("Mouse X axis (analog left/right)", "MouseAxisX", "HIDAxis"),
            ("Mouse Y axis (analog up/down)", "MouseAxisY", "HIDAxis"),
            ("Left click", "MouseLeftClick", "HIDButton"),
            ("Right click", "MouseRightClick", "HIDButton"),
            ("Scroll up", "MouseScrollUp", "HIDButton"),
            ("Scroll down", "MouseScrollDown", "HIDButton"),
        };

        private BindingConfiguration BindingConfiguration { get; set; }
        private InputDeviceManager InputDeviceManager { get; set; }
        private List<JoystickDevice> AttachedDevices { get; set; } = new List<JoystickDevice>();
        private Dictionary<Guid, Joystick> ActiveJoysticks { get; set; } = new Dictionary<Guid, Joystick>();
        private Dictionary<Guid, string> DeviceFriendlyNames { get; set; } = new Dictionary<Guid, string>();
        private Dictionary<Guid, Guid> DeviceProductGuids { get; set; } = new Dictionary<Guid, Guid>();

        private string CurrentCaptureMode { get; set; } // "Main" or "Modifier"
        private string CurrentCaptureAction { get; set; }
        private string CurrentCaptureInputType { get; set; }
        private string CurrentCaptureDeviceName { get; set; }
        private Guid CurrentCaptureProductGuid { get; set; }
        private List<string> CapturedInputCodes { get; set; } = new List<string>();
        private List<string> CapturedModifierCodes { get; set; } = new List<string>();
        private Timer InputCaptureTimer { get; set; }
        private Timer JoystickPollTimer { get; set; }

        private Dictionary<Guid, bool[]> PreviousButtonStatesMap { get; set; } = new Dictionary<Guid, bool[]>();
        private Dictionary<Guid, int> PreviousPOVStateMap { get; set; } = new Dictionary<Guid, int>();
        private Dictionary<Guid, Dictionary<string, int>> InitialAxisStatesMap { get; set; } = new Dictionary<Guid, Dictionary<string, int>>();
        private const int AxisCaptureThreshold = 3000;
        private int AxisDebugTickCounter;

        private NumericUpDown numSensitivity;
        private Panel sectionsPanel;
        private Font FormFont;

        public MouseBindingsControl()
        {
            FormFont = new Font("Tahoma", 8.25f);
            this.Font = FormFont;
            this.AutoScroll = true;

            InputCaptureTimer = new Timer { Interval = 50 };
            InputCaptureTimer.Tick += (s, e) => UpdateCaptureDisplay();

            JoystickPollTimer = new Timer { Interval = 20 };
            JoystickPollTimer.Tick += JoystickPollTimer_Tick;
        }

        /// <summary>
        /// Wires the control to the live BindingConfiguration. Must be called
        /// once after the parent form has loaded its configuration.
        /// </summary>
        public void Initialize(BindingConfiguration bindingConfiguration)
        {
            BindingConfiguration = bindingConfiguration;
            InputDeviceManager = new InputDeviceManager();
            LoadAttachedDevices();
            BuildLayout();
            PopulateBindings();
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
        }

        private void BuildLayout()
        {
            var header = new Label
            {
                Text = "Bind HID inputs to global mouse actions. Cursor output is system-wide and changes save automatically.",
                Location = new Point(5, 10),
                AutoSize = true,
                Font = FormFont                
            };
            this.Controls.Add(header);

            var lblSens = new Label
            {
                Text = "Mouse speed (1–10):",
                Location = new Point(5, 40),
                Size = new Size(130, 22),
                Font = FormFont,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            this.Controls.Add(lblSens);

            int initialSens = BindingConfiguration.MouseSensitivity > 0
                ? BindingConfiguration.MouseSensitivity
                : 5;

            numSensitivity = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 10,
                Value = Math.Min(10, Math.Max(1, initialSens)),
                Location = new Point(140, 40),
                Size = new Size(60, 22),
                Font = FormFont,
            };

            numSensitivity.ValueChanged += (s, e) =>
            {
                BindingConfiguration.MouseSensitivity = (int)numSensitivity.Value;
                BindingConfiguration.SaveMouseConfiguration();
            };

            this.Controls.Add(numSensitivity);

            sectionsPanel = new Panel
            {
                Location = new Point(0, 75),
                Size = new Size(590, Sections.Length * 105 + 10),
                BorderStyle = BorderStyle.None,
            };

            this.Controls.Add(sectionsPanel);

            int y = 5;
            const int H = 105;

            foreach (var section in Sections)
            {
                CreateActionSection(section.Display, section.Action, section.InputType, y);
                y += H;
            }
        }

        private void PopulateBindings()
        {
            foreach (var binding in BindingConfiguration.MouseBindings)
            {
                if (binding.InputCodes != null && binding.InputCodes.Count > 0)
                {
                    UpdateBindingDisplay(binding);
                }
            }
        }

        // ---- Section UI ----

        private void CreateActionSection(string actionDisplay, string actionName, string inputType, int yPos)
        {
            var grp = new GroupBox
            {
                Name = $"grp{actionName}_{inputType}",
                Text = actionDisplay,
                Location = new Point(5, yPos),
                Size = new Size(580, 100),
                Font = FormFont,
                ForeColor = SystemColors.ControlText,
            };

            int xPos = 10;

            grp.Controls.Add(new Label
            {
                Text = "Input:", Location = new Point(xPos, 25), Size = new Size(60, 18),
                Font = FormFont, TextAlign = ContentAlignment.MiddleLeft,
            });

            var lblMain = new Label
            {
                Name = $"lblMainDisplay_{actionName}_{inputType}",
                Text = "(none)",
                Location = new Point(xPos + 65, 25), Size = new Size(430, 18),
                BorderStyle = BorderStyle.Fixed3D, Font = FormFont,
                AutoEllipsis = true, BackColor = Color.White, ForeColor = Color.Black,
                Cursor = Cursors.Hand,
            };
            lblMain.Click += (s, e) => StartCapture(actionName, inputType, "Main");
            grp.Controls.Add(lblMain);

            var btnConfirmMain = new Button
            {
                Name = $"btnConfirmMain_{actionName}_{inputType}",
                Text = "✓", Size = new Size(28, 22),
                Location = new Point(xPos + 500, 23), Enabled = false,
            };
            btnConfirmMain.Click += (s, e) => ConfirmMain(actionName, inputType);
            grp.Controls.Add(btnConfirmMain);

            var btnClearMain = new Button
            {
                Name = $"btnClearMain_{actionName}_{inputType}",
                Text = "✕", Size = new Size(28, 22),
                Location = new Point(xPos + 533, 23), Enabled = false,
            };
            btnClearMain.Click += (s, e) => ClearMain(actionName, inputType);
            grp.Controls.Add(btnClearMain);

            grp.Controls.Add(new Label
            {
                Text = "Modifier:", Location = new Point(xPos, 55), Size = new Size(60, 18),
                Font = FormFont, TextAlign = ContentAlignment.MiddleLeft,
            });

            var lblMod = new Label
            {
                Name = $"lblModDisplay_{actionName}_{inputType}",
                Text = "(none)",
                Location = new Point(xPos + 65, 55), Size = new Size(430, 18),
                BorderStyle = BorderStyle.Fixed3D, Font = FormFont,
                AutoEllipsis = true, BackColor = Color.White, ForeColor = Color.Black,
                Cursor = Cursors.Hand,
            };
            lblMod.Click += (s, e) => StartCapture(actionName, inputType, "Modifier");
            grp.Controls.Add(lblMod);

            var btnConfirmMod = new Button
            {
                Name = $"btnConfirmMod_{actionName}_{inputType}",
                Text = "✓", Size = new Size(28, 22),
                Location = new Point(xPos + 500, 53), Enabled = false,
            };
            btnConfirmMod.Click += (s, e) => ConfirmModifier(actionName, inputType);
            grp.Controls.Add(btnConfirmMod);

            var btnClearMod = new Button
            {
                Name = $"btnClearMod_{actionName}_{inputType}",
                Text = "✕", Size = new Size(28, 22),
                Location = new Point(xPos + 533, 53), Enabled = false,
            };
            btnClearMod.Click += (s, e) => ClearModifier(actionName, inputType);
            grp.Controls.Add(btnClearMod);

            var lblStatus = new Label
            {
                Name = $"lblStatus_{actionName}_{inputType}",
                Text = "", Location = new Point(xPos, 77), Size = new Size(400, 20),
                ForeColor = Color.Blue, AutoSize = false,
            };
            grp.Controls.Add(lblStatus);

            if (inputType == "HIDAxis")
            {
                var chkInvert = new CheckBox
                {
                    Name = $"chkInvert_{actionName}_{inputType}",
                    Text = "Invert axis", Location = new Point(xPos + 430, 77),
                    Size = new Size(150, 20), Font = FormFont,
                };
                chkInvert.CheckedChanged += (s, e) =>
                {
                    var binding = FindBinding(actionName, inputType);
                    if (binding != null)
                    {
                        binding.Inverted = chkInvert.Checked;
                        BindingConfiguration.SaveMouseConfiguration();
                    }
                };
                grp.Controls.Add(chkInvert);
            }

            sectionsPanel.Controls.Add(grp);
        }

        // ---- Binding lookup / display ----

        private InputBinding FindBinding(string action, string inputType)
        {
            return BindingConfiguration.MouseBindings
                .FirstOrDefault(b => b.Action == action && b.InputType == inputType);
        }

        private InputBinding FindOrCreateBinding(string action, string inputType)
        {
            var binding = FindBinding(action, inputType);
            if (binding == null)
            {
                binding = new InputBinding(action, inputType, $"{action} ({inputType})");
                BindingConfiguration.MouseBindings.Add(binding);
            }
            return binding;
        }

        private void UpdateBindingDisplay(InputBinding binding)
        {
            var mainLbl = sectionsPanel.Controls.Find($"lblMainDisplay_{binding.Action}_{binding.InputType}", true).FirstOrDefault() as Label;
            if (mainLbl != null && binding.InputCodes.Count > 0)
            {
                mainLbl.Text = string.Join(" | ", binding.InputCodes);
                mainLbl.BackColor = Color.FromArgb(200, 255, 190);
            }

            var modLbl = sectionsPanel.Controls.Find($"lblModDisplay_{binding.Action}_{binding.InputType}", true).FirstOrDefault() as Label;
            if (modLbl != null)
            {
                if (binding.ModifierCodes.Count > 0)
                {
                    modLbl.Text = string.Join(" | ", binding.ModifierCodes);
                    modLbl.BackColor = Color.FromArgb(200, 255, 190);
                }
                else
                {
                    modLbl.Text = "(none)";
                    modLbl.BackColor = Color.White;
                }
            }

            if (binding.InputType == "HIDAxis")
            {
                var chk = sectionsPanel.Controls.Find($"chkInvert_{binding.Action}_{binding.InputType}", true).FirstOrDefault() as CheckBox;
                if (chk != null)
                    chk.Checked = binding.Inverted;
            }

            var clrMain = sectionsPanel.Controls.Find($"btnClearMain_{binding.Action}_{binding.InputType}", true).FirstOrDefault() as Button;
            if (clrMain != null) clrMain.Enabled = binding.InputCodes.Count > 0;
            var clrMod = sectionsPanel.Controls.Find($"btnClearMod_{binding.Action}_{binding.InputType}", true).FirstOrDefault() as Button;
            if (clrMod != null) clrMod.Enabled = binding.ModifierCodes.Count > 0;
        }

        // ---- Capture lifecycle ----

        private void StartCapture(string actionName, string inputType, string captureMode)
        {
            CurrentCaptureMode = captureMode;
            CurrentCaptureAction = actionName;
            CurrentCaptureInputType = inputType;
            (captureMode == "Main" ? CapturedInputCodes : CapturedModifierCodes).Clear();

            var displayLabelName = captureMode == "Main"
                ? $"lblMainDisplay_{actionName}_{inputType}"
                : $"lblModDisplay_{actionName}_{inputType}";
            var statusLabelName = $"lblStatus_{actionName}_{inputType}";

            var displayLabel = sectionsPanel.Controls.Find(displayLabelName, true).FirstOrDefault() as Label;
            var statusLabel = sectionsPanel.Controls.Find(statusLabelName, true).FirstOrDefault() as Label;

            if (displayLabel != null)
            {
                displayLabel.BackColor = Color.Yellow;
                displayLabel.Text = "...";
            }
            if (statusLabel != null)
            {
                statusLabel.Text = captureMode == "Main" ? "Waiting for input" : "Waiting for modifiers";
                statusLabel.ForeColor = Color.Red;
            }

            ToggleControlsForCapture(actionName, inputType, captureMode, capturing: true);

            InitializeJoystickCapture();
            InitializePreviousButtonStatesForAll();
            if (inputType == "HIDAxis")
            {
                InitializeAxisSnapshotsForAll();
            }

            JoystickPollTimer.Start();
            InputCaptureTimer.Start();
        }

        private void ToggleControlsForCapture(string actionName, string inputType, string captureMode, bool capturing)
        {
            string activeGroupName = $"grp{actionName}_{inputType}";

            foreach (Control ctrl in sectionsPanel.Controls)
            {
                if (!(ctrl is GroupBox grp))
                    continue;

                if (grp.Name != activeGroupName)
                {
                    grp.Enabled = !capturing;
                    continue;
                }

                foreach (Control child in grp.Controls)
                {
                    if (captureMode == "Main")
                    {
                        if (child.Name == $"btnConfirmMain_{actionName}_{inputType}" ||
                            child.Name == $"btnClearMain_{actionName}_{inputType}")
                        {
                            child.Enabled = capturing;
                        }
                        else if (child.Name?.Contains("Mod") == true)
                        {
                            child.Enabled = !capturing;
                        }
                    }
                    else
                    {
                        if (child.Name == $"btnConfirmMod_{actionName}_{inputType}" ||
                            child.Name == $"btnClearMod_{actionName}_{inputType}")
                        {
                            child.Enabled = capturing;
                        }
                        else if (child.Name?.Contains("Main") == true)
                        {
                            child.Enabled = !capturing;
                        }
                    }
                }
            }

            if (numSensitivity != null) numSensitivity.Enabled = !capturing;
        }

        private void ConfirmMain(string actionName, string inputType)
        {
            InputCaptureTimer.Stop();
            JoystickPollTimer.Stop();

            if (CapturedInputCodes.Count == 0)
            {
                ToggleControlsForCapture(actionName, inputType, "Main", capturing: false);
                return;
            }

            var binding = FindOrCreateBinding(actionName, inputType);
            binding.InputCodes = new List<string>(CapturedInputCodes);
            binding.Description = $"{actionName} ({string.Join(" | ", CapturedInputCodes)})";
            if (CurrentCaptureProductGuid != Guid.Empty)
                binding.ProductGuid = CurrentCaptureProductGuid.ToString();

            if (inputType == "HIDAxis")
            {
                var chk = sectionsPanel.Controls.Find($"chkInvert_{actionName}_{inputType}", true).FirstOrDefault() as CheckBox;
                if (chk != null) binding.Inverted = chk.Checked;
            }

            UpdateBindingDisplay(binding);

            var statusLbl = sectionsPanel.Controls.Find($"lblStatus_{actionName}_{inputType}", true).FirstOrDefault() as Label;
            if (statusLbl != null) { statusLbl.Text = "Input saved"; statusLbl.ForeColor = Color.Green; }

            CapturedInputCodes.Clear();
            CurrentCaptureProductGuid = Guid.Empty;
            ToggleControlsForCapture(actionName, inputType, "Main", capturing: false);

            BindingConfiguration.SaveMouseConfiguration();
        }

        private void ConfirmModifier(string actionName, string inputType)
        {
            InputCaptureTimer.Stop();
            JoystickPollTimer.Stop();

            if (CapturedModifierCodes.Count == 0)
            {
                ToggleControlsForCapture(actionName, inputType, "Modifier", capturing: false);
                return;
            }

            var binding = FindBinding(actionName, inputType);
            if (binding != null)
            {
                binding.ModifierCodes = new List<string>(CapturedModifierCodes);
                UpdateBindingDisplay(binding);

                var statusLbl = sectionsPanel.Controls.Find($"lblStatus_{actionName}_{inputType}", true).FirstOrDefault() as Label;
                if (statusLbl != null) { statusLbl.Text = "Modifiers saved"; statusLbl.ForeColor = Color.Green; }

                BindingConfiguration.SaveMouseConfiguration();
            }

            CapturedModifierCodes.Clear();
            ToggleControlsForCapture(actionName, inputType, "Modifier", capturing: false);
        }

        private void ClearMain(string actionName, string inputType)
        {
            InputCaptureTimer.Stop();
            JoystickPollTimer.Stop();

            var binding = FindBinding(actionName, inputType);
            if (binding != null)
            {
                binding.InputCodes.Clear();
                binding.ProductGuid = null;
                BindingConfiguration.SaveMouseConfiguration();
            }

            var lbl = sectionsPanel.Controls.Find($"lblMainDisplay_{actionName}_{inputType}", true).FirstOrDefault() as Label;
            if (lbl != null) { lbl.Text = "(none)"; lbl.BackColor = Color.White; }
            var statusLbl = sectionsPanel.Controls.Find($"lblStatus_{actionName}_{inputType}", true).FirstOrDefault() as Label;
            if (statusLbl != null) { statusLbl.Text = ""; statusLbl.ForeColor = Color.Blue; }

            CapturedInputCodes.Clear();
            ToggleControlsForCapture(actionName, inputType, "Main", capturing: false);
        }

        private void ClearModifier(string actionName, string inputType)
        {
            InputCaptureTimer.Stop();
            JoystickPollTimer.Stop();

            var binding = FindBinding(actionName, inputType);
            if (binding != null)
            {
                binding.ModifierCodes.Clear();
                BindingConfiguration.SaveMouseConfiguration();
            }

            var lbl = sectionsPanel.Controls.Find($"lblModDisplay_{actionName}_{inputType}", true).FirstOrDefault() as Label;
            if (lbl != null) { lbl.Text = "(none)"; lbl.BackColor = Color.White; }
            var statusLbl = sectionsPanel.Controls.Find($"lblStatus_{actionName}_{inputType}", true).FirstOrDefault() as Label;
            if (statusLbl != null) { statusLbl.Text = ""; statusLbl.ForeColor = Color.Blue; }

            CapturedModifierCodes.Clear();
            ToggleControlsForCapture(actionName, inputType, "Modifier", capturing: false);
        }

        // ---- Joystick polling (mirrors frmBindingEditor) ----

        private void InitializeJoystickCapture()
        {
            try
            {
                foreach (var device in AttachedDevices)
                {
                    if (ActiveJoysticks.ContainsKey(device.InstanceGuid))
                        continue;
                    try
                    {
                        var joystick = InputDeviceManager.CreateJoystick(device.InstanceGuid);
                        joystick.Acquire();
                        ActiveJoysticks[device.InstanceGuid] = joystick;
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex, "Failed to acquire device {0}", device.ProductName);
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
            PreviousButtonStatesMap.Clear();
            PreviousPOVStateMap.Clear();

            foreach (var kvp in ActiveJoysticks)
            {
                try
                {
                    System.Threading.Thread.Sleep(50);
                    var state = kvp.Value.GetCurrentState();

                    if (state.Buttons != null)
                    {
                        var buf = new bool[state.Buttons.Length];
                        Array.Copy(state.Buttons, buf, state.Buttons.Length);
                        PreviousButtonStatesMap[kvp.Key] = buf;
                    }

                    PreviousPOVStateMap[kvp.Key] =
                        (state.PointOfViewControllers != null && state.PointOfViewControllers.Length > 0)
                            ? state.PointOfViewControllers[0]
                            : -1;
                }
                catch (Exception ex)
                {
                    log.Error(ex, "Error snapshotting buttons for {0}", kvp.Key);
                }
            }
        }

        private void InitializeAxisSnapshotsForAll()
        {
            InitialAxisStatesMap.Clear();
            foreach (var kvp in ActiveJoysticks)
            {
                try
                {
                    try { kvp.Value.Poll(); } catch { }
                    var state = kvp.Value.GetCurrentState();
                    InitialAxisStatesMap[kvp.Key] = ReadAxisValues(state);
                }
                catch (Exception ex)
                {
                    log.Error(ex, "Error snapshotting axes for {0}", kvp.Key);
                }
            }
        }

        private static Dictionary<string, int> ReadAxisValues(JoystickState state)
        {
            var axes = new Dictionary<string, int>
            {
                { "Axis_X", state.X }, { "Axis_Y", state.Y }, { "Axis_Z", state.Z },
                { "Axis_RX", state.RotationX }, { "Axis_RY", state.RotationY }, { "Axis_RZ", state.RotationZ },
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
                foreach (var kvp in ActiveJoysticks)
                {
                    try
                    {
                        try { kvp.Value.Poll(); } catch { }
                        var state = kvp.Value.GetCurrentState();
                        PollJoystickState(state, kvp.Key);

                        if (CurrentCaptureInputType == "HIDAxis"
                            && CurrentCaptureMode == "Main"
                            && CapturedInputCodes.Count > 0)
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex, "Error polling device {0}", kvp.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error in poll timer");
            }
        }

        private void PollJoystickState(JoystickState state, Guid deviceGuid)
        {
            var friendlyName = DeviceFriendlyNames.TryGetValue(deviceGuid, out var n) ? n : "Unknown";

            if (CurrentCaptureInputType == "HIDAxis"
                && CurrentCaptureMode == "Main"
                && CapturedInputCodes.Count == 0)
            {
                if (!InitialAxisStatesMap.TryGetValue(deviceGuid, out var initialAxes))
                    return;

                var currentAxes = ReadAxisValues(state);

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
                    if (!initialAxes.TryGetValue(pair.Key, out int initial))
                        continue;
                    if (Math.Abs(pair.Value - initial) < AxisCaptureThreshold)
                        continue;

                    string axisCode = $"{friendlyName}_{pair.Key}";
                    CapturedInputCodes.Add(axisCode);
                    CurrentCaptureDeviceName = friendlyName;
                    CurrentCaptureProductGuid = DeviceProductGuids.TryGetValue(deviceGuid, out var pg) ? pg : Guid.Empty;
                    UpdateCaptureDisplay();
                    return;
                }
                return;
            }

            if (!PreviousButtonStatesMap.TryGetValue(deviceGuid, out var prevButtons)) return;
            if (!PreviousPOVStateMap.TryGetValue(deviceGuid, out var prevPOV)) return;

            if (CurrentCaptureMode == "Main")
            {
                var buttons = state.Buttons;
                if (buttons != null && prevButtons != null)
                {
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        if (!prevButtons[i] && buttons[i])
                        {
                            string buttonCode = $"{friendlyName}_Button_{i}";
                            CapturedInputCodes.Clear();
                            CapturedInputCodes.Add(buttonCode);
                            prevButtons[i] = true;
                            CurrentCaptureDeviceName = friendlyName;
                            CurrentCaptureProductGuid = DeviceProductGuids.TryGetValue(deviceGuid, out var pg) ? pg : Guid.Empty;
                            UpdateCaptureDisplay();
                            return;
                        }
                    }
                    Array.Copy(buttons, prevButtons, buttons.Length);
                }

                var pov = state.PointOfViewControllers;
                if (pov != null && pov.Length > 0)
                {
                    int povValue = pov[0];
                    if ((prevPOV == -1 && povValue >= 0) || (prevPOV >= 0 && povValue >= 0 && prevPOV != povValue))
                    {
                        string code = GetPOVCode(povValue);
                        if (!string.IsNullOrEmpty(code))
                        {
                            string full = $"{friendlyName}_{code}";
                            CapturedInputCodes.Clear();
                            CapturedInputCodes.Add(full);
                            PreviousPOVStateMap[deviceGuid] = povValue;
                            CurrentCaptureDeviceName = friendlyName;
                            CurrentCaptureProductGuid = DeviceProductGuids.TryGetValue(deviceGuid, out var pg) ? pg : Guid.Empty;
                            UpdateCaptureDisplay();
                            return;
                        }
                    }
                    PreviousPOVStateMap[deviceGuid] = povValue;
                }
            }
            else if (CurrentCaptureMode == "Modifier")
            {
                var buttons = state.Buttons;
                if (buttons != null && prevButtons != null)
                {
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        if (!prevButtons[i] && buttons[i])
                        {
                            string code = $"{friendlyName}_Button_{i}";
                            if (!CapturedModifierCodes.Contains(code))
                                CapturedModifierCodes.Add(code);
                        }
                    }
                    Array.Copy(buttons, prevButtons, buttons.Length);
                }

                var pov = state.PointOfViewControllers;
                if (pov != null && pov.Length > 0)
                {
                    int povValue = pov[0];
                    if ((prevPOV == -1 && povValue >= 0) || (prevPOV >= 0 && povValue >= 0 && prevPOV != povValue))
                    {
                        string code = GetPOVCode(povValue);
                        if (!string.IsNullOrEmpty(code))
                        {
                            string full = $"{friendlyName}_{code}";
                            if (!CapturedModifierCodes.Contains(full))
                                CapturedModifierCodes.Add(full);
                        }
                        PreviousPOVStateMap[deviceGuid] = povValue;
                    }
                }
            }

            UpdateCaptureDisplay();
        }

        private void UpdateCaptureDisplay()
        {
            if (CurrentCaptureMode == "Main" && CapturedInputCodes.Count > 0)
            {
                var lbl = sectionsPanel?.Controls.Find($"lblMainDisplay_{CurrentCaptureAction}_{CurrentCaptureInputType}", true).FirstOrDefault() as Label;
                if (lbl != null) lbl.Text = string.Join(" | ", CapturedInputCodes);
            }
            else if (CurrentCaptureMode == "Modifier" && CapturedModifierCodes.Count > 0)
            {
                var lbl = sectionsPanel?.Controls.Find($"lblModDisplay_{CurrentCaptureAction}_{CurrentCaptureInputType}", true).FirstOrDefault() as Label;
                if (lbl != null) lbl.Text = string.Join(" | ", CapturedModifierCodes);
            }
        }

        private static string GetPOVCode(int povValue)
        {
            switch (povValue)
            {
                case 0: return "POV_North";
                case 4500: return "POV_NorthEast";
                case 9000: return "POV_East";
                case 13500: return "POV_SouthEast";
                case 18000: return "POV_South";
                case 22500: return "POV_SouthWest";
                case 27000: return "POV_West";
                case 31500: return "POV_NorthWest";
                default: return null;
            }
        }

        private static string GetFriendlyDeviceName(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName)) return "Unknown";
            var name = System.Text.RegularExpressions.Regex.Replace(productName, @"[^a-zA-Z0-9]", "_").Trim('_');
            return string.IsNullOrEmpty(name) ? "Device" : name;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    InputCaptureTimer?.Stop();
                    InputCaptureTimer?.Dispose();
                    JoystickPollTimer?.Stop();
                    JoystickPollTimer?.Dispose();

                    foreach (var j in ActiveJoysticks.Values)
                    {
                        try { j.Unacquire(); j.Dispose(); } catch { }
                    }
                    ActiveJoysticks.Clear();
                    InputDeviceManager?.Dispose();
                    FormFont?.Dispose();
                }
                catch { }
            }
            base.Dispose(disposing);
        }
    }
}
