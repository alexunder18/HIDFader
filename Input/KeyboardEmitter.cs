using NLog;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace HIDFader.Input
{
    /// <summary>
    /// Emits keyboard chords via Win32 SendInput. A "chord" is a token like
    /// "A", "F1", "Ctrl+C", or "Ctrl+Shift+K" — one or more modifiers (+),
    /// with a single main key at the end. Modifiers are pressed, the main key
    /// is pressed and released, then modifiers are released in reverse order.
    /// </summary>
    public static class KeyboardEmitter
    {
        private static readonly Logger log = LogManager.GetCurrentClassLogger();

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        /// <summary>
        /// Emit a sequence of chords one after another. Runs synchronously on the
        /// calling thread — callers on the joystick polling thread should offload
        /// to the thread pool to avoid blocking the poll loop.
        /// </summary>
        public static void EmitSequence(IList<string> chords, int delayBetweenMs = 20)
        {
            if (chords == null || chords.Count == 0)
                return;

            for (int i = 0; i < chords.Count; i++)
            {
                var chord = chords[i];
                if (string.IsNullOrWhiteSpace(chord))
                    continue;

                try
                {
                    EmitChord(chord);
                }
                catch (Exception ex)
                {
                    log.Error(ex, "Error emitting chord '{0}'", chord);
                }

                if (delayBetweenMs > 0 && i < chords.Count - 1)
                {
                    Thread.Sleep(delayBetweenMs);
                }
            }
        }

        public static void EmitChord(string chord)
        {
            if (string.IsNullOrWhiteSpace(chord))
                return;

            var parts = chord.Split('+');
            var modVKs = new List<ushort>();
            ushort mainVK = 0;

            for (int i = 0; i < parts.Length; i++)
            {
                var token = parts[i].Trim();
                if (token.Length == 0)
                    continue;

                if (i < parts.Length - 1)
                {
                    if (TryResolveModifier(token, out ushort m))
                        modVKs.Add(m);
                }
                else
                {
                    mainVK = ResolveKey(token);
                }
            }

            if (mainVK == 0)
            {
                log.Debug($"Chord '{chord}' did not resolve to a key; skipping");
                return;
            }

            var inputs = new List<INPUT>(modVKs.Count * 2 + 2);

            foreach (var m in modVKs)
                inputs.Add(MakeKey(m, keyUp: false));

            inputs.Add(MakeKey(mainVK, keyUp: false));
            inputs.Add(MakeKey(mainVK, keyUp: true));

            for (int i = modVKs.Count - 1; i >= 0; i--)
                inputs.Add(MakeKey(modVKs[i], keyUp: true));

            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        private static INPUT MakeKey(ushort vk, bool keyUp)
        {
            var inp = new INPUT { type = INPUT_KEYBOARD };
            inp.u.ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };
            return inp;
        }

        private static bool TryResolveModifier(string token, out ushort vk)
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    vk = (ushort)Keys.ControlKey; return true;
                case "lctrl":
                    vk = (ushort)Keys.LControlKey; return true;
                case "rctrl":
                    vk = (ushort)Keys.RControlKey; return true;
                case "shift":
                    vk = (ushort)Keys.ShiftKey; return true;
                case "lshift":
                    vk = (ushort)Keys.LShiftKey; return true;
                case "rshift":
                    vk = (ushort)Keys.RShiftKey; return true;
                case "alt":
                case "menu":
                    vk = (ushort)Keys.Menu; return true;
                case "lalt":
                    vk = (ushort)Keys.LMenu; return true;
                case "ralt":
                    vk = (ushort)Keys.RMenu; return true;
                case "win":
                case "lwin":
                    vk = (ushort)Keys.LWin; return true;
                case "rwin":
                    vk = (ushort)Keys.RWin; return true;
                default:
                    vk = 0; return false;
            }
        }

        private static ushort ResolveKey(string token)
        {
            if (Enum.TryParse<Keys>(token, true, out var k))
                return (ushort)k;

            // Common aliases users may have typed manually in config — not required
            // from the UI (which always stores the Keys enum name), but cheap to accept.
            switch (token.ToLowerInvariant())
            {
                case "esc": return (ushort)Keys.Escape;
                case "enter": return (ushort)Keys.Enter;
                case "return": return (ushort)Keys.Return;
                case "space": return (ushort)Keys.Space;
                case "tab": return (ushort)Keys.Tab;
                case "backspace": return (ushort)Keys.Back;
                case "del": return (ushort)Keys.Delete;
                case "ins": return (ushort)Keys.Insert;
                case "pgup": return (ushort)Keys.PageUp;
                case "pgdn": return (ushort)Keys.PageDown;
            }

            return 0;
        }

        /// <summary>
        /// Turns a WinForms KeyDown event into a chord string of the form
        /// "Ctrl+Shift+A" / "A" / "F1". Returns null if the key is a modifier-only
        /// keystroke (user has not yet pressed the main key).
        /// </summary>
        public static string BuildChordFromKeyEvent(KeyEventArgs e)
        {
            var main = e.KeyCode;

            if (main == Keys.ControlKey || main == Keys.LControlKey || main == Keys.RControlKey
                || main == Keys.ShiftKey || main == Keys.LShiftKey || main == Keys.RShiftKey
                || main == Keys.Menu || main == Keys.LMenu || main == Keys.RMenu
                || main == Keys.LWin || main == Keys.RWin
                || main == Keys.None)
            {
                return null;
            }

            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Shift) parts.Add("Shift");
            if (e.Alt) parts.Add("Alt");
            parts.Add(main.ToString());
            return string.Join("+", parts);
        }
    }
}
