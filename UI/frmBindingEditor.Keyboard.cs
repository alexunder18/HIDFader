using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using HIDFader.Core;
using HIDFader.Input;

namespace HIDFader.UI
{
    /// <summary>
    /// Keyboard tab: binds an HID input (button/POV hat/axis) to a sequence of
    /// keyboard chords. Each row in the tab is one InputBinding with
    /// Action = "KeyboardEmit". HID capture reuses the main form's joystick
    /// polling; keyboard-key capture uses the form's KeyPreview.
    /// </summary>
    public partial class frmBindingEditor
    {
        private const string KeyboardAction = "KeyboardEmit";

        private FlowLayoutPanel keyboardRowsPanel;
        private Button btnAddKeyboardBinding;

        // When non-null, the capture state machine is operating on a keyboard
        // row rather than on a fixed HID-tab action/inputType slot. Holds the
        // BindingId of the row being edited.
        private string CurrentKeyboardBindingId;

        // Which field of the keyboard row is being captured: "HID", "Keys", "Modifier".
        private string CurrentKeyboardCaptureField;

        // Chord sequence accumulated during a "Keys" capture session.
        private List<string> CapturedKeys = new List<string>();

        private void SetupKeyboardTab(TabPage tab)
        {
            var header = new Label
            {
                Text = "Bind HID inputs to keystrokes. Keys are only sent while this app is the foreground window.",
                Location = new Point(5, 5),
                Size = new Size(595, 30),
                Font = FormFont,
                ForeColor = SystemColors.ControlDarkDark,
            };
            tab.Controls.Add(header);

            btnAddKeyboardBinding = new Button
            {
                Text = "+ Add Keyboard Binding",
                Location = new Point(5, 38),
                Size = new Size(180, 26),
                Font = FormFont,
            };
            btnAddKeyboardBinding.Click += (s, e) => AddKeyboardBinding();
            tab.Controls.Add(btnAddKeyboardBinding);

            keyboardRowsPanel = new FlowLayoutPanel
            {
                Location = new Point(5, 70),
                Size = new Size(595, 390),
                FlowDirection = FlowDirection.TopDown,
                AutoScroll = true,
                WrapContents = false,
                BorderStyle = BorderStyle.None,
            };
            tab.Controls.Add(keyboardRowsPanel);

            // Form-level key capture routes through this handler; it only acts
            // while a keyboard row is in "Keys" capture mode.
            this.KeyPreview = true;
            this.KeyDown += FormKeyDown_ForKeyboardCapture;
        }

        private void PopulateKeyboardTabFromConfig()
        {
            if (keyboardRowsPanel == null)
                return;

            keyboardRowsPanel.Controls.Clear();

            foreach (var binding in ApplicationConfig.InputBindings)
            {
                if (binding.Action != KeyboardAction)
                    continue;

                AddKeyboardRow(binding);
            }
        }

        private void AddKeyboardBinding()
        {
            var binding = new InputBinding(KeyboardAction, "HIDButton", "Keyboard binding");
            ApplicationConfig.InputBindings.Add(binding);
            AddKeyboardRow(binding);
        }

