using Microsoft.Win32;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using HIDMate.Core;
using HIDMate.Input;

namespace HIDMate.UI
{
    /// <summary>
    /// Main application form - displays list of applications with volume control bindings
    /// Allows user to select an application to configure, add new applications, or remove configurations
    /// </summary>
    public partial class frmApplicationSelector : Form
    {
        private readonly Logger log = LogManager.GetCurrentClassLogger();

        // HKCU run-key entry name for the Start-with-Windows feature. Lives under
        // HKEY_CURRENT_USER so we can write it without elevation.
        private const string AutoStartRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AutoStartValueName = "HIDMate";

        private AudioSessionManager audioSessionManager;
        private BindingConfiguration bindingConfiguration;
        private VolumeControlListener volumeControlListener;
        private Timer listenerStartupTimer;
        private NotifyIcon trayIcon;
        private bool exitFromTray;

        // Created at runtime rather than in InitializeComponent — the WinForms
        // designer can't reliably round-trip a custom UserControl declared in
        // the auto-generated code, so we keep the Designer file standard-only.
        private MouseBindingsControl mouseBindingsControl;

        public frmApplicationSelector()
        {
            InitializeComponent();

            this.Text = "HIDMate";
            this.StartPosition = FormStartPosition.CenterScreen;

            listViewApplications.Columns.Add("Name", 200);
            listViewApplications.Columns.Add("Audio Device", 250);
            listViewApplications.Columns.Add("Bindings", 120);

            volumeStep.Minimum = 1;
            volumeStep.Maximum = 10;
            volumeStep.Value = 5;
            volumeStep.ValueChanged += VolumeStep_SelectedItemChanged;

            chkStartWithWindows.Checked = IsStartWithWindowsEnabled();
            chkStartWithWindows.CheckedChanged += ChkStartWithWindows_CheckedChanged;

            mouseBindingsControl = new MouseBindingsControl
            {
                Dock = DockStyle.Fill,
                Name = "mouseBindingsControl",
            };
            tabMouse.Controls.Add(mouseBindingsControl);

            listViewApplications.DoubleClick += ListViewApplications_DoubleClick;
            listViewApplications.KeyDown += ListViewApplications_KeyDown;
            btnConfigure.Click += BtnConfigure_Click;
            btnRefresh.Click += BtnRefresh_Click;

            InitializeTrayIcon();
            this.Resize += FrmApplicationSelector_Resize;
            this.FormClosing += FrmApplicationSelector_FormClosing;
        }

        private void InitializeTrayIcon()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Show", null, (s, e) => RestoreFromTray());
            menu.Items.Add("Exit", null, (s, e) => { exitFromTray = true; Close(); });

            trayIcon = new NotifyIcon
            {
                Icon = this.Icon ?? SystemIcons.Application,
                Text = "HIDMate",
                ContextMenuStrip = menu,
                Visible = false,
            };

            trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private void FrmApplicationSelector_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
                if (trayIcon != null) trayIcon.Visible = true;
            }
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;

            if (trayIcon != null) 
                trayIcon.Visible = false;

