using HIDMate.Core;

namespace HIDMate.Input
{
    public partial class VolumeControlListener
    {
        private class HeldInputState
        {
            public long PressTime { get; set; }
            public string Action { get; set; }

            // Null for global mouse bindings — those don't belong to any single app.
            public ApplicationConfig Config { get; set; }

            // Captured at press-time so a sensitivity change mid-hold doesn't
            // alter the speed of an already-pressed mouse button. Only meaningful
            // for mouse actions; ignored otherwise.
            public int MouseSensitivity { get; set; }

            public bool HasExecutedInitial { get; set; }
        }
    }
}