        private void AddKeyboardRow(InputBinding binding)
        {
            var row = new GroupBox
            {
                Name = $"kbdRow_{binding.BindingId}",
                Text = $"Keyboard binding",
                Size = new Size(570, 150),
                Font = FormFont,
                Margin = new Padding(3, 3, 3, 6),
            };

            // Row 1: trigger type + trigger capture
            var lblTriggerType = new Label
            {
                Text = "Trigger:",
                Location = new Point(10, 25),
                Size = new Size(55, 20),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            row.Controls.Add(lblTriggerType);

            var cboType = new ComboBox
            {
                Name = $"kbdType_{binding.BindingId}",
                Location = new Point(70, 23),
                Size = new Size(110, 22),
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            cboType.Items.Add("Button / POV");
            cboType.Items.Add("Axis");
            cboType.SelectedIndex = binding.InputType == "HIDAxis" ? 1 : 0;
            cboType.SelectedIndexChanged += (s, e) => KeyboardTypeChanged(binding.BindingId, cboType.SelectedIndex);
            row.Controls.Add(cboType);

            var lblTrig = new Label
            {
                Name = $"kbdTrigDisplay_{binding.BindingId}",
                Text = binding.InputCodes != null && binding.InputCodes.Count > 0
                    ? string.Join(" | ", binding.InputCodes)
                    : "(none)",
                Location = new Point(185, 23),
                Size = new Size(260, 22),
                BorderStyle = BorderStyle.Fixed3D,
                BackColor = binding.InputCodes != null && binding.InputCodes.Count > 0
                    ? Color.FromArgb(200, 255, 190) : Color.White,
                ForeColor = Color.Black,
                AutoEllipsis = true,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            lblTrig.Click += (s, e) => StartKeyboardCapture(binding.BindingId, "HID");
            row.Controls.Add(lblTrig);

            var btnTrigConfirm = new Button
            {
                Name = $"kbdTrigConfirm_{binding.BindingId}",
                Text = "✓",
                Size = new Size(28, 22),
                Location = new Point(450, 23),
                Enabled = false,
            };
            btnTrigConfirm.Click += (s, e) => ConfirmKeyboardCapture(binding.BindingId, "HID");
            row.Controls.Add(btnTrigConfirm);

            var btnTrigClear = new Button
            {
                Name = $"kbdTrigClear_{binding.BindingId}",
                Text = "✕",
                Size = new Size(28, 22),
                Location = new Point(482, 23),
                Enabled = binding.InputCodes != null && binding.InputCodes.Count > 0,
            };
            btnTrigClear.Click += (s, e) => ClearKeyboardCapture(binding.BindingId, "HID");
            row.Controls.Add(btnTrigClear);

            var chkInvert = new CheckBox
            {
                Name = $"kbdInvert_{binding.BindingId}",
                Text = "Invert",
                Location = new Point(516, 24),
                Size = new Size(55, 20),
                Checked = binding.Inverted,
                Visible = binding.InputType == "HIDAxis",
            };
            chkInvert.CheckedChanged += (s, e) => { binding.Inverted = chkInvert.Checked; };
            row.Controls.Add(chkInvert);

            // Row 2: keys capture
            var lblKeys = new Label
            {
                Text = "Keys:",
                Location = new Point(10, 55),
                Size = new Size(55, 20),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            row.Controls.Add(lblKeys);

            var lblKeysDisplay = new Label
            {
                Name = $"kbdKeysDisplay_{binding.BindingId}",
                Text = binding.OutputKeys != null && binding.OutputKeys.Count > 0
                    ? string.Join(", ", binding.OutputKeys)
                    : "(none) — click to record",
                Location = new Point(70, 53),
                Size = new Size(375, 22),
                BorderStyle = BorderStyle.Fixed3D,
                BackColor = binding.OutputKeys != null && binding.OutputKeys.Count > 0
                    ? Color.FromArgb(200, 255, 190) : Color.White,
                ForeColor = Color.Black,
                AutoEllipsis = true,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            lblKeysDisplay.Click += (s, e) => StartKeyboardCapture(binding.BindingId, "Keys");
            row.Controls.Add(lblKeysDisplay);

            var btnKeysConfirm = new Button
            {
                Name = $"kbdKeysConfirm_{binding.BindingId}",
                Text = "✓",
                Size = new Size(28, 22),
                Location = new Point(450, 53),
                Enabled = false,
            };
            btnKeysConfirm.Click += (s, e) => ConfirmKeyboardCapture(binding.BindingId, "Keys");
            row.Controls.Add(btnKeysConfirm);

            var btnKeysClear = new Button
            {
                Name = $"kbdKeysClear_{binding.BindingId}",
                Text = "✕",
                Size = new Size(28, 22),
                Location = new Point(482, 53),
                Enabled = binding.OutputKeys != null && binding.OutputKeys.Count > 0,
            };
            btnKeysClear.Click += (s, e) => ClearKeyboardCapture(binding.BindingId, "Keys");
            row.Controls.Add(btnKeysClear);

            // Row 3: modifier
            var lblMod = new Label
            {
                Text = "Modifier:",
                Location = new Point(10, 85),
                Size = new Size(55, 20),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            row.Controls.Add(lblMod);

            var lblModDisplay = new Label
            {
                Name = $"kbdModDisplay_{binding.BindingId}",
                Text = binding.ModifierCodes != null && binding.ModifierCodes.Count > 0
                    ? string.Join(" | ", binding.ModifierCodes)
                    : "(none)",
                Location = new Point(70, 83),
                Size = new Size(375, 22),
                BorderStyle = BorderStyle.Fixed3D,
                BackColor = binding.ModifierCodes != null && binding.ModifierCodes.Count > 0
                    ? Color.FromArgb(200, 255, 190) : Color.White,
                ForeColor = Color.Black,
                AutoEllipsis = true,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            lblModDisplay.Click += (s, e) => StartKeyboardCapture(binding.BindingId, "Modifier");
            row.Controls.Add(lblModDisplay);

            var btnModConfirm = new Button
            {
                Name = $"kbdModConfirm_{binding.BindingId}",
                Text = "✓",
                Size = new Size(28, 22),
                Location = new Point(450, 83),
                Enabled = false,
            };
            btnModConfirm.Click += (s, e) => ConfirmKeyboardCapture(binding.BindingId, "Modifier");
            row.Controls.Add(btnModConfirm);

            var btnModClear = new Button
            {
                Name = $"kbdModClear_{binding.BindingId}",
                Text = "✕",
                Size = new Size(28, 22),
                Location = new Point(482, 83),
                Enabled = binding.ModifierCodes != null && binding.ModifierCodes.Count > 0,
            };
            btnModClear.Click += (s, e) => ClearKeyboardCapture(binding.BindingId, "Modifier");
            row.Controls.Add(btnModClear);

            // Row 4: status + remove
            var lblStatus = new Label
            {
                Name = $"kbdStatus_{binding.BindingId}",
                Text = "",
                Location = new Point(10, 115),
                Size = new Size(430, 20),
                ForeColor = Color.Blue,
                AutoSize = false,
            };
            row.Controls.Add(lblStatus);

            var btnRemove = new Button
            {
                Text = "Remove",
                Size = new Size(80, 24),
                Location = new Point(482, 114),
                ForeColor = Color.DarkRed,
            };
            btnRemove.Click += (s, e) => RemoveKeyboardRow(binding.BindingId);
            row.Controls.Add(btnRemove);

            keyboardRowsPanel.Controls.Add(row);
        }

        private void KeyboardTypeChanged(string bindingId, int selectedIndex)
        {
            var binding = FindKeyboardBinding(bindingId);
            if (binding == null)
                return;

            var newType = selectedIndex == 1 ? "HIDAxis" : "HIDButton";

            if (binding.InputType != newType)
            {
                // Switching type invalidates any existing trigger capture on this row.
                binding.InputType = newType;
                binding.InputCodes.Clear();
                binding.ProductGuid = null;

                var trigLbl = FindControlByName($"kbdTrigDisplay_{bindingId}") as Label;
                if (trigLbl != null)
                {
                    trigLbl.Text = "(none)";
                    trigLbl.BackColor = Color.White;
                }
            }

            var chk = FindControlByName($"kbdInvert_{bindingId}") as CheckBox;
            if (chk != null)
                chk.Visible = newType == "HIDAxis";
        }

        private void RemoveKeyboardRow(string bindingId)
        {
            // If the row being removed is the one currently capturing, cancel capture first.
            if (CurrentKeyboardBindingId == bindingId)
            {
                CancelKeyboardCapture();
            }

            ApplicationConfig.InputBindings.RemoveAll(b => b.BindingId == bindingId);

            var row = FindControlByName($"kbdRow_{bindingId}") as GroupBox;
            if (row != null)
                keyboardRowsPanel.Controls.Remove(row);
        }

        private InputBinding FindKeyboardBinding(string bindingId)
        {
            return ApplicationConfig.InputBindings
                .FirstOrDefault(b => b.Action == KeyboardAction && b.BindingId == bindingId);
        }

        private void StartKeyboardCapture(string bindingId, string field)
        {
            var binding = FindKeyboardBinding(bindingId);
            if (binding == null)
                return;

            // Any previous capture (from this tab or the HID tab) must stop before we retarget.
            InputCaptureTimer?.Stop();
            JoystickPollTimer?.Stop();

            CurrentKeyboardBindingId = bindingId;
            CurrentKeyboardCaptureField = field;

            // Reuse existing state so the existing polling logic can route its findings
            // back through our branch in UpdateCaptureDisplay().
            CapturedInputCodes.Clear();
            CapturedModifierCodes.Clear();
            CapturedKeys.Clear();

            // Keys capture is additive: seed with the existing chords so new presses
            // append rather than replace. Users clear with ✕ first if they want to start over.
            if (field == "Keys" && binding.OutputKeys != null && binding.OutputKeys.Count > 0)
            {
                CapturedKeys.AddRange(binding.OutputKeys);
            }

            CurrentCaptureAction = bindingId;
            CurrentCaptureInputType = binding.InputType;
            CurrentCaptureMode = field == "Modifier" ? "Modifier" : "Main";

            var displayName = field == "HID" ? $"kbdTrigDisplay_{bindingId}"
                            : field == "Keys" ? $"kbdKeysDisplay_{bindingId}"
                            : $"kbdModDisplay_{bindingId}";

            var statusName = $"kbdStatus_{bindingId}";

            var displayLabel = FindControlByName(displayName) as Label;
            var statusLabel = FindControlByName(statusName) as Label;

            if (displayLabel != null)
            {
                displayLabel.BackColor = Color.Yellow;
                if (field == "Keys")
                {
                    displayLabel.Text = CapturedKeys.Count > 0
                        ? string.Join(", ", CapturedKeys)
                        : "Press keys...";
                }
                else
                {
                    displayLabel.Text = "...";
                }
            }

            if (statusLabel != null)
            {
                statusLabel.Text = field == "HID" ? "Waiting for HID input"
                                  : field == "Keys" ? "Recording keys — new keys appended, press ✓ when done"
                                  : "Waiting for modifiers";
                statusLabel.ForeColor = Color.Red;
            }

            DisableKeyboardRowsDuringCapture(bindingId, field, enable: false);

            if (field == "HID" || field == "Modifier")
            {
                // Reuse the HID tab's joystick polling infrastructure.
                InitializeJoystickCapture();
                InitializePreviousButtonStatesForAll();

                if (binding.InputType == "HIDAxis" && field == "HID")
                {
                    InitializeAxisSnapshotsForAll();
                }

                JoystickPollTimer.Start();
                InputCaptureTimer.Start();
            }
            else // Keys
            {
                // Key capture is driven by FormKeyDown_ForKeyboardCapture — no polling needed.
                InputCaptureTimer.Start();
                this.Focus();
            }
        }

        private void DisableKeyboardRowsDuringCapture(string activeBindingId, string activeField, bool enable)
        {
            btnApply.Enabled = enable;
            btnReset.Enabled = enable;
            btnCancel.Enabled = enable;
            if (btnAddKeyboardBinding != null)
                btnAddKeyboardBinding.Enabled = enable;

            // Disable the entire HID tab while a keyboard capture is live so the user
            // can't start two captures at once.
            if (tabHID != null)
                tabHID.Enabled = enable;

            if (keyboardRowsPanel == null)
                return;

            foreach (Control ctrl in keyboardRowsPanel.Controls)
            {
                if (!(ctrl is GroupBox row))
                    continue;

                bool isActive = row.Name == $"kbdRow_{activeBindingId}";

                if (!isActive)
                {
                    row.Enabled = enable;
                    continue;
                }

                foreach (Control child in row.Controls)
                {
                    string name = child.Name ?? string.Empty;

                    // Confirm/clear for the active field become enabled during capture.
                    if (activeField == "HID" && (name == $"kbdTrigConfirm_{activeBindingId}" || name == $"kbdTrigClear_{activeBindingId}"))
                        child.Enabled = !enable;
                    else if (activeField == "Keys" && (name == $"kbdKeysConfirm_{activeBindingId}" || name == $"kbdKeysClear_{activeBindingId}"))
                        child.Enabled = !enable;
                    else if (activeField == "Modifier" && (name == $"kbdModConfirm_{activeBindingId}" || name == $"kbdModClear_{activeBindingId}"))
                        child.Enabled = !enable;
                    // Remove button and type dropdown disabled during capture.
                    else if (child is Button btn && btn.Text == "Remove")
                        child.Enabled = enable;
                    else if (name.StartsWith("kbdType_"))
                        child.Enabled = enable;
                    // Display labels remain clickable so the user can swap what they're capturing.
                }
            }
        }

        private void ConfirmKeyboardCapture(string bindingId, string field)
        {
            var binding = FindKeyboardBinding(bindingId);
            if (binding == null)
                return;

            InputCaptureTimer?.Stop();
            JoystickPollTimer?.Stop();

            var displayName = field == "HID" ? $"kbdTrigDisplay_{bindingId}"
                            : field == "Keys" ? $"kbdKeysDisplay_{bindingId}"
                            : $"kbdModDisplay_{bindingId}";
            var displayLabel = FindControlByName(displayName) as Label;
            var statusLabel = FindControlByName($"kbdStatus_{bindingId}") as Label;

            var clearBtnName = field == "HID" ? $"kbdTrigClear_{bindingId}"
                             : field == "Keys" ? $"kbdKeysClear_{bindingId}"
                             : $"kbdModClear_{bindingId}";
            var clearBtn = FindControlByName(clearBtnName) as Button;

            if (field == "HID")
            {
                if (CapturedInputCodes.Count == 0)
                {
                    if (displayLabel != null)
                        displayLabel.BackColor = binding.InputCodes.Count > 0 ? Color.FromArgb(200, 255, 190) : Color.White;
                }
                else
                {
                    binding.InputCodes = new List<string>(CapturedInputCodes);
                    binding.Description = $"Keyboard ({string.Join(" | ", CapturedInputCodes)})";
                    if (CurrentCaptureProductGuid != Guid.Empty)
                        binding.ProductGuid = CurrentCaptureProductGuid.ToString();

                    if (displayLabel != null)
                    {
                        displayLabel.Text = string.Join(" | ", binding.InputCodes);
                        displayLabel.BackColor = Color.FromArgb(200, 255, 190);
                    }

                    if (clearBtn != null)
                        clearBtn.Enabled = true;

                    if (statusLabel != null)
                    {
                        statusLabel.Text = "Trigger saved";
                        statusLabel.ForeColor = Color.Green;
                    }
                }
            }
            else if (field == "Keys")
            {
                if (CapturedKeys.Count == 0)
                {
                    if (displayLabel != null)
                        displayLabel.BackColor = binding.OutputKeys.Count > 0 ? Color.FromArgb(200, 255, 190) : Color.White;
                }
                else
                {
                    binding.OutputKeys = new List<string>(CapturedKeys);

                    if (displayLabel != null)
                    {
                        displayLabel.Text = string.Join(", ", binding.OutputKeys);
                        displayLabel.BackColor = Color.FromArgb(200, 255, 190);
                    }

                    if (clearBtn != null)
                        clearBtn.Enabled = true;

                    if (statusLabel != null)
                    {
                        statusLabel.Text = "Keys saved";
                        statusLabel.ForeColor = Color.Green;
                    }
                }
            }
            else // Modifier
            {
                if (CapturedModifierCodes.Count == 0)
                {
                    if (displayLabel != null)
                        displayLabel.BackColor = binding.ModifierCodes.Count > 0 ? Color.FromArgb(200, 255, 190) : Color.White;
                }
                else
                {
                    binding.ModifierCodes = new List<string>(CapturedModifierCodes);

                    if (displayLabel != null)
                    {
                        displayLabel.Text = string.Join(" | ", binding.ModifierCodes);
                        displayLabel.BackColor = Color.FromArgb(200, 255, 190);
                    }

                    if (clearBtn != null)
                        clearBtn.Enabled = true;

                    if (statusLabel != null)
                    {
                        statusLabel.Text = "Modifiers saved";
                        statusLabel.ForeColor = Color.Green;
                    }
                }
            }

            CapturedInputCodes.Clear();
            CapturedModifierCodes.Clear();
            CapturedKeys.Clear();
            CurrentCaptureProductGuid = Guid.Empty;

            DisableKeyboardRowsDuringCapture(bindingId, field, enable: true);

            CurrentKeyboardBindingId = null;
            CurrentKeyboardCaptureField = null;
        }

        private void ClearKeyboardCapture(string bindingId, string field)
        {
            var binding = FindKeyboardBinding(bindingId);
            if (binding == null)
                return;

            InputCaptureTimer?.Stop();
            JoystickPollTimer?.Stop();

            if (field == "HID")
            {
                binding.InputCodes.Clear();
                binding.ProductGuid = null;
                var lbl = FindControlByName($"kbdTrigDisplay_{bindingId}") as Label;
                if (lbl != null) { lbl.Text = "(none)"; lbl.BackColor = Color.White; }
                var clr = FindControlByName($"kbdTrigClear_{bindingId}") as Button;
                if (clr != null) clr.Enabled = false;
            }
            else if (field == "Keys")
            {
                binding.OutputKeys.Clear();
                var lbl = FindControlByName($"kbdKeysDisplay_{bindingId}") as Label;
                if (lbl != null) { lbl.Text = "(none) — click to record"; lbl.BackColor = Color.White; }
                var clr = FindControlByName($"kbdKeysClear_{bindingId}") as Button;
                if (clr != null) clr.Enabled = false;
            }
            else // Modifier
            {
                binding.ModifierCodes.Clear();
                var lbl = FindControlByName($"kbdModDisplay_{bindingId}") as Label;
                if (lbl != null) { lbl.Text = "(none)"; lbl.BackColor = Color.White; }
                var clr = FindControlByName($"kbdModClear_{bindingId}") as Button;
                if (clr != null) clr.Enabled = false;
            }

            CapturedInputCodes.Clear();
            CapturedModifierCodes.Clear();
            CapturedKeys.Clear();

            var statusLabel = FindControlByName($"kbdStatus_{bindingId}") as Label;
            if (statusLabel != null) { statusLabel.Text = ""; statusLabel.ForeColor = Color.Blue; }

            DisableKeyboardRowsDuringCapture(bindingId, field, enable: true);

            CurrentKeyboardBindingId = null;
            CurrentKeyboardCaptureField = null;
        }

        private void CancelKeyboardCapture()
        {
            if (CurrentKeyboardBindingId == null)
                return;

            InputCaptureTimer?.Stop();
            JoystickPollTimer?.Stop();

            var active = CurrentKeyboardBindingId;
            var field = CurrentKeyboardCaptureField;

            CapturedInputCodes.Clear();
            CapturedModifierCodes.Clear();
            CapturedKeys.Clear();

            DisableKeyboardRowsDuringCapture(active, field ?? "HID", enable: true);

            CurrentKeyboardBindingId = null;
            CurrentKeyboardCaptureField = null;
        }

        private void FormKeyDown_ForKeyboardCapture(object sender, KeyEventArgs e)
        {
            if (CurrentKeyboardBindingId == null || CurrentKeyboardCaptureField != "Keys")
                return;

            var chord = KeyboardEmitter.BuildChordFromKeyEvent(e);
            if (chord == null)
                return;

            CapturedKeys.Add(chord);

            var lbl = FindControlByName($"kbdKeysDisplay_{CurrentKeyboardBindingId}") as Label;
            if (lbl != null)
                lbl.Text = string.Join(", ", CapturedKeys);

            // Suppress default handling so Tab/Enter/arrow keys don't move focus away
            // from the form while recording.
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        /// <summary>
        /// Invoked from PollJoystickState when the active capture is a keyboard row
        /// (instead of the fixed-slot HID tab). Routes captured codes into the
        /// keyboard row's display labels.
        /// </summary>
        private void UpdateKeyboardCaptureDisplay()
        {
            if (CurrentKeyboardBindingId == null)
                return;

            if (CurrentKeyboardCaptureField == "HID" && CapturedInputCodes.Count > 0)
            {
                var lbl = FindControlByName($"kbdTrigDisplay_{CurrentKeyboardBindingId}") as Label;
                if (lbl != null)
                    lbl.Text = string.Join(" | ", CapturedInputCodes);
            }
            else if (CurrentKeyboardCaptureField == "Modifier" && CapturedModifierCodes.Count > 0)
            {
                var lbl = FindControlByName($"kbdModDisplay_{CurrentKeyboardBindingId}") as Label;
                if (lbl != null)
                    lbl.Text = string.Join(" | ", CapturedModifierCodes);
            }
        }
    }
}
