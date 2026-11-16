using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ClipboardTyper
{
    /// <summary>
    /// Types text as real keystrokes (like KeePass auto-type), so it works in
    /// remote tools that block clipboard paste. Run it on a background thread.
    /// </summary>
    internal sealed class AutoTyper
    {
        public const ushort VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10,
            VK_CONTROL = 0x11, VK_MENU = 0x12, VK_CAPITAL = 0x14, VK_ESCAPE = 0x1B, VK_SPACE = 0x20,
            VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24, VK_LEFT = 0x25, VK_UP = 0x26,
            VK_RIGHT = 0x27, VK_DOWN = 0x28, VK_SNAPSHOT = 0x2C, VK_INSERT = 0x2D, VK_DELETE = 0x2E,
            VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_APPS = 0x5D, VK_DIVIDE = 0x6F, VK_F4 = 0x73, VK_F5 = 0x74,
            VK_NUMLOCK = 0x90, VK_RCONTROL = 0xA3, VK_RMENU = 0xA5;

        private IntPtr target = IntPtr.Zero;   // window that must stay in the foreground
        private IntPtr hkl;                    // keyboard layout of the target window
        private readonly int keyDelay;
        private volatile bool cancelRequested;

        public string StopReason { get; private set; }

        /// <summary>
        /// Created when typing is requested, before the start delay, so Esc and Cancel() work
        /// from the very beginning. Call Attach() once the target window is known.
        /// </summary>
        public AutoTyper(int keyDelayMs)
        {
            keyDelay = Math.Max(0, keyDelayMs);
            hkl = NativeMethods.GetKeyboardLayout(0);
        }

        /// <summary>Sets the window to type into and uses its keyboard layout.</summary>
        public void Attach(IntPtr targetWindow)
        {
            target = targetWindow;
            IntPtr layout = IntPtr.Zero;
            if (targetWindow != IntPtr.Zero)
            {
                uint pid;
                uint tid = NativeMethods.GetWindowThreadProcessId(targetWindow, out pid);
                layout = NativeMethods.GetKeyboardLayout(tid);
            }
            if (layout == IntPtr.Zero) layout = NativeMethods.GetKeyboardLayout(0);
            hkl = layout;
        }

        public void Cancel() { cancelRequested = true; }

        /// <summary>Waits the given time. Returns false if cancelled (Esc or Cancel()) meanwhile.</summary>
        public bool Wait(int ms)
        {
            int waited = 0;
            while (waited < ms)
            {
                if (IsCancelled()) return false;
                int step = Math.Min(20, ms - waited);
                Thread.Sleep(step);
                waited += step;
            }
            return !IsCancelled();
        }

        /// <summary>
        /// Waits until the user has let go of Ctrl/Alt/Shift/Win (and optionally another key).
        /// Returns false if cancelled (Esc or Cancel()) meanwhile.
        /// </summary>
        public bool WaitForKeysReleased(int timeoutMs, int extraVk)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (IsCancelled()) return false;
                bool down = IsDown(VK_SHIFT) || IsDown(VK_CONTROL) || IsDown(VK_MENU) ||
                            IsDown(VK_LWIN) || IsDown(VK_RWIN) || (extraVk != 0 && IsDown(extraVk));
                if (!down) break;
                Thread.Sleep(20);
                waited += 20;
            }
            return Wait(50);
        }

        private static bool IsDown(int vk)
        {
            return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        /// <summary>True if Cancel() was called or Esc is held; sets StopReason.</summary>
        private bool IsCancelled()
        {
            if (cancelRequested) { if (StopReason == null) StopReason = "Cancelled"; return true; }
            if (IsDown(VK_ESCAPE)) { StopReason = "Cancelled (Esc)"; return true; }
            return false;
        }

        private bool ShouldStop()
        {
            if (IsCancelled()) return true;
            if (target != IntPtr.Zero && NativeMethods.GetForegroundWindow() != target)
            {
                StopReason = "Stopped: target window lost focus";
                return true;
            }
            return false;
        }

        /// <summary>Types the text. Returns false if it was cancelled.</summary>
        public bool TypeText(string text)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');

            // With Caps Lock on, Shift would invert letters - switch it off while typing.
            bool caps = (NativeMethods.GetKeyState(VK_CAPITAL) & 1) != 0;
            if (caps) Tap(VK_CAPITAL);
            try
            {
                foreach (char c in text)
                {
                    if (ShouldStop()) return false;
                    TypeChar(c);
                }
            }
            finally
            {
                if (caps) Tap(VK_CAPITAL);
            }
            return true;
        }

        /// <summary>Presses the keys in order, then releases them in reverse (e.g. Ctrl+Alt+Del).</summary>
        public bool SendCombo(ushort[] keys)
        {
            if (ShouldStop()) return false;
            int hold = Math.Max(30, keyDelay);
            for (int i = 0; i < keys.Length; i++) { Key(keys[i], false); Thread.Sleep(hold); }
            for (int i = keys.Length - 1; i >= 0; i--) { Key(keys[i], true); Thread.Sleep(hold); }
            return true;
        }

        public void PressEnter()
        {
            if (!ShouldStop()) Tap(VK_RETURN);
        }

        private void TypeChar(char c)
        {
            if (c == '\n') { Tap(VK_RETURN); return; }
            if (c == '\t') { Tap(VK_TAB); return; }

            short scan = NativeMethods.VkKeyScanEx(c, hkl);
            int vk = scan & 0xFF;
            int state = (scan >> 8) & 0xFF;
            bool shift = (state & 1) != 0, ctrl = (state & 2) != 0, alt = (state & 4) != 0;

            // Not on this keyboard layout, needs exotic modifiers, or is a control char -> send as Unicode.
            if (scan == -1 || vk == 0xFF || (state & 0x38) != 0 || (ctrl && !alt) || (alt && !ctrl))
            {
                TypeUnicode(c);
                return;
            }

            // Verify the key really produces this character (e.g. on Swiss layouts VkKeyScan maps
            // 'Ä' to the plain 'ä' key, which only works with Caps Lock). Otherwise use Unicode.
            bool dead;
            if (!ProducesChar((uint)vk, shift, ctrl, alt, c, out dead))
            {
                TypeUnicode(c);
                return;
            }

            if (ctrl) Key(VK_CONTROL, false);   // Ctrl+Alt = AltGr
            if (alt) Key(VK_MENU, false);
            if (shift) Key(VK_SHIFT, false);
            Key((ushort)vk, false);
            HoldPause();
            Key((ushort)vk, true);
            if (shift) Key(VK_SHIFT, true);
            if (alt) Key(VK_MENU, true);
            if (ctrl) Key(VK_CONTROL, true);
            GapPause();

            // Dead keys (^ ` ´ ~ ¨ on many European layouts) only produce the character
            // when followed by a space.
            if (dead) Tap(VK_SPACE);
        }

        /// <summary>
        /// Asks the keyboard layout what this key + modifiers produces. Returns true if it is
        /// exactly <paramref name="c"/>; <paramref name="dead"/> tells whether it is a dead key.
        /// </summary>
        private bool ProducesChar(uint vk, bool shift, bool ctrl, bool alt, char c, out bool dead)
        {
            byte[] keyState = new byte[256];
            if (shift) { keyState[VK_SHIFT] = 0x80; keyState[0xA0] = 0x80; }
            if (ctrl) { keyState[VK_CONTROL] = 0x80; keyState[0xA2] = 0x80; }
            if (alt) { keyState[VK_MENU] = 0x80; keyState[VK_RMENU] = 0x80; }
            StringBuilder sb = new StringBuilder(8);
            uint sc = NativeMethods.MapVirtualKeyEx(vk, NativeMethods.MAPVK_VK_TO_VSC, hkl);
            // Flag 0x4: do not change the keyboard state (Windows 10 1607+).
            int rc = NativeMethods.ToUnicodeEx(vk, sc, keyState, sb, sb.Capacity, 0x4, hkl);
            dead = rc < 0;
            if (rc == 0 || sb.Length == 0) return false;
            if (rc > 1) return false;
            return sb[0] == c;
        }

        private void Tap(ushort vk)
        {
            Key(vk, false);
            HoldPause();
            Key(vk, true);
            GapPause();
        }

        private void HoldPause() { if (keyDelay > 0) Thread.Sleep(Math.Max(1, keyDelay / 2)); }
        private void GapPause() { if (keyDelay > 0) Thread.Sleep(keyDelay); }

        private void Key(ushort vk, bool up)
        {
            NativeMethods.INPUT input = new NativeMethods.INPUT();
            input.type = NativeMethods.INPUT_KEYBOARD;
            input.u.ki.wVk = vk;
            input.u.ki.wScan = (ushort)NativeMethods.MapVirtualKeyEx(vk, NativeMethods.MAPVK_VK_TO_VSC, hkl);
            uint flags = up ? NativeMethods.KEYEVENTF_KEYUP : 0;
            if (IsExtended(vk)) flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
            input.u.ki.dwFlags = flags;
            Send(input);
        }

        private void TypeUnicode(char c)
        {
            NativeMethods.INPUT down = new NativeMethods.INPUT();
            down.type = NativeMethods.INPUT_KEYBOARD;
            down.u.ki.wVk = 0;
            down.u.ki.wScan = c;
            down.u.ki.dwFlags = NativeMethods.KEYEVENTF_UNICODE;
            NativeMethods.INPUT up = down;
            up.u.ki.dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP;
            Send(down);
            HoldPause();
            Send(up);
            GapPause();
        }

        private static void Send(NativeMethods.INPUT input)
        {
            NativeMethods.SendInput(1, new NativeMethods.INPUT[] { input },
                Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        private static bool IsExtended(ushort vk)
        {
            switch (vk)
            {
                case VK_PRIOR: case VK_NEXT: case VK_END: case VK_HOME:
                case VK_LEFT: case VK_UP: case VK_RIGHT: case VK_DOWN:
                case VK_INSERT: case VK_DELETE: case VK_SNAPSHOT:
                case VK_LWIN: case VK_RWIN: case VK_APPS:
                case VK_DIVIDE: case VK_NUMLOCK: case VK_RCONTROL: case VK_RMENU:
                    return true;
                default:
                    return false;
            }
        }
    }
}
