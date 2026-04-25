using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace HIDFader.Core
{
    /// <summary>
    /// Represents a single input binding (HID button, keyboard key, or combination)
    /// </summary>
    [Serializable]
    public class InputBinding
    {
        /// <summary>
        /// Unique identifier for this binding
        /// </summary>
        [XmlAttribute]
        public string BindingId { get; set; }

        /// <summary>
        /// The action this binding triggers (VolumeUp, VolumeDown, Mute)
        /// </summary>
        [XmlAttribute]
        public string Action { get; set; }

        /// <summary>
        /// Input type (HIDButton, KeyboardKey, etc.)
        /// </summary>
        [XmlAttribute]
        public string InputType { get; set; }

        /// <summary>
        /// The actual input value (button code, key code, etc.)
        /// </summary>
        [XmlElement]
        public List<string> InputCodes { get; set; } = new List<string>();

        /// <summary>
        /// For HIDAxis bindings only: when true, the normalized axis value is
        /// flipped (volume = 1 - axis) so "axis toward zero" = "volume to max".
        /// Useful for throttles/sliders that report inverted physical direction.
        /// </summary>
        [XmlAttribute]
        public bool Inverted { get; set; }

        /// <summary>
        /// VID/PID-derived GUID of the HID device this binding was captured from.
        /// Stable across USB ports and reboots. Empty for keyboard bindings or
        /// legacy HID bindings captured before this field existed (those fall back
        /// to device-name matching).
        /// </summary>
        [XmlAttribute]
        public string ProductGuid { get; set; }

        /// <summary>
        /// Modifier buttons/keys that must be held (optional)
        /// </summary>
        [XmlElement]
        public List<string> ModifierCodes { get; set; } = new List<string>();

        /// <summary>
        /// For KeyboardEmit bindings: ordered sequence of keyboard chords
        /// (e.g., "A", "Ctrl+C", "F1") to send when the HID input triggers.
        /// Each chord is pressed and released in order.
        /// </summary>
        [XmlElement]
        public List<string> OutputKeys { get; set; } = new List<string>();

        /// <summary>
        /// User-friendly description of the binding
        /// </summary>
        [XmlAttribute]
        public string Description { get; set; }

        /// <summary>
        /// Is this binding enabled
        /// </summary>
        [XmlAttribute]
        public bool Enabled { get; set; } = true;

        public InputBinding()
        {
        }

        public InputBinding(string action, string inputType, string description)
        {
            BindingId = Guid.NewGuid().ToString();
            Action = action;
            InputType = inputType;
            Description = description;
        }

        public override string ToString()
        {
            return Description ?? $"{Action} ({InputType})";
        }
    }
}
