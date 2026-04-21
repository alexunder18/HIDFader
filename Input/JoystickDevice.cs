using SharpDX.DirectInput;
using System;
using System.Collections.Generic;

namespace HIDFader.Input
{
    /// <summary>
    /// Represents a joystick device
    /// </summary>
    public class JoystickDevice
    {
        public Guid InstanceGuid { get; }
        public Guid ProductGuid { get; }
        public string ProductName { get; }
        public DeviceInstance DeviceInstance { get; }

        public JoystickDevice(DeviceInstance deviceInstance)
        {
            DeviceInstance = deviceInstance;
            InstanceGuid = deviceInstance.InstanceGuid;
            ProductGuid = deviceInstance.ProductGuid;
            ProductName = deviceInstance.ProductName;
        }

        public override string ToString()
        {
            return ProductName;
        }
    }
}
