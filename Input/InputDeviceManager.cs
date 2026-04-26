using NLog;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;

namespace HIDMate.Input
{
    /// <summary>
    /// Manages DirectInput device enumeration
    /// </summary>
    public class InputDeviceManager : IDisposable
    {
        private readonly Logger log = LogManager.GetCurrentClassLogger();
        private DirectInput directInput;
        private bool disposed;

        public InputDeviceManager()
        {
            directInput = new DirectInput();
        }

        /// <summary>
        /// Gets all attached game control devices
        /// </summary>
        public List<JoystickDevice> GetGameControlDevices()
        {
            var devices = new List<JoystickDevice>();

            try
            {
                var deviceInstances = directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);

                foreach (var deviceInstance in deviceInstances)
                {
                    devices.Add(new JoystickDevice(deviceInstance));
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Failed to enumerate devices");
            }

            return devices;
        }

        /// <summary>
        /// Creates a joystick instance for the specified device
        /// </summary>
        public Joystick CreateJoystick(JoystickDevice device)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }

            return new Joystick(directInput, device.InstanceGuid);
        }

        /// <summary>
        /// Creates a joystick instance by GUID
        /// </summary>
        public Joystick CreateJoystick(Guid instanceGuid)
        {
            return new Joystick(directInput, instanceGuid);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            directInput?.Dispose();
            disposed = true;
        }       
    }
}
