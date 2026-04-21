using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace HIDFader.Core
{
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
        private XmlSerializer ConfigSerializer { get; set; }
        private bool Disposed { get; set; }

        public int VolumeStepPercent { get; set; } = 1;

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
            SettingsFilePath = Path.Combine(Path.GetDirectoryName(ConfigurationFilePath), "Settings.xml");
            ConfigSerializer = new XmlSerializer(typeof(List<ApplicationConfig>), new XmlRootAttribute("ApplicationConfigurations"));
        }

        public BindingConfiguration(string configPath) : this()
        {
            ConfigurationFilePath = configPath;
            SettingsFilePath = Path.Combine(Path.GetDirectoryName(configPath), "Settings.xml");
        }

        /// <summary>
        /// Get the default configuration file path (AppData folder)
        /// </summary>
        private static string GetDefaultConfigurationPath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appConfigDir = Path.Combine(appDataPath, "HIDFader");

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
