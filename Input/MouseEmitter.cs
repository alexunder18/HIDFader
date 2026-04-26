using NLog;
using System;
using System.Runtime.InteropServices;

namespace HIDMate.Input
{
    /// <summary>
    /// Emits relative mouse movement, button clicks, and wheel scrolls via
    /// the legacy Win32 mouse_event API. SendInput would be more modern, but
    /// mouse_event is one call per event and avoids duplicating the INPUT
    /// struct definitions already used by KeyboardEmitter.
    /// </summary>
    public static class MouseEmitter
    {
        private static readonly Logger log = LogManager.GetCurrentClassLogger();

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const int WHEEL_DELTA = 120;

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, IntPtr dwExtraInfo);

        public static void Move(int dx, int dy)
        {
            if (dx == 0 && dy == 0)
                return;

            try
            {
                mouse_event(MOUSEEVENTF_MOVE, dx, dy, 0, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Mouse move failed");
            }
        }

        public static void LeftClick()
        {
            try
            {
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Mouse left click failed");
            }
        }

        public static void RightClick()
        {
            try
            {
                mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, IntPtr.Zero);
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Mouse right click failed");
            }
        }

        /// <summary>
        /// Scroll the wheel by the given number of notches (positive = up, negative = down).
        /// </summary>
        public static void Scroll(int notches)
        {
            if (notches == 0)
                return;

            try
            {
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, notches * WHEEL_DELTA, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Mouse scroll failed");
            }
        }
    }
}
