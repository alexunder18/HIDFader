using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace HIDMate.Core
{
    /// <summary>
    /// Represents a complete configuration for a single application
    /// including all its input bindings for volume control
    /// </summary>
    [Serializable]
    [XmlRoot(ElementName = "ApplicationConfig")]
    public class ApplicationConfig
    {
        /// <summary>
        ///     Unique identifier for this application configuration
        /// </summary>
        [XmlAttribute]
        public string ConfigId { get; set; }

        /// <summary>
        /// Process name or executable name (e.g., "DCS.exe", "discord")
        /// </summary>
        [XmlAttribute]
        public string ProcessName { get; set; }

        /// <summary>
        /// User-friendly display name (e.g., "DCS World", "Discord")
        /// </summary>
        [XmlAttribute]
        public string DisplayName { get; set; }

        /// <summary>
        /// Full path to executable (optional, for non-currently-running apps)
        /// </summary>
        [XmlAttribute]
        public string ExecutablePath { get; set; }

        /// <summary>
        /// All input bindings for this application
        /// </summary>
        [XmlArrayItem(ElementName = "Binding")]
        public List<InputBinding> InputBindings { get; set; } = new List<InputBinding>();

        /// <summary>
        /// Is this application configuration enabled
        /// </summary>
        [XmlAttribute]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// When this configuration was created
        /// </summary>
        [XmlAttribute]
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// When this configuration was last modified
        /// </summary>
        [XmlAttribute]
        public DateTime LastModifiedDate { get; set; }

        /// <summary>
        /// Optional notes about this application
        /// </summary>
        [XmlElement]
        public string Notes { get; set; }

        public ApplicationConfig()
        {
            ConfigId = Guid.NewGuid().ToString();
            CreatedDate = DateTime.UtcNow;
            LastModifiedDate = DateTime.UtcNow;
        }

        public ApplicationConfig(string processName, string displayName) : this()
        {
            ProcessName = processName;
            DisplayName = displayName;
        }

        /// <summary>
        /// Get all bindings for a specific action (VolumeUp, VolumeDown, Mute)
        /// </summary>
        public List<InputBinding> GetBindingsForAction(string action)
        {
            var bindings = new List<InputBinding>();
            foreach (var binding in InputBindings)
            {
                if (binding.Enabled && binding.Action == action)
                {
                    bindings.Add(binding);
                }
            }
            return bindings;
        }

        /// <summary>
        /// Get all HID button bindings
        /// </summary>
        public List<InputBinding> GetHidBindings()
        {
            var bindings = new List<InputBinding>();
            foreach (var binding in InputBindings)
            {
                if (binding.Enabled && binding.InputType == "HIDButton")
                {
                    bindings.Add(binding);
                }
            }
            return bindings;
        }

        /// <summary>
        /// True for any Action starting with "Mouse". Mouse bindings are stored
        /// globally on BindingConfiguration, not per ApplicationConfig — this
        /// helper is kept here for the legacy-migration code path and for the
        /// listener's binding-routing checks.
        /// </summary>
        public static bool IsMouseAction(string action)
        {
            return !string.IsNullOrEmpty(action)
                && action.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get all keyboard bindings
        /// </summary>
        public List<InputBinding> GetKeyboardBindings()
        {
            var bindings = new List<InputBinding>();
            foreach (var binding in InputBindings)
            {
                if (binding.Enabled && binding.InputType == "KeyboardKey")
                {
                    bindings.Add(binding);
                }
            }
            return bindings;
        }

        /// <summary>
        /// Check if any bindings are configured for this application
        /// </summary>
        public bool HasBindings()
        {
            return InputBindings.Count > 0;
        }

        /// <summary>
        /// Check if any enabled bindings are configured for this application
        /// </summary>
        public bool HasEnabledBindings()
        {
            foreach (var binding in InputBindings)
            {
                if (binding.Enabled && binding.InputCodes != null && binding.InputCodes.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Create a binding for the specified action
        /// </summary>
        public InputBinding CreateBinding(string action, string inputType)
        {
            var binding = new InputBinding(action, inputType, $"{action} ({inputType})");
            InputBindings.Add(binding);
            LastModifiedDate = DateTime.UtcNow;
            return binding;
        }

        /// <summary>
        /// Remove a binding by its ID
        /// </summary>
        public bool RemoveBinding(string bindingId)
        {
            var binding = InputBindings.Find(b => b.BindingId == bindingId);
            if (binding != null)
            {
                InputBindings.Remove(binding);
                LastModifiedDate = DateTime.UtcNow;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Update the last modified timestamp
        /// </summary>
        public void TouchModified()
        {
            LastModifiedDate = DateTime.UtcNow;
        }

        public override string ToString()
        {
            return DisplayName ?? ProcessName;
        }
    }
}
