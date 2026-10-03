// Auto Typer - types your text into any other window, like a human would.
//
// Paste text, click "Start Typing", then click into any input box in another
// program. Typing starts right after that click. Press Esc to stop.
//
// Build with build.bat. It uses the C# compiler that ships with Windows
// (.NET Framework 4), so nothing needs to be installed to build or to run.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Auto Typer")]
[assembly: AssemblyProduct("Auto Typer")]
[assembly: AssemblyDescription("Types text into any window like a human.")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace AutoTyper
{
    // -----------------------------------------------------------------------
    // Human-like typing plan (pure logic, no Windows calls)
    // -----------------------------------------------------------------------

    enum ActionKind { Wait, Key, Back, Pos }

    struct TypingAction
    {
        public ActionKind Kind;
        public char Char;
        public double Seconds;  // pause length, or how long the key is held
        public int Pos;         // text[0..Pos) is now typed correctly

        public static TypingAction Wait(double s) { return new TypingAction { Kind = ActionKind.Wait, Seconds = s }; }
        public static TypingAction Key(char c, double hold) { return new TypingAction { Kind = ActionKind.Key, Char = c, Seconds = hold }; }
        public static TypingAction Back(double hold) { return new TypingAction { Kind = ActionKind.Back, Seconds = hold }; }
        public static TypingAction At(int pos) { return new TypingAction { Kind = ActionKind.Pos, Pos = pos }; }
    }

    class TypingPlan
    {
        // Pauses and typo fixes make the real speed lower than the raw key speed;
        // this factor makes the measured speed land close to the WPM setting.
        public const double SpeedCalibration = 0.68;

        // Nearby keys on a QWERTY keyboard, used to make realistic typos.
        static readonly Dictionary<char, string> Neighbors = new Dictionary<char, string> {
            {'q', "wa"}, {'w', "qeas"}, {'e', "wrsd"}, {'r', "etdf"}, {'t', "ryfg"},
            {'y', "tugh"}, {'u', "yihj"}, {'i', "uojk"}, {'o', "ipkl"}, {'p', "ol"},
            {'a', "qwsz"}, {'s', "awedxz"}, {'d', "serfcx"}, {'f', "drtgvc"}, {'g', "ftyhbv"},
            {'h', "gyujnb"}, {'j', "huikmn"}, {'k', "jiolm"}, {'l', "kop"},
            {'z', "asx"}, {'x', "zsdc"}, {'c', "xdfv"}, {'v', "cfgb"}, {'b', "vghn"},
            {'n', "bhjm"}, {'m', "njk"},
        };

        // Very common letter pairs that fingers type faster.
        static readonly HashSet<string> FastBigrams = new HashSet<string>((
            "th he in er an re on at en nd ti es or te of ed is it al ar st to nt ng " +
            "se ha as ou io le ve co me de hi ri ro ic ne ea ra ce").Split(' '));

        const string ShiftedSymbols = "~!@#$%^&*()_+{}|:\"<>?";

        readonly string text;
        readonly double baseTime, pace, typoRate;
        readonly Random rng;
        double rhythm = 1.0;
        char prev = '\0';
        double lastHold = 0.0;

        public TypingPlan(string text, int wpm, double typoRate, Random rng)
        {
            this.text = text;
            double nominal = 60.0 / (wpm * 5);  // seconds per character
            baseTime = nominal * SpeedCalibration;
            pace = nominal / 0.12;  // faster typists pause less; 1.0 at 100 WPM
            this.typoRate = typoRate;
            this.rng = rng ?? new Random();
        }

        public IEnumerable<TypingAction> Actions()
        {
            int i = 0, noTypoAt = -1;
            double wait, hold;
            while (i < text.Length)
            {
                char ch = text[i];
                if (i != noTypoAt && Neighbors.ContainsKey(char.ToLowerInvariant(ch)) && rng.NextDouble() < typoRate)
                {
                    string typed = Mistake(i);
                    foreach (char c in typed)
                    {
                        NextKey(c, out wait, out hold);
                        yield return TypingAction.Wait(wait);
                        yield return TypingAction.Key(c, hold);
                    }
                    // Notice the mistake, then erase back to the last correct letter.
                    int keep = 0;
                    while (keep < typed.Length && i + keep < text.Length && typed[keep] == text[i + keep])
                        keep++;
                    yield return TypingAction.Wait(Uniform(0.2, 0.6) * pace);
                    for (int k = 0; k < typed.Length - keep; k++)
                    {
                        yield return TypingAction.Back(Uniform(0.03, 0.07));
                        yield return TypingAction.Wait(Uniform(0.05, 0.12) * pace);
                    }
                    yield return TypingAction.Wait(Uniform(0.1, 0.3) * pace);
                    prev = '\0';
                    lastHold = 0.0;
                    i += keep;
                    noTypoAt = i;
                    yield return TypingAction.At(i);
                    continue;
                }
                NextKey(ch, out wait, out hold);
                yield return TypingAction.Wait(wait);
                yield return TypingAction.Key(ch, hold);
                i++;
                yield return TypingAction.At(i);
            }
        }

        void NextKey(char ch, out double wait, out double hold)
        {
            hold = Math.Min(Uniform(0.035, 0.085), baseTime * 0.45);
            wait = Math.Max(Interval(ch) - lastHold, 0.005);
            prev = ch;
            lastHold = hold;
        }

        // Time from the previous key press to this one.
        double Interval(char ch)
        {
            // Slow drift so the speed is never constant (focus, warm-up, tiredness).
            rhythm += Gauss(0.03) + (1.0 - rhythm) * 0.05;
            rhythm = Clamp(rhythm, 0.75, 1.35);

            double t = baseTime * rhythm * Math.Exp(Gauss(0.3));
            if (prev != '\0' && FastBigrams.Contains(new string(new[] { char.ToLowerInvariant(prev), char.ToLowerInvariant(ch) })))
                t *= 0.75;
            else if (prev == ch)
                t *= 0.85;
            if (char.IsUpper(ch) || ShiftedSymbols.IndexOf(ch) >= 0)
                t += Uniform(0.03, 0.09);
            else if (!char.IsLetter(ch) && ch != ' ' && ch != '\n')
                t *= 1.3;
            t = Clamp(t, baseTime * 0.5, baseTime * 4);

            double pause = 0.0;
            if (prev == '.' || prev == '!' || prev == '?')
                pause = Uniform(0.25, 0.7);
            else if (prev == ',' || prev == ';' || prev == ':')
                pause = Uniform(0.08, 0.3);
            else if (prev == '\n')
                pause = Uniform(0.3, 0.9);
            else if (prev == ' ')
            {
                double r = rng.NextDouble();
                if (r < 0.015)
                    pause = Uniform(0.7, 2.0);  // thinking
                else if (r < 0.13)
                    pause = Uniform(0.05, 0.3);  // small hesitation
            }
            return t + pause * pace;
        }

        // What gets typed when a typo happens at text[i].
        string Mistake(int i)
        {
            char ch = text[i];
            char nxt = i + 1 < text.Length ? text[i + 1] : '\0';
            double r = rng.NextDouble();
            string typed;
            int j;
            if (r < 0.15 && char.IsLetter(nxt) && char.ToLowerInvariant(nxt) != char.ToLowerInvariant(ch))
            {
                typed = new string(new[] { nxt, ch });  // swapped letters: "teh"
                j = i + 2;
            }
            else if (r < 0.25)
            {
                typed = new string(ch, 2);  // key pressed twice
                j = i + 1;
            }
            else
            {
                string options = Neighbors[char.ToLowerInvariant(ch)];
                char wrong = options[rng.Next(options.Length)];
                typed = (char.IsUpper(ch) ? char.ToUpperInvariant(wrong) : wrong).ToString();  // nearby key
                j = i + 1;
            }
            // Often a letter or two more get typed before the mistake is noticed.
            double e = rng.NextDouble();
            int extra = e < 0.45 ? 0 : e < 0.80 ? 1 : 2;
            var sb = new StringBuilder(typed);
            for (int k = j; k < j + extra && k < text.Length && text[k] != '\n'; k++)
                sb.Append(text[k]);
            return sb.ToString();
        }

        double Uniform(double a, double b) { return a + (b - a) * rng.NextDouble(); }

        double Gauss(double sigma)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        static double Clamp(double v, double lo, double hi) { return Math.Min(Math.Max(v, lo), hi); }
    }

    // -----------------------------------------------------------------------
    // Windows keyboard input
    // -----------------------------------------------------------------------

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
        public const int VK_CAPITAL = 0x14;
        public const int VK_ESCAPE = 0x1B;
        public const int SM_SWAPBUTTON = 23;
        public const uint GA_ROOT = 2;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const int SW_RESTORE = 9;

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
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    }

    class Stopped : Exception
    {
        public Stopped(string message) : base(message) { }
    }

    // Sends a TypingPlan to the window the user clicks into.
    class Typist
    {
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        // Clicking the desktop or taskbar should not start typing there.
        static readonly HashSet<string> ShellClasses = new HashSet<string> { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

        readonly bool shiftEnter;
        readonly Random rng = new Random();
        volatile bool stopRequested;
        IntPtr target = IntPtr.Zero;
        IntPtr layout = IntPtr.Zero;
        public volatile int Progress;

        public Typist(bool shiftEnter) { this.shiftEnter = shiftEnter; }

        public void Stop() { stopRequested = true; }

        public IntPtr Target { get { return target; } }

        public void SetTarget(IntPtr hwnd)
        {
            uint pid;
            layout = Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(hwnd, out pid));
            target = hwnd;
        }

        static double Now() { return Clock.Elapsed.TotalSeconds; }

        static bool Pressed(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }

        void Check()
        {
            if (stopRequested)
                throw new Stopped("Stopped.");
            if (Pressed(Native.VK_ESCAPE))
                throw new Stopped("Stopped (Esc).");
            if (target != IntPtr.Zero)
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (fg != IntPtr.Zero && fg != target)
                    throw new Stopped("Stopped: you switched to another window.");
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

        // Block until the user clicks into another window, which becomes the target.
        public void WaitForClick(IntPtr ownWindow)
        {
            // GetAsyncKeyState reads physical buttons, and left-handed setups swap them.
            int button = Native.GetSystemMetrics(Native.SM_SWAPBUTTON) != 0 ? Native.VK_RBUTTON : Native.VK_LBUTTON;
            while (true)
            {
                while (!Pressed(button))
                    Wait(0.01);
                while (Pressed(button))
                    Wait(0.01);
                Wait(0.15);  // let the click activate the window
                IntPtr fg = Native.GetForegroundWindow();
                if (fg != IntPtr.Zero && Native.GetAncestor(fg, Native.GA_ROOT) != ownWindow && !ShellClasses.Contains(ClassName(fg)))
                {
                    SetTarget(fg);
                    return;
                }
            }
        }

        // Use a window that was just brought to the front for us (the remembered target box).
        public bool UseTarget(IntPtr hwnd)
        {
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
                        break;
                    case ActionKind.Key:
                        Check();
                        clock += a.Seconds;
                        Press(a.Char, a.Seconds);
                        break;
                    case ActionKind.Back:
                        Check();
                        clock += a.Seconds;
                        Tap(Native.VK_BACK, a.Seconds, false);
                        break;
                }
            }
        }

        void Press(char ch, double hold)
        {
            if (ch == '\n')
                Tap(Native.VK_RETURN, hold, shiftEnter);
            else if (ch == '\t')
                Tap(Native.VK_TAB, hold, false);
            else
            {
                short scan = Native.VkKeyScanEx(ch, layout);
                if (scan == -1 || (scan & 0x600) != 0)  // not on this keyboard layout, or needs Ctrl/Alt
                {
                    TapUnicode(ch, hold);
                    return;
                }
                bool shift = (scan & 0x100) != 0;
                if (char.IsLetter(ch) && (Native.GetKeyState(Native.VK_CAPITAL) & 1) != 0)
                    shift = !shift;
                Tap(scan & 0xFF, hold, shift);
            }
        }

        // Taps are never interrupted, so a key is never left held down.
        void Tap(int vk, double hold, bool shift)
        {
            if (shift)
            {
                Key(Native.VK_SHIFT, false);
                Sleep(Uniform(0.015, 0.04));
            }
            Key(vk, false);
            Sleep(hold);
            Key(vk, true);
            if (shift)
            {
                Sleep(Uniform(0.01, 0.03));
                Key(Native.VK_SHIFT, true);
            }
        }

        static void TapUnicode(char ch, double hold)
        {
            Send(0, ch, Native.KEYEVENTF_UNICODE);
            Sleep(hold);
            Send(0, ch, Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP);
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

    // -----------------------------------------------------------------------
    // Window
    // -----------------------------------------------------------------------

    class MainForm : Form
    {
        readonly TextBox textBox;
        readonly TrackBar speed;
        readonly Label speedValue;
        readonly NumericUpDown typo;
        readonly CheckBox shiftEnter, skipIndent, onTop, remember;
        readonly Label savedLabel;
        readonly LinkLabel forget;
        readonly Button startButton, stopButton;
        readonly ProgressBar progress;
        readonly Label status;
        readonly System.Windows.Forms.Timer pollTimer, topTimer;

        Typist typist;
        Thread worker;
        int total;
        IntPtr savedTarget = IntPtr.Zero;  // window of the remembered target box
        uint savedPid;
        volatile string message = "";
        volatile bool finished;

        public MainForm()
        {
            SuspendLayout();
            Text = "Auto Typer";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 500);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;

            var main = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1 };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            main.Controls.Add(new Label { Text = "Text to type:", AutoSize = true, Margin = new Padding(0, 0, 0, 4) });

            textBox = new TextBox
            {
                Multiline = true, AcceptsReturn = true, AcceptsTab = true, WordWrap = true, MaxLength = 0,
                ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 10F),
                Margin = new Padding(0, 0, 0, 10),
            };
            textBox.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.A)  // multiline TextBox has no Ctrl+A by default
                {
                    textBox.SelectAll();
                    e.SuppressKeyPress = true;
                }
            };
            main.Controls.Add(textBox);

            var opts = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            opts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            opts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            opts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            opts.Controls.Add(new Label { Text = "Speed (WPM):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            speed = new TrackBar { Minimum = 30, Maximum = 300, Value = 160, SmallChange = 5, LargeChange = 20,
                                   TickStyle = TickStyle.None, Dock = DockStyle.Fill };
            opts.Controls.Add(speed, 1, 0);
            speedValue = new Label { Text = "160", AutoSize = true, Anchor = AnchorStyles.Left, MinimumSize = new Size(32, 0) };
            opts.Controls.Add(speedValue, 2, 0);
            speed.ValueChanged += (s, e) => speedValue.Text = speed.Value.ToString();

            opts.Controls.Add(new Label { Text = "Typo chance (%):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            typo = new NumericUpDown { Minimum = 0, Maximum = 20, DecimalPlaces = 1, Increment = 0.5M, Value = 1, Width = 70,
                                       Anchor = AnchorStyles.Left };
            opts.Controls.Add(typo, 1, 1);
            main.Controls.Add(opts);

            shiftEnter = new CheckBox { Text = "New line = Shift+Enter (for chat apps where Enter sends)", AutoSize = true,
                                        Margin = new Padding(0, 8, 0, 0) };
            skipIndent = new CheckBox { Text = "Skip spaces at the start of lines (code editors with auto-indent)", AutoSize = true,
                                        Margin = new Padding(0, 2, 0, 0) };
            onTop = new CheckBox { Text = "Always on top", Checked = true, AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
            onTop.CheckedChanged += (s, e) => TopMost = onTop.Checked;
            main.Controls.Add(shiftEnter);
            main.Controls.Add(skipIndent);
            main.Controls.Add(onTop);

            var rememberRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
            remember = new CheckBox { Text = "Remember target box:", Checked = true, AutoSize = true, Margin = new Padding(0, 0, 2, 0) };
            remember.CheckedChanged += (s, e) => ForgetTarget();
            savedLabel = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 4, 6, 0) };
            forget = new LinkLabel { Text = "Forget", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            forget.LinkClicked += (s, e) => ForgetTarget();
            rememberRow.Controls.Add(remember);
            rememberRow.Controls.Add(savedLabel);
            rememberRow.Controls.Add(forget);
            main.Controls.Add(rememberRow);
            UpdateSavedLabel();

            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 6) };
            startButton = new Button { Text = "Start Typing", AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
            startButton.Click += (s, e) => StartTyping();
            stopButton = new Button { Text = "Stop", AutoSize = true, Enabled = false, Margin = new Padding(0) };
            stopButton.Click += (s, e) => { if (typist != null) typist.Stop(); };
            buttons.Controls.Add(startButton);
            buttons.Controls.Add(stopButton);
            main.Controls.Add(buttons);

            progress = new ProgressBar { Dock = DockStyle.Fill, Height = 18, Margin = new Padding(0) };
            main.Controls.Add(progress);
            status = new Label { Text = "Paste your text, click Start Typing, then click into any input box.", AutoSize = true,
                                 Margin = new Padding(0, 6, 0, 0) };
            main.Controls.Add(status);

            main.RowCount = main.Controls.Count;
            for (int r = 0; r < main.RowCount; r++)
                main.RowStyles.Add(r == 1 ? new RowStyle(SizeType.Percent, 100F) : new RowStyle(SizeType.AutoSize));
            Controls.Add(main);

            pollTimer = new System.Windows.Forms.Timer { Interval = 100 };
            pollTimer.Tick += (s, e) => Poll();
            // Re-assert "on top" every second so other on-top windows can't cover this one.
            topTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            topTimer.Tick += (s, e) =>
            {
                KeepOnTop();
                UpdateSavedLabel();  // follows title changes and closed windows
            };
            topTimer.Start();

            // Never shrink below the natural size, so the buttons stay visible.
            Load += (s, e) => MinimumSize = Size;
            FormClosing += (s, e) =>
            {
                if (typist != null)
                    typist.Stop();
                if (worker != null)
                    worker.Join(1000);  // let any held key be released
            };
            ResumeLayout(false);
            PerformLayout();
        }

        void KeepOnTop()
        {
            // NOACTIVATE: never steal focus from the window being typed into.
            if (onTop.Checked)
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        bool SavedTargetAlive()
        {
            if (savedTarget == IntPtr.Zero)
                return false;
            uint pid;
            if (Native.IsWindow(savedTarget) && Native.GetWindowThreadProcessId(savedTarget, out pid) != 0 && pid == savedPid)
                return true;
            savedTarget = IntPtr.Zero;  // the window was closed
            return false;
        }

        void SaveTarget(IntPtr hwnd)
        {
            savedTarget = hwnd;
            Native.GetWindowThreadProcessId(hwnd, out savedPid);
            UpdateSavedLabel();
        }

        void ForgetTarget()
        {
            savedTarget = IntPtr.Zero;
            UpdateSavedLabel();
        }

        void UpdateSavedLabel()
        {
            string text;
            if (!remember.Checked)
                text = "off";
            else if (!SavedTargetAlive())
                text = "none yet";
            else
            {
                var sb = new StringBuilder(256);
                Native.GetWindowText(savedTarget, sb, sb.Capacity);
                string title = sb.Length == 0 ? "(window without a title)" : sb.ToString();
                text = "\"" + (title.Length > 45 ? title.Substring(0, 44) + "…" : title) + "\"";
            }
            if (savedLabel.Text != text)
                savedLabel.Text = text;
            forget.Visible = savedTarget != IntPtr.Zero;
        }

        void StartTyping()
        {
            string text = textBox.Text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (skipIndent.Checked)
                text = Regex.Replace(text, "\n[ \t]+", "\n");
            if (text.Trim().Length == 0)
            {
                status.Text = "Please enter some text first.";
                return;
            }

            var plan = new TypingPlan(text, speed.Value, (double)typo.Value / 100.0, null);
            var t = new Typist(shiftEnter.Checked);
            typist = t;
            total = text.Length;
            finished = false;
            progress.Value = 0;
            startButton.Enabled = false;
            stopButton.Enabled = true;

            // Bring the remembered box's window back; it puts the cursor back in that box.
            // This must happen here: Windows only lets the app the user just clicked switch windows.
            IntPtr saved = IntPtr.Zero;
            if (remember.Checked && SavedTargetAlive())
            {
                saved = savedTarget;
                if (Native.IsIconic(saved))
                    Native.ShowWindow(saved, Native.SW_RESTORE);
                Native.SetForegroundWindow(saved);
                message = "Switching to the saved box...  (Esc to cancel)";
            }
            else
                message = "Now click into the input box where you want the text...  (Esc to cancel)";
            UpdateSavedLabel();

            IntPtr own = Handle;
            worker = new Thread(() => Work(t, own, saved, plan)) { IsBackground = true };
            worker.Start();
            Poll();
            pollTimer.Start();
        }

        void Work(Typist t, IntPtr own, IntPtr saved, TypingPlan plan)
        {
            try
            {
                if (saved == IntPtr.Zero || !t.UseTarget(saved))
                {
                    if (saved != IntPtr.Zero)
                        message = "Couldn't switch to the saved window. Click into the input box instead...  (Esc to cancel)";
                    t.WaitForClick(own);
                }
                message = "Typing...  (press Esc to stop)";
                t.Wait(0.3 + 0.4 * new Random().NextDouble());
                t.Run(plan.Actions());
                message = "Done!";
            }
            catch (Stopped e)
            {
                message = e.Message;
            }
            catch (Exception e)
            {
                message = "Error: " + e.Message;
            }
            finally
            {
                finished = true;
            }
        }

        void Poll()
        {
            bool done = finished;
            status.Text = message;
            progress.Value = Math.Min(100, (int)(100L * typist.Progress / total));
            if (done)
            {
                pollTimer.Stop();
                startButton.Enabled = true;
                stopButton.Enabled = false;
                if (remember.Checked && typist.Target != IntPtr.Zero)
                    SaveTarget(typist.Target);
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            try { Native.SetProcessDPIAware(); } catch (Exception) { }
            try { Native.timeBeginPeriod(1); } catch (Exception) { }  // precise sleeps for natural key timing
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.ToString(), "Auto Typer error");
            Application.Run(new MainForm());
        }
    }
}