            Activate();
        }

        private void FrmApplicationSelector_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            try
            {
                audioSessionManager = new AudioSessionManager();
                bindingConfiguration = new BindingConfiguration();
                bindingConfiguration.Load();

                int savedStep = bindingConfiguration.VolumeStepPercent;
                volumeStep.Value = savedStep;

                // Mouse tab needs the loaded configuration to render bindings.
                mouseBindingsControl.Initialize(bindingConfiguration);

                try
                {
                    RefreshApplicationList();
                }
                catch (Exception refreshEx)
                {
                    log.Debug($"Error refreshing application list: {refreshEx.Message}");
                    log.Debug($"Stack trace: {refreshEx.StackTrace}");
                }

                listenerStartupTimer = new Timer();
                listenerStartupTimer.Interval = 500;
                listenerStartupTimer.Tick += (sender, args) =>
                {
                    listenerStartupTimer.Stop();
                    listenerStartupTimer.Dispose();
                    StartVolumeListener();
                };

                listenerStartupTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing application: {ex.Message}", "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartVolumeListener()
        {
            try
            {
                LogAllBindings();

                volumeControlListener = new VolumeControlListener(bindingConfiguration, audioSessionManager);
                volumeControlListener.VolumeStep = GetSelectedVolumeStep();

                try
                {
                    volumeControlListener.Start();
                }
                catch (Exception startEx)
                {
                    log.Debug($"Stack trace: {startEx.StackTrace}");
                }
            }
            catch (Exception listenerEx)
            {
                log.Debug($"Error initializing volume control listener: {listenerEx.Message}");
                log.Debug($"Stack trace: {listenerEx.StackTrace}");
            }
        }

        private void LogAllBindings()
        {
            try
            {
                var configurations = bindingConfiguration.GetAllConfigurations();

                foreach (var config in configurations)
                {
                    for (int i = 0; i < config.InputBindings.Count; i++)
                    {
                        var binding = config.InputBindings[i];
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error logging bindings");
            }
        }

        private float GetSelectedVolumeStep()
        {
            if (float.TryParse(volumeStep.Text, out float pct) && pct >= 1 && pct <= 10)
            {
                return pct / 100f;
            }

            return 0.01f;
        }

        private void VolumeStep_SelectedItemChanged(object sender, EventArgs e)
        {
            float step = GetSelectedVolumeStep();

            if (volumeControlListener != null)
            {
                volumeControlListener.VolumeStep = step;
            }

            if (int.TryParse(volumeStep.Text, out int pct) && bindingConfiguration != null)
            {
                bindingConfiguration.VolumeStepPercent = pct;
                bindingConfiguration.SaveSettings();
            }
        }

        /// <summary>
        /// Refresh the application list from audio sessions and configurations
        /// </summary>
        private void RefreshApplicationList()
        {
            ListView listView = listViewApplications;
            
            if (listView == null) 
            { 
                return; 
            }

            listView.BeginUpdate();

            try
            {
                listView.Items.Clear();
                applicationIcons.Images.Clear();
                List<AudioSessionInfo> audioSessions = audioSessionManager.GetAllAudioSessions();
                List<ApplicationConfig> configurations = bindingConfiguration.GetAllConfigurations();
                HashSet<string> configuredProcesses = new HashSet<string>();
                
                foreach (var config in configurations)
                {
                    configuredProcesses.Add(config.ProcessName.ToLower());
                }

                HashSet<string> audioSessionProcesses = new HashSet<string>();

                foreach (var session in audioSessions)
                {
                    if (session.IsSystemSound)
                    {
                        continue;
                    }

                    var processKey = (session.ProcessName ?? string.Empty).ToLower();

                    if (!audioSessionProcesses.Contains(processKey))
                    {
                        audioSessionProcesses.Add(processKey);
                        AddApplicationItemToList(listView, session, configuredProcesses);
                    }
                }

                foreach (var config in configurations)
                {
                    var processKey = (config.ProcessName ?? string.Empty).ToLower();

                    if (!audioSessionProcesses.Contains(processKey))
                    {
                        AddConfiguredApplicationItemToList(listView, config);
                    }
                }                
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error refreshing application list: {ex.Message}", "Refresh Error", MessageBoxButtons.OK, MessageBoxIcon.Error);                
            }
            finally
            {
                listView.EndUpdate();
            }
        }

        /// <summary>
        /// Add an audio session item to the list
        /// </summary>
        private void AddApplicationItemToList(ListView listView, AudioSessionInfo session, HashSet<string> configuredProcesses)
        {
            try
            {
                var processName = (session.ProcessName ?? string.Empty).ToLower();
                var displayName = session.IsSystemSound ? "System Sounds" : session.ProcessName;

                Icon appIcon = null;
                try
                {
                    if (session.ProcessId > 0)
                    {
                        var process = Process.GetProcessById(session.ProcessId);
                        string exePath = null;
                        try
                        {
                            exePath = process.MainModule?.FileName;
                        }
                        catch (Exception exMod)
                        {
                            log.Debug($"MainModule failed for {session.ProcessName} (PID {session.ProcessId}): {exMod.Message}");
                        }

                        if (!string.IsNullOrEmpty(exePath))
                        {
                            appIcon = Icon.ExtractAssociatedIcon(exePath);
                            log.Debug($"Icon extracted for {session.ProcessName}: {appIcon != null}");
                        }
                        else
                        {
                            log.Debug($"No exe path for {session.ProcessName}");
                        }
                    }
                }
                catch (Exception exIcon)
                {
                    log.Debug($"Icon extraction failed for {session.ProcessName}: {exIcon.Message}");
                }

                var item = new ListViewItem()
                {
                    Tag = new { SessionInfo = session, IsConfigured = false }
                };

                if (appIcon != null)
                {
                    applicationIcons.Images.Add(appIcon);
                    item.ImageIndex = applicationIcons.Images.Count - 1;
                }

                item.Text = displayName;
                item.SubItems.Add(session.DeviceFriendlyName ?? "");

                if (configuredProcesses.Contains(processName))
                {
                    var config = bindingConfiguration.GetConfigurationByProcessName(session.ProcessName);
                    var hasBindings = config?.HasEnabledBindings() == true;
                    item.SubItems.Add(hasBindings ? "Configured" : "Not configured");

                    if (hasBindings)
                    {
                        item.BackColor = Color.FromArgb(200, 255, 190); ;
                    }
                }
                else
                {
                    item.SubItems.Add("Not configured");
                }

                listView.Items.Add(item);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error adding session item");
            }
        }

        /// <summary>
        /// Add a configured application item (not currently in audio sessions) to the list
        /// </summary>
        private void AddConfiguredApplicationItemToList(ListView listView, ApplicationConfig config)
        {
            try
            {
                var item = new ListViewItem();
                item.Tag = new { Config = config, IsConfigured = true };
                item.ImageIndex = -1;
                item.Text = config.DisplayName;
                item.SubItems.Add("(not running)");
                var hasBindings = config.HasEnabledBindings();
                item.SubItems.Add(hasBindings ? "Configured" : "Not configured");
                item.ForeColor = Color.Gray;

                if (hasBindings)
                    item.BackColor = Color.FromArgb(200, 255, 190);

                listView.Items.Add(item);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error adding configured item");
            }
        }

        private void ListViewApplications_DoubleClick(object sender, EventArgs e)
        {
            BtnConfigure_Click(sender, e);
        }

        private void ListViewApplications_KeyDown(object sender, KeyEventArgs e)
        {           
            if (e.KeyCode == Keys.Enter)
            {
                BtnConfigure_Click(sender, e);
                e.Handled = true;
            }
        }

        private void BtnConfigure_Click(object sender, EventArgs e)
        {
            if (listViewApplications.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select an application first", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedItem = listViewApplications.SelectedItems[0];
            dynamic tag = selectedItem.Tag;

            ApplicationConfig config = null;

            if (tag.IsConfigured)
            {
                config = (ApplicationConfig)tag.Config;
            }
            else
            {
                var sessionInfo = (AudioSessionInfo)tag.SessionInfo;
                config = bindingConfiguration.GetConfigurationByProcessName(sessionInfo.ProcessName);

                if (config == null)
                {
                    // Create new config for this session
                    var displayName = sessionInfo.IsSystemSound ? "System Sounds" : sessionInfo.ProcessName;
                    config = bindingConfiguration.AddConfiguration(sessionInfo.ProcessName, displayName);
                    bindingConfiguration.Save();
                }
            }

            if (config != null)
            {
                using (frmBindingEditor dlg = new frmBindingEditor(config, bindingConfiguration))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        RefreshApplicationList();
                    }
                }
            }
        }

        private void BtnRefresh_Click(object sender, EventArgs e)
        {
            RefreshApplicationList();
        }

        // ---- Start with Windows ----

        private static bool IsStartWithWindowsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, writable: false))
                {
                    return key?.GetValue(AutoStartValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private void ChkStartWithWindows_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, writable: true))
                {
                    if (key == null)
                    {
                        log.Error("Could not open HKCU Run key");
                        return;
                    }

                    if (chkStartWithWindows.Checked)
                    {
                        // Quote the path so spaces in folder names don't break Run-key parsing.
                        key.SetValue(AutoStartValueName, $"\"{Application.ExecutablePath}\"");
                        log.Debug($"Registered autostart: {Application.ExecutablePath}");
                    }
                    else
                    {
                        key.DeleteValue(AutoStartValueName, throwOnMissingValue: false);
                        log.Debug("Removed autostart entry");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Failed to update Start with Windows setting");
                MessageBox.Show($"Could not update Start with Windows setting: {ex.Message}",
                    "Setting Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            if (listenerStartupTimer != null)
            {
                listenerStartupTimer.Stop();
                listenerStartupTimer.Dispose();
                listenerStartupTimer = null;
            }

            if (volumeControlListener != null)
            {
                try
                {
                    volumeControlListener.Stop();
                    volumeControlListener.Dispose();
                }
                catch { }

                volumeControlListener = null;
            }

            if (bindingConfiguration != null)
            {
                bindingConfiguration.Dispose();
            }

            if (applicationIcons != null)
            {
                applicationIcons.Dispose();
            }
        }       
    }
}
