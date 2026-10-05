// Sends a TypingPlan to another window as real key presses, and pauses when the user
// takes focus away, clicks, or presses Esc.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AutoTyper
{
    static class Native
    {
        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_UNICODE = 0x0004;
        public const int VK_LBUTTON = 0x01;
        public const int VK_RBUTTON = 0x02;
        public const int VK_BACK = 0x08;
        public const int VK_TAB = 0x09;
        public const int VK_RETURN = 0x0D;
        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;
        public const int VK_CAPITAL = 0x14;
        public const int VK_ESCAPE = 0x1B;
        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;
        public const int VK_NUMPAD0 = 0x60;
        public const int SM_SWAPBUTTON = 23;
        public const uint GA_ROOT = 2;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const int SW_RESTORE = 9;
        public const int WM_SETREDRAW = 0x000B;
        public const int EM_GETSCROLLPOS = 0x04DD;
        public const int EM_SETSCROLLPOS = 0x04DE;

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public InputUnion u; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        public const uint GW_HWNDPREV = 3;
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern short VkKeyScanEx(char ch, IntPtr layout);
        [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref POINT lParam);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    }

    // Why typing stopped. CanResume is true for a pause, so typing can continue from the same spot.
    class Stopped : Exception
    {
        public readonly bool CanResume;
        public Stopped(string message, bool canResume) : base(message) { CanResume = canResume; }
    }

    // Remote PC mode: on, off, or on only for remote desktop / virtual machine windows.
    enum RemoteMode { Automatic, On, Off }

    class Typist
    {
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        // Clicking the desktop or taskbar should not start typing there.
        static readonly HashSet<string> ShellClasses = new HashSet<string> { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

        // Remote desktop and virtual machine windows (process names). They forward key presses to
        // the other PC, but often drop the Unicode key events used for symbols that aren't on the
        // keyboard, and a fast key-up that gets lost makes the other PC auto-repeat the key.
        static readonly HashSet<string> RemoteViewers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "mstsc", "msrdc", "parsecd", "anydesk", "teamviewer", "rustdesk", "vncviewer", "tvnviewer", "nxplayer",
            "vmware", "vmplayer", "virtualboxvm", "vmconnect",
        };

        // In Remote PC mode every key is held at least this long, with at least this gap before
        // the next one, so the remote side sees each press and release clearly.
        const double RemoteHold = 0.045, RemoteGap = 0.035;

        // Characters with no Alt code that look exactly like a keyboard character (minus signs, hyphens).
        static readonly Dictionary<char, char> Lookalikes = new Dictionary<char, char> {
            {'−', '-'}, {'‐', '-'}, {'‑', '-'}, {'‒', '-'}, {'﹣', '-'}, {'－', '-'},
        };

        static readonly Encoding Ansi = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        static readonly Encoding Oem = Encoding.GetEncoding(437, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        readonly Random rng = new Random();
        readonly IntPtr own;  // this app's window
        volatile bool stopRequested, pauseRequested;
        IntPtr target = IntPtr.Zero;
        IntPtr layout = IntPtr.Zero;
        volatile bool remoteTarget;
        double lastKeyUp;

        public volatile int Progress;      // text[0..Progress) is typed correctly
        public volatile int Dirty;         // keys typed after Progress that are still in the box (a typo in progress)
        public volatile bool Typing;       // keys are being sent (not waiting for the target)
        public volatile bool ShiftEnter;   // new line = Shift+Enter
        public volatile bool PauseOnClick;
        public volatile int Remote;        // a RemoteMode
        public volatile int UnsureSymbols; // symbols with no Alt code, sent as Unicode to a remote window

        // Careful key timing, and symbols as Alt codes: always, or (Automatic) for remote desktop / VM windows.
        public bool InRemoteMode
        {
            get { return Remote == (int)RemoteMode.On || (Remote == (int)RemoteMode.Automatic && remoteTarget); }
        }

        public Typist(IntPtr own, bool shiftEnter, bool pauseOnClick)
        {
            this.own = own;
            ShiftEnter = shiftEnter;
            PauseOnClick = pauseOnClick;
        }

        public void Stop() { stopRequested = true; }
        public void Pause() { pauseRequested = true; }

        // Get ready to continue after a pause.
        public void Rearm()
        {
            stopRequested = false;
            pauseRequested = false;
        }

        public IntPtr Target { get { return target; } }

        void SetTarget(IntPtr hwnd)
        {
            uint pid;
            layout = Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(hwnd, out pid));
            remoteTarget = IsRemoteViewer(pid);
            lastKeyUp = 0;
            target = hwnd;
            ForgetEarlierPresses();  // the click that picked the box must not pause typing
        }

        static bool IsRemoteViewer(uint pid)
        {
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                    return RemoteViewers.Contains(p.ProcessName);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static double Now() { return Clock.Elapsed.TotalSeconds; }

        static bool Pressed(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }

        // Held now, or pressed and let go since the last look: a quick tap (touchpads, remote
        // desktop) can fall between two polls. Looking clears it.
        static bool Hit(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8001) != 0; }

        static void ForgetEarlierPresses()
        {
            Hit(Native.VK_ESCAPE);
            Hit(Native.VK_LBUTTON);
            Hit(Native.VK_RBUTTON);
        }

        bool IsOwn(IntPtr hwnd) { return hwnd != IntPtr.Zero && Native.GetAncestor(hwnd, Native.GA_ROOT) == own; }

        void Check()
        {
            if (stopRequested)
                throw new Stopped("Stopped.", false);
            if (pauseRequested)
                throw new Stopped("Paused.", true);
            if (Hit(Native.VK_ESCAPE))
                throw new Stopped("Paused (Esc).", true);
            if (target == IntPtr.Zero)
                return;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != target)
                throw new Stopped(IsOwn(fg) ? "Paused." : "Paused: you switched to another window.", true);
            if (PauseOnClick && (Hit(Native.VK_LBUTTON) | Hit(Native.VK_RBUTTON)))  // | : clear both
            {
                Native.POINT p;
                Native.GetCursorPos(out p);
                throw new Stopped(IsOwn(Native.WindowFromPoint(p)) ? "Paused." :
                                  "Paused: you clicked, so the cursor may have moved.", true);
            }
        }

        public void Wait(double seconds)
        {
            double end = Now() + seconds;
            while (true)
            {
                Check();
                double left = end - Now();
                if (left <= 0)
                    return;
                Thread.Sleep(Math.Max(1, Math.Min(10, (int)(left * 1000))));
            }
        }

        // Typing while the user holds Ctrl, Alt or Win would trigger shortcuts, so wait it out.
        void WaitForModifiers()
        {
            while (Pressed(Native.VK_CONTROL) || Pressed(Native.VK_MENU) || Pressed(Native.VK_LWIN) || Pressed(Native.VK_RWIN))
                Wait(0.02);
        }

        // Block until the user clicks into another window, which becomes the target.
        public void WaitForClick()
        {
            target = IntPtr.Zero;
            ForgetEarlierPresses();  // e.g. the click on Start, or an old Esc
            // GetAsyncKeyState reads physical buttons, and left-handed setups swap them.
            int button = Native.GetSystemMetrics(Native.SM_SWAPBUTTON) != 0 ? Native.VK_RBUTTON : Native.VK_LBUTTON;
            while (true)
            {
                while (!Hit(button))
                    Wait(0.01);
                while (Pressed(button))
                    Wait(0.01);
                Wait(0.15);  // let the click activate the window
                IntPtr fg = Native.GetForegroundWindow();
                if (fg != IntPtr.Zero && !IsOwn(fg) && !ShellClasses.Contains(ClassName(fg)))
                {
                    SetTarget(fg);
                    return;
                }
            }
        }

        // Use a window that was just brought to the front for us (the remembered or paused target).
        public bool UseTarget(IntPtr hwnd)
        {
            target = IntPtr.Zero;
            ForgetEarlierPresses();  // e.g. the click on Resume
            double end = Now() + 1.5;
            while (Native.GetForegroundWindow() != hwnd)
            {
                if (Now() > end)
                    return false;
                Wait(0.02);
            }
            SetTarget(hwnd);
            return true;
        }

        static string ClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            Native.GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        // A pause can land in the middle of a typo; erase the wrong keys before going on.
        public void EraseDirty()
        {
            while (Dirty > 0)
            {
                Wait(Uniform(0.08, 0.16));
                WaitForModifiers();
                Tap(Native.VK_BACK, Uniform(0.03, 0.07), false);
                Dirty--;
            }
        }

        public void Run(IEnumerable<TypingAction> plan)
        {
            // Follow the plan's timeline, so extra real time (like pressing Shift)
            // comes out of the next pause instead of slowing the typing down.
            double clock = Now();
            foreach (TypingAction a in plan)
            {
                switch (a.Kind)
                {
                    case ActionKind.Wait:
                        clock += a.Seconds;
                        double left = clock - Now();
                        if (left > 0)
                            Wait(left);
                        else
                        {
                            Check();
                            clock = Now();  // running late: don't burst to catch up
                        }
                        break;
                    case ActionKind.Pos:
                        Progress = a.Pos;
                        Dirty = 0;
                        break;
                    case ActionKind.Key:
                        Check();
                        WaitForModifiers();
                        clock += a.Seconds;
                        Press(a.Char, a.Seconds);
                        Dirty++;
                        break;
                    case ActionKind.Back:
                        Check();
                        WaitForModifiers();
                        clock += a.Seconds;
                        Tap(Native.VK_BACK, a.Seconds, false);
                        Dirty--;
                        break;
                }
            }
        }

        void Press(char ch, double hold)
        {
            if (ch == '\n')
                Tap(Native.VK_RETURN, hold, ShiftEnter);
            else if (ch == '\t')
                Tap(Native.VK_TAB, hold, false);
            else
            {
                short scan = Native.VkKeyScanEx(ch, layout);
                if (scan == -1 || (scan & 0x600) != 0)  // not on this keyboard layout, or needs Ctrl/Alt
                {
                    TypeSymbol(ch, hold);
                    return;
                }
                bool shift = (scan & 0x100) != 0;
                if (char.IsLetter(ch) && (Native.GetKeyState(Native.VK_CAPITAL) & 1) != 0)
                    shift = !shift;
                Tap(scan & 0xFF, hold, shift);
            }
        }

        // A character that isn't on the keyboard, like ², — or é. Unicode key events work in apps on
        // this PC, but remote desktop and VM windows often drop them, so there it goes in as a
        // Windows Alt code (Alt + digits on the number pad): plain key presses that get through.
        void TypeSymbol(char ch, double hold)
        {
            if (InRemoteMode)
            {
                string code = AltCode(ch);
                if (code != null)
                {
                    TapAltCode(code);
                    return;
                }
                char plain;
                if (Lookalikes.TryGetValue(ch, out plain))
                {
                    Press(plain, hold);
                    return;
                }
                if (!char.IsLowSurrogate(ch))  // count an emoji once
                    UnsureSymbols++;
            }
            TapUnicode(ch, hold);
        }

        // "0178" for ² (Windows-1252: Alt+0178), "243" for ≤ (code page 437: Alt+243), or null.
        static string AltCode(char ch)
        {
            int b = SingleByte(Ansi, ch);
            if (b >= 0)
                return "0" + b.ToString("D3");
            b = SingleByte(Oem, ch);
            return b >= 128 ? b.ToString() : null;
        }

        static int SingleByte(Encoding encoding, char ch)
        {
            if (char.IsSurrogate(ch))
                return -1;
            try
            {
                byte[] bytes = encoding.GetBytes(new[] { ch });
                return bytes.Length == 1 ? bytes[0] : -1;
            }
            catch (EncoderFallbackException)
            {
                return -1;
            }
        }

        // Alt codes are only used in Remote PC mode, so every step gets the careful timing.
        void TapAltCode(string digits)
        {
            KeyGap();
            Key(Native.VK_MENU, false);
            Sleep(RemoteGap);
            foreach (char d in digits)
            {
                int vk = Native.VK_NUMPAD0 + (d - '0');
                Key(vk, false);
                Sleep(RemoteGap);
                Key(vk, true);
                Sleep(RemoteGap);
            }
            Key(Native.VK_MENU, true);
            lastKeyUp = Now();
        }

        // Taps are never interrupted, so a key is never left held down.
        void Tap(int vk, double hold, bool shift)
        {
            bool careful = InRemoteMode;
            if (careful)
            {
                KeyGap();
                hold = Math.Max(hold, RemoteHold);
            }
            if (shift)
            {
                Key(Native.VK_SHIFT, false);
                Sleep(careful ? RemoteGap : Uniform(0.015, 0.04));
            }
            Key(vk, false);
            Sleep(hold);
            KeyUp(vk, careful);
            if (shift)
            {
                Sleep(careful ? RemoteGap : Uniform(0.01, 0.03));
                KeyUp(Native.VK_SHIFT, careful);
            }
            lastKeyUp = Now();
        }

        void TapUnicode(char ch, double hold)
        {
            if (InRemoteMode)
            {
                KeyGap();
                hold = Math.Max(hold, RemoteHold);
            }
            Send(0, ch, Native.KEYEVENTF_UNICODE);
            Sleep(hold);
            Send(0, ch, Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP);
            lastKeyUp = Now();
        }

        // In Remote PC mode the key-up goes out twice. The second one does nothing on its own, but if
        // the first got lost on the way, it stops the other PC from auto-repeating the key ("angullllar").
        static void KeyUp(int vk, bool careful)
        {
            Key(vk, true);
            if (careful)
            {
                Sleep(0.012);
                Key(vk, true);
            }
        }

        // Remote PC mode: leave a clear gap after the previous key before pressing the next one.
        void KeyGap()
        {
            double wait = lastKeyUp + RemoteGap - Now();
            if (wait > 0)
                Sleep(wait);
        }

        static void Key(int vk, bool up)
        {
            Send((ushort)vk, (ushort)Native.MapVirtualKey((uint)vk, 0), up ? Native.KEYEVENTF_KEYUP : 0);
        }

        static void Send(ushort vk, ushort scan, uint flags)
        {
            var inputs = new Native.INPUT[1];
            inputs[0].type = Native.INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = vk;
            inputs[0].u.ki.wScan = scan;
            inputs[0].u.ki.dwFlags = flags;
            Native.SendInput(1, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        static void Sleep(double seconds) { Thread.Sleep((int)Math.Round(seconds * 1000)); }

        double Uniform(double a, double b) { return a + (b - a) * rng.NextDouble(); }
    }
}
