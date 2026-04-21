using HIDFader.Core;

namespace HIDFader.Input
{
public partial class VolumeControlListener
    {
        private class HeldInputState
        {
            public long PressTime { get; set; }
            public string Action { get; set; }
            public ApplicationConfig Config { get; set; }
            public bool HasExecutedInitial { get; set; }
        }
    }
}
