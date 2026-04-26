using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace HIDMate.Core
{
    /// <summary>
    /// Top-level XML payload for the global mouse configuration. Mouse bindings
    /// are NOT per-application (the cursor is global), so they live in their own
    /// file rather than nested under each ApplicationConfig.
    /// </summary>
    [XmlRoot("MouseConfiguration")]
    public class MouseConfigurationFile
    {
        [XmlAttribute]
        public int Sensitivity { get; set; } = 5;

        [XmlArray("Bindings")]
        [XmlArrayItem("Binding")]
        public List<InputBinding> Bindings { get; set; } = new List<InputBinding>();
    }

    /// <summary>
    /// Manages all application configurations and their persistence
    /// This is the central manager for the configuration system
    /// </summary>
    public class BindingConfiguration : IDisposable
    {
        private readonly Logger log = LogManager.GetCurrentClassLogger();
        private List<ApplicationConfig> ApplicationConfigs { get; set; } = new List<ApplicationConfig>();
        private string ConfigurationFilePath { get; set; }
        private string SettingsFilePath { get; set; }
        private string MouseConfigurationFilePath { get; set; }
        private XmlSerializer ConfigSerializer { get; set; }
        private XmlSerializer MouseSerializer { get; set; }
        private bool Disposed { get; set; }

        public int VolumeStepPercent { get; set; } = 1;

        /// <summary>
        /// Global mouse bindings. Cursor output is system-wide, so these are
        /// stored once for the whole app rather than per ApplicationConfig.
        /// </summary>
        public List<InputBinding> MouseBindings { get; set; } = new List<InputBinding>();

        /// <summary>
        /// Speed multiplier for mouse-movement and scroll bindings (1–10).
        /// </summary>
        public int MouseSensitivity { get; set; } = 5;

        /// <summary>
        /// Event raised when configuration is loaded from disk
        /// </summary>
        public event EventHandler ConfigurationLoaded;

        /// <summary>
        /// Event raised when configuration is saved to disk
        /// </summary>
        public event EventHandler ConfigurationSaved;

        /// <summary>
        /// Event raised when an application configuration is added
        /// </summary>
        public event EventHandler<ApplicationConfigEventArgs> ApplicationConfigAdded;

        /// <summary>
        /// Event raised when an application configuration is removed
        /// </summary>
        public event EventHandler<ApplicationConfigEventArgs> ApplicationConfigRemoved;

        public BindingConfiguration()
        {
            ConfigurationFilePath = GetDefaultConfigurationPath();
            var dir = Path.GetDirectoryName(ConfigurationFilePath);
            SettingsFilePath = Path.Combine(dir, "Settings.xml");
            MouseConfigurationFilePath = Path.Combine(dir, "MouseBindings.xml");
            ConfigSerializer = new XmlSerializer(typeof(List<ApplicationConfig>), new XmlRootAttribute("ApplicationConfigurations"));
            MouseSerializer = new XmlSerializer(typeof(MouseConfigurationFile));
        }

        public BindingConfiguration(string configPath) : this()
        {
            ConfigurationFilePath = configPath;
            var dir = Path.GetDirectoryName(configPath);
            SettingsFilePath = Path.Combine(dir, "Settings.xml");
            MouseConfigurationFilePath = Path.Combine(dir, "MouseBindings.xml");
        }

        /// <summary>
        /// Get the default configuration file path (AppData folder)
        /// </summary>
        private static string GetDefaultConfigurationPath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appConfigDir = Path.Combine(appDataPath, "HIDMate");

            if (!Directory.Exists(appConfigDir))
            {
                Directory.CreateDirectory(appConfigDir);
            }

            return Path.Combine(appConfigDir, "BindingConfigurations.xml");
        }

        /// <summary>
        /// Load configurations from disk
        /// </summary>
        public void Load()
        {
            try
            {
                log.Debug($"Loading from: {ConfigurationFilePath}");
                log.Debug($"File exists: {File.Exists(ConfigurationFilePath)}");

                if (File.Exists(ConfigurationFilePath))
                {
                    using (var fileStream = new FileStream(ConfigurationFilePath, FileMode.Open, FileAccess.Read))
                    {
                        object deserializedObject = ConfigSerializer.Deserialize(fileStream);
                        if (deserializedObject is List<ApplicationConfig> configs)
                        {
                            ApplicationConfigs = configs ?? new List<ApplicationConfig>();
                            log.Debug($"Deserialized {ApplicationConfigs.Count} configs successfully");
                        }
                        else
                        {
                            log.Debug($"Deserialization returned wrong type: {deserializedObject?.GetType().Name}");
                            ApplicationConfigs = new List<ApplicationConfig>();
                        }
                    }
                }
                else
                {
                    log.Debug($"Configuration file not found");
                    ApplicationConfigs = new List<ApplicationConfig>();
                }

                ConfigurationLoaded?.Invoke(this, EventArgs.Empty);
                log.Debug($"Loaded {ApplicationConfigs.Count} application configurations");
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error loading configuration");
                ApplicationConfigs = new List<ApplicationConfig>();
            }

            LoadSettings();
            LoadMouseConfiguration();

            if (MigrateLegacyMouseBindings())
            {
                // A previous build stored Mouse* bindings inside ApplicationConfig.
                // Move them to the global list once so they survive future loads,
                // then persist both files so the legacy entries don't get re-read.
                Save();
                SaveMouseConfiguration();
            }
        }

        private void LoadMouseConfiguration()
        {
            try
            {
                if (!File.Exists(MouseConfigurationFilePath))
                {
                    log.Debug($"Mouse configuration file not found: {MouseConfigurationFilePath}");
                    return;
                }

                using (var fileStream = new FileStream(MouseConfigurationFilePath, FileMode.Open, FileAccess.Read))
                {
                    if (MouseSerializer.Deserialize(fileStream) is MouseConfigurationFile mouseFile)
                    {
                        MouseBindings = mouseFile.Bindings ?? new List<InputBinding>();
                        MouseSensitivity = mouseFile.Sensitivity > 0 ? mouseFile.Sensitivity : 5;
                        log.Debug($"Loaded {MouseBindings.Count} mouse bindings, sensitivity {MouseSensitivity}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error loading mouse configuration");
                MouseBindings = new List<InputBinding>();
                MouseSensitivity = 5;
            }
        }

        public void SaveMouseConfiguration()
        {
            try
            {
                var dir = Path.GetDirectoryName(MouseConfigurationFilePath);

                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(MouseConfigurationFilePath))
                {
                    File.Copy(MouseConfigurationFilePath, MouseConfigurationFilePath + ".bak", true);
                }

                var payload = new MouseConfigurationFile
                {
                    Sensitivity = MouseSensitivity,
                    Bindings = MouseBindings ?? new List<InputBinding>(),
                };

                using (var fileStream = new FileStream(MouseConfigurationFilePath, FileMode.Create, FileAccess.Write))
                {
                    MouseSerializer.Serialize(fileStream, payload);
                }

                log.Debug($"Saved {payload.Bindings.Count} mouse bindings, sensitivity {payload.Sensitivity}");
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error saving mouse configuration");
            }
        }

        /// <summary>
        /// One-time migration: prior builds stored Mouse* bindings inside each
        /// ApplicationConfig. Move them out into the global MouseBindings list
        /// and clear the per-app copies. Returns true if anything was moved so
        /// the caller knows to persist.
        /// </summary>
        private bool MigrateLegacyMouseBindings()
        {
            bool migrated = false;

            foreach (var config in ApplicationConfigs)
            {
                var legacy = config.InputBindings
                    .Where(b => ApplicationConfig.IsMouseAction(b.Action))
                    .ToList();

                if (legacy.Count == 0)
                {
                    continue;
                }

                foreach (var binding in legacy)
                {
                    config.InputBindings.Remove(binding);
                    MouseBindings.Add(binding);
                }

                migrated = true;
                log.Debug($"Migrated {legacy.Count} mouse binding(s) from app '{config.DisplayName}' to global list");
            }

            return migrated;
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var lines = File.ReadAllLines(SettingsFilePath);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('=');
                        if (parts.Length == 2 && parts[0].Trim() == "VolumeStepPercent")
                        {
                            if (int.TryParse(parts[1].Trim(), out int val) && val >= 1 && val <= 10)
                                VolumeStepPercent = val;
                        }
                    }
                    log.Debug($"Loaded settings: VolumeStepPercent={VolumeStepPercent}");
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error loading settings");
            }
        }

        public void SaveSettings()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsFilePath);

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(SettingsFilePath, $"VolumeStepPercent={VolumeStepPercent}");
                log.Debug($"Saved settings: VolumeStepPercent={VolumeStepPercent}");
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error saving settings");
            }
        }

        /// <summary>
        /// Save configurations to disk
        /// </summary>
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigurationFilePath);

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Create backup before saving
                if (File.Exists(ConfigurationFilePath))
                {
                    var backupPath = ConfigurationFilePath + ".bak";
                    File.Copy(ConfigurationFilePath, backupPath, true);
                }

                using (var fileStream = new FileStream(ConfigurationFilePath, FileMode.Create, FileAccess.Write))
                {
                    ConfigSerializer.Serialize(fileStream, ApplicationConfigs);
                }

                ConfigurationSaved?.Invoke(this, EventArgs.Empty);
                log.Debug($"Saved {ApplicationConfigs.Count} application configurations");
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error saving configuration");
            }
        }

        /// <summary>
        /// Get all application configurations
        /// </summary>
        public List<ApplicationConfig> GetAllConfigurations()
        {
            return new List<ApplicationConfig>(ApplicationConfigs);
        }

        /// <summary>
        /// Get configuration by process name
        /// </summary>
        public ApplicationConfig GetConfigurationByProcessName(string processName)
        {
            foreach (var config in ApplicationConfigs)
            {
                if (config.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                {
                    return config;
                }
            }
            return null;
        }

        /// <summary>
        /// Get configuration by config ID
        /// </summary>
        public ApplicationConfig GetConfigurationById(string configId)
        {
            foreach (var config in ApplicationConfigs)
            {
                if (config.ConfigId == configId)
                {
                    return config;
                }
            }
            return null;
        }

        /// <summary>
        /// Add a new application configuration
        /// </summary>
        public ApplicationConfig AddConfiguration(string processName, string displayName)
        {
            // Check if already exists
            if (GetConfigurationByProcessName(processName) != null)
            {
                log.Debug($"Configuration for '{processName}' already exists");
                return null;
            }

            var config = new ApplicationConfig(processName, displayName);
            ApplicationConfigs.Add(config);
            ApplicationConfigAdded?.Invoke(this, new ApplicationConfigEventArgs { Config = config });
            log.Debug($"Added configuration for '{displayName}' ({processName})");
            return config;
        }

        /// <summary>
        /// Remove configuration by config ID
        /// </summary>
        public bool RemoveConfiguration(string configId)
        {
            var config = GetConfigurationById(configId);
            if (config != null)
            {
                ApplicationConfigs.Remove(config);
                ApplicationConfigRemoved?.Invoke(this, new ApplicationConfigEventArgs { Config = config });
                log.Debug($"Removed configuration for '{config.DisplayName}'");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Remove configuration by process name
        /// </summary>
        public bool RemoveConfigurationByProcessName(string processName)
        {
            var config = GetConfigurationByProcessName(processName);
            if (config != null)
            {
                return RemoveConfiguration(config.ConfigId);
            }
            return false;
        }

        /// <summary>
        /// Update an existing configuration
        /// </summary>
        public void UpdateConfiguration(ApplicationConfig config)
        {
            config.TouchModified();
            log.Debug($"Updated configuration for '{config.DisplayName}'");
        }

        /// <summary>
        /// Get all enabled configurations
        /// </summary>
        public List<ApplicationConfig> GetEnabledConfigurations()
        {
            var enabledConfigs = new List<ApplicationConfig>();
            foreach (var config in ApplicationConfigs)
            {
                if (config.Enabled)
                {
                    enabledConfigs.Add(config);
                }
            }
            return enabledConfigs;
        }

        /// <summary>
        /// Get all configurations that have enabled bindings
        /// </summary>
        public List<ApplicationConfig> GetConfigurationsWithBindings()
        {
            var configuratedApps = new List<ApplicationConfig>();
            foreach (var config in ApplicationConfigs)
            {
                if (config.HasEnabledBindings())
                {
                    configuratedApps.Add(config);
                }
            }
            return configuratedApps;
        }

        /// <summary>
        /// Clear all configurations (reset to empty)
        /// </summary>
        public void ClearAllConfigurations()
        {
            ApplicationConfigs.Clear();
            log.Debug("Cleared all configurations");
        }

        /// <summary>
        /// Get count of configurations
        /// </summary>
        public int GetConfigurationCount()
        {
            return ApplicationConfigs.Count;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    ApplicationConfigs.Clear();
                    ApplicationConfigs = null;
                }
                Disposed = true;
            }
        }
    }

    /// <summary>
    /// Event arguments for application configuration events
    /// </summary>
    public class ApplicationConfigEventArgs : EventArgs
    {
        public ApplicationConfig Config { get; set; }
    }
}
