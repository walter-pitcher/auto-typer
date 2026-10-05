// The main window: layout, options and saved settings. Typing sessions are in MainForm.Typing.cs.

using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AutoTyper
{
    partial class MainForm : Form
    {
        readonly SettingsStore settings = new SettingsStore();
        readonly TextEditor editor;
        readonly Label counts;
        readonly TrackBar speed;
        readonly Label speedValue;
        readonly NumericUpDown typo;
        readonly ComboBox remote;
        readonly CheckBox shiftEnter, skipIndent, pauseOnClick, onTop, keepText, remember;
        readonly Label savedLabel;
        readonly LinkLabel forget;
        readonly FlatButton mainButton, stopButton;
        readonly ProgressStrip progress;
        readonly Pill pill;
        readonly Label status, stats;
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 15000 };
        readonly System.Windows.Forms.Timer pollTimer, topTimer;

        IntPtr savedTarget = IntPtr.Zero;  // window of the remembered target box
        uint savedPid;

        public MainForm()
        {
            SuspendLayout();
            Text = "Auto Typer";
            Icon = AppIcon.Create();
            Font = new Font("Segoe UI", 9.75F);
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 720);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            var main = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 16, 16), ColumnCount = 1 };
            main.ColumnStyles.Add(Fill());

            // Header: name and current state.
            var header = Grid(Theme.Window, Fill(), Auto());
            header.Margin = new Padding(0, 0, 0, 12);
            header.Controls.Add(new Label { Text = "Auto Typer", AutoSize = true, Font = new Font("Segoe UI Semibold", 16F), Margin = new Padding(0) }, 0, 0);
            header.Controls.Add(new Label { Text = "Types your text into any window, the way a person would.", AutoSize = true,
                                            ForeColor = Theme.Muted, Margin = new Padding(2, 0, 0, 0) }, 0, 1);
            pill = new Pill { Anchor = AnchorStyles.Right, Margin = new Padding(0, 4, 0, 0), Font = new Font("Segoe UI Semibold", 9F) };
            header.Controls.Add(pill, 1, 0);
            header.SetRowSpan(pill, 2);
            main.Controls.Add(header);

            // The text, with typed characters highlighted while typing.
            var textCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 4, 8), Margin = new Padding(0, 0, 0, 12) };
            var textGrid = Grid(Theme.Card, Fill());
            textGrid.AutoSize = false;
            var textHead = Grid(Theme.Card, Auto(), Fill(), Auto(), Auto());
            textHead.Margin = new Padding(0, 0, 10, 6);
            textHead.Controls.Add(new Label { Text = "Text to type", AutoSize = true, Font = new Font("Segoe UI Semibold", 10F), Margin = new Padding(0) }, 0, 0);
            var paste = Link("Paste");
            paste.LinkClicked += (s, e) => { editor.Focus(); editor.PastePlain(); };
            var clear = Link("Clear");
            clear.LinkClicked += (s, e) =>
            {
                if (editor.ReadOnly)
                    return;
                editor.Focus();
                editor.SelectAll();
                editor.SelectedText = "";  // unlike Clear(), this can be undone
            };
            textHead.Controls.Add(paste, 2, 0);
            textHead.Controls.Add(clear, 3, 0);

            editor = new TextEditor
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text,
                Font = new Font("Consolas", 10.5F), DetectUrls = false, HideSelection = false, AcceptsTab = true,
                WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical, Margin = new Padding(0),
                Placeholder = "Paste or type the text you want typed.\n\nTip: select part of the text to type only that part.",
            };
            editor.ContextMenuStrip = EditorMenu();

            var textFoot = Grid(Theme.Card, Fill(), Auto());
            textFoot.Margin = new Padding(0, 6, 10, 0);
            counts = new Label { AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            keepText = Check("Keep text after closing", "keepText", true, "Saves the text on this PC so it is still here next time.");
            keepText.Margin = new Padding(0);
            textFoot.Controls.Add(counts, 0, 0);
            textFoot.Controls.Add(keepText, 1, 0);

            textGrid.Controls.Add(textHead, 0, 0);
            textGrid.Controls.Add(editor, 0, 1);
            textGrid.Controls.Add(textFoot, 0, 2);
            textGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            textGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            textGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            textCard.Controls.Add(textGrid);
            main.Controls.Add(textCard);

            // Options.
            var optCard = new Card { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                     Padding = new Padding(14, 10, 14, 10), Margin = new Padding(0, 0, 0, 14) };
            var opts = Grid(Theme.Card, Auto(), Fill(), Auto());
            opts.Controls.Add(Caption("Speed"), 0, 0);
            speed = new TrackBar { Minimum = 30, Maximum = 300, SmallChange = 5, LargeChange = 20, TickStyle = TickStyle.None,
                                   AutoSize = false, Height = 30, Dock = DockStyle.Fill, BackColor = Theme.Card, Margin = new Padding(8, 0, 8, 0) };
            speed.Value = settings.Int("speed", 160, speed.Minimum, speed.Maximum);
            tips.SetToolTip(speed, "Words per minute. Most people type 40-80; pauses and fixed typos are included.");
            opts.Controls.Add(speed, 1, 0);
            speedValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left, MinimumSize = new Size(140, 0), Margin = new Padding(0),
                                     Font = new Font("Segoe UI Semibold", 9.75F) };
            opts.Controls.Add(speedValue, 2, 0);

            opts.Controls.Add(Caption("Typos"), 0, 1);
            var typoRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(8, 4, 0, 4), BackColor = Theme.Card };
            typo = new NumericUpDown { Minimum = 0, Maximum = 20, DecimalPlaces = 1, Increment = 0.5M, Width = 64, Margin = new Padding(0) };
            typo.Value = settings.Decimal("typo", 1M, typo.Minimum, typo.Maximum);
            typoRow.Controls.Add(typo);
            typoRow.Controls.Add(new Label { Text = "% of letters get a typo, which is then fixed", AutoSize = true,
                                             ForeColor = Theme.Muted, Margin = new Padding(6, 4, 0, 0) });
            opts.Controls.Add(typoRow, 1, 1);
            opts.SetColumnSpan(typoRow, 2);

            opts.Controls.Add(Caption("Remote PC"), 0, 2);
            var remoteRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(8, 0, 0, 4), BackColor = Theme.Card };
            remote = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(0) };
            remote.Items.AddRange(new object[] { "Automatic", "Always on", "Off" });  // order matches RemoteMode
            remote.SelectedIndex = settings.Int("remote", 0, 0, remote.Items.Count - 1);
            tips.SetToolTip(remote, "Remote desktop apps (Parsec, AnyDesk, Remote Desktop, ...) can drop symbols like ², — or é,\n" +
                                    "and repeat a letter when keys come too fast. Remote PC mode types symbols as Alt codes\n" +
                                    "and presses each key a little more deliberately. Automatic turns it on whenever you\n" +
                                    "type into a remote desktop or virtual machine window.");
            remoteRow.Controls.Add(remote);
            remoteRow.Controls.Add(new Label { Text = "safer typing for Parsec, AnyDesk, Remote Desktop…", AutoSize = true,
                                               ForeColor = Theme.Muted, Margin = new Padding(6, 4, 0, 0) });
            opts.Controls.Add(remoteRow, 1, 2);
            opts.SetColumnSpan(remoteRow, 2);

            var checks = Grid(Theme.Card, new ColumnStyle(SizeType.Percent, 50F), new ColumnStyle(SizeType.Percent, 50F));
            checks.Margin = new Padding(0, 6, 0, 0);
            shiftEnter = Check("Shift+Enter for new lines", "shiftEnter", false, "For chat apps where Enter sends the message.");
            pauseOnClick = Check("Pause when I click", "pauseOnClick", true,
                                 "A click can move the cursor, so typing pauses until you press Resume.");
            skipIndent = Check("Skip spaces at line starts", "skipIndent", false, "For code editors that indent new lines by themselves.");
            onTop = Check("Always on top", "onTop", true, null);
            remember = Check("Remember target:", "remember", true,
                             "Next time, switch straight back to the same window instead of waiting for a click.");
            remember.Margin = new Padding(0, 3, 2, 3);
            savedLabel = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 6, 0) };
            forget = Link("Forget");
            forget.Margin = new Padding(0, 6, 0, 0);
            var rememberRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Theme.Card };
            rememberRow.Controls.AddRange(new Control[] { remember, savedLabel, forget });
            checks.Controls.Add(shiftEnter, 0, 0);
            checks.Controls.Add(pauseOnClick, 1, 0);
            checks.Controls.Add(skipIndent, 0, 1);
            checks.Controls.Add(onTop, 1, 1);
            checks.Controls.Add(rememberRow, 0, 2);
            checks.SetColumnSpan(rememberRow, 2);
            opts.Controls.Add(checks, 0, 3);
            opts.SetColumnSpan(checks, 3);
            optCard.Controls.Add(opts);
            main.Controls.Add(optCard);

            // Progress, status and buttons.
            progress = new ProgressStrip { Dock = DockStyle.Fill, Height = 8, Margin = new Padding(0, 0, 0, 8) };
            main.Controls.Add(progress);

            var statusRow = Grid(Theme.Window, Fill(), Auto());
            statusRow.Margin = new Padding(0, 0, 0, 12);
            status = new SteadyLabel { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Height = 22,
                                       TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0) };
            // Fixed width: the numbers change many times a second, and a growing or shrinking
            // label would keep shifting the layout next to it.
            stats = new SteadyLabel { AutoSize = false, Size = new Size(250, 22), ForeColor = Theme.Muted,
                                      TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(8, 0, 0, 0) };
            statusRow.Controls.Add(status, 0, 0);
            statusRow.Controls.Add(stats, 1, 0);
            main.Controls.Add(statusRow);

            var buttons = Grid(Theme.Window, Auto(), Auto(), Fill());
            mainButton = new FlatButton { Size = new Size(200, 42), Font = new Font("Segoe UI Semibold", 10.5F), Margin = new Padding(0, 0, 8, 0) };
            mainButton.Click += (s, e) => MainAction();
            stopButton = new FlatButton { Kind = ButtonKind.Secondary, Text = "Stop", Size = new Size(100, 42), Margin = new Padding(0) };
            stopButton.Click += (s, e) => StopTyping();
            buttons.Controls.Add(mainButton, 0, 0);
            buttons.Controls.Add(stopButton, 1, 0);
            buttons.Controls.Add(new Label { Text = "Esc pauses  ·  Ctrl+Enter starts", AutoSize = true, ForeColor = Theme.Muted,
                                             Anchor = AnchorStyles.Right, Margin = new Padding(0) }, 2, 0);
            main.Controls.Add(buttons);

            main.RowCount = main.Controls.Count;
            for (int r = 0; r < main.RowCount; r++)
                main.RowStyles.Add(r == 1 ? new RowStyle(SizeType.Percent, 100F) : new RowStyle(SizeType.AutoSize));
            Controls.Add(main);

            pollTimer = new System.Windows.Forms.Timer { Interval = 50 };
            pollTimer.Tick += (s, e) => Poll();
            // Re-assert "on top" every second so other on-top windows can't cover this one.
            topTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            topTimer.Tick += (s, e) =>
            {
                KeepOnTop();
                UpdateSavedLabel();  // follows title changes and closed windows
            };
            topTimer.Start();

            if (keepText.Checked)
                editor.Text = settings.LoadText();
            TopMost = onTop.Checked;

            // Options apply right away, even in the middle of typing.
            speed.ValueChanged += (s, e) =>
            {
                if (plan != null)
                    plan.Wpm = speed.Value;
                UpdateSpeedLabel();
                UpdateCounts();
            };
            typo.ValueChanged += (s, e) => { if (plan != null) plan.TypoRate = (double)typo.Value / 100.0; };
            remote.SelectedIndexChanged += (s, e) => { if (typist != null) typist.Remote = remote.SelectedIndex; };
            shiftEnter.CheckedChanged += (s, e) => { if (typist != null) typist.ShiftEnter = shiftEnter.Checked; };
            pauseOnClick.CheckedChanged += (s, e) => { if (typist != null) typist.PauseOnClick = pauseOnClick.Checked; };
            onTop.CheckedChanged += (s, e) => TopMost = onTop.Checked;
            remember.CheckedChanged += (s, e) => ForgetTarget();
            forget.LinkClicked += (s, e) => ForgetTarget();
            editor.TextChanged += (s, e) =>
            {
                if (phase == Phase.Idle && !changingHighlight)
                    ClearHighlight();
                UpdateCounts();
            };
            editor.SelectionChanged += (s, e) => UpdateCounts();
            // Going back to editing removes the highlight of the last run.
            editor.Enter += (s, e) => { if (phase == Phase.Idle) ClearHighlight(); };
            editor.MouseDown += (s, e) => { if (phase == Phase.Idle) ClearHighlight(); };
            KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    MainAction();
                }
            };

            Load += (s, e) =>
            {
                MinimumSize = new Size(Width, Height * 4 / 5);
                SetPhase(Phase.Idle);
                status.Text = "Paste your text, press Start Typing, then click into the box where it should go.";
                UpdateSpeedLabel();
                UpdateCounts();
                UpdateSavedLabel();
            };
            FormClosing += (s, e) =>
            {
                if (typist != null)
                    typist.Stop();
                if (worker != null)
                    worker.Join(1000);  // let any held key be released
                SaveSettings();
            };
            ResumeLayout(false);
            PerformLayout();
        }

        static ColumnStyle Auto() { return new ColumnStyle(SizeType.AutoSize); }
        static ColumnStyle Fill() { return new ColumnStyle(SizeType.Percent, 100F); }

        static TableLayoutPanel Grid(Color back, params ColumnStyle[] columns)
        {
            var grid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0),
                                              BackColor = back, ColumnCount = columns.Length };
            foreach (ColumnStyle c in columns)
                grid.ColumnStyles.Add(c);
            return grid;
        }

        static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font("Segoe UI Semibold", 9.75F), Margin = new Padding(0) };
        }

        static LinkLabel Link(string text)
        {
            return new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(8, 2, 0, 0), LinkBehavior = LinkBehavior.HoverUnderline,
                                   LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent };
        }

        CheckBox Check(string text, string key, bool def, string tip)
        {
            var box = new CheckBox { Text = text, AutoSize = true, Checked = settings.Bool(key, def), Margin = new Padding(0, 3, 12, 3), Tag = key };
            if (tip != null)
                tips.SetToolTip(box, tip);
            return box;
        }

        ContextMenuStrip EditorMenu()
        {
            var menu = new ContextMenuStrip();
            var undo = new ToolStripMenuItem("Undo", null, (s, e) => editor.Undo());
            var cut = new ToolStripMenuItem("Cut", null, (s, e) => editor.Cut());
            var copy = new ToolStripMenuItem("Copy", null, (s, e) => editor.Copy());
            var paste = new ToolStripMenuItem("Paste", null, (s, e) => editor.PastePlain());
            var delete = new ToolStripMenuItem("Delete", null, (s, e) => editor.SelectedText = "");
            var all = new ToolStripMenuItem("Select All", null, (s, e) => editor.SelectAll());
            menu.Items.AddRange(new ToolStripItem[] { undo, new ToolStripSeparator(), cut, copy, paste, delete, new ToolStripSeparator(), all });
            menu.Opening += (s, e) =>
            {
                bool editable = !editor.ReadOnly, selected = editor.SelectionLength > 0;
                undo.Enabled = editable && editor.CanUndo;
                cut.Enabled = delete.Enabled = editable && selected;
                copy.Enabled = selected;
                paste.Enabled = editable && Clipboard.ContainsText();
            };
            return menu;
        }

        void SaveSettings()
        {
            settings.Set("speed", speed.Value);
            settings.Set("typo", typo.Value);
            settings.Set("remote", remote.SelectedIndex);
            foreach (CheckBox box in new[] { shiftEnter, skipIndent, pauseOnClick, onTop, keepText, remember })
                settings.Set((string)box.Tag, box.Checked);
            settings.Save(keepText.Checked ? editor.Text : "");
        }

        void UpdateSpeedLabel()
        {
            int w = speed.Value;
            string name = w < 40 ? "Relaxed" : w < 70 ? "Average" : w < 100 ? "Skilled" : w < 140 ? "Fast" : w < 200 ? "Very fast" : "Extreme";
            speedValue.Text = w + " WPM  ·  " + name;
        }

        // Size and duration of the text (or the selected part) before typing starts.
        void UpdateCounts()
        {
            if (phase != Phase.Idle || changingHighlight)
                return;
            bool selection = editor.SelectionLength > 0;
            string s = selection ? editor.SelectedText : editor.Text;
            if (s.Trim().Length == 0)
            {
                counts.Text = "";
                return;
            }
            int words = 0;
            for (int i = 0; i < s.Length; i++)
                if (!char.IsWhiteSpace(s[i]) && (i == 0 || char.IsWhiteSpace(s[i - 1])))
                    words++;
            counts.Text = (selection ? "Selected: " : "") + s.Length.ToString("N0") + " characters  ·  " + words.ToString("N0") +
                          " words  ·  about " + Duration(s.Length / (speed.Value * 5 / 60.0));
        }

        static string Duration(double seconds)
        {
            int s = (int)Math.Round(seconds);
            return s >= 3600 ? string.Format("{0}:{1:00}:{2:00}", s / 3600, s / 60 % 60, s % 60)
                             : string.Format("{0}:{1:00}", s / 60, s % 60);
        }

        void KeepOnTop()
        {
            // Only when something actually covers this window: re-raising it every time would keep
            // swapping places with other always-on-top windows, and both would flicker.
            // NOACTIVATE: never steal focus from the window being typed into.
            if (onTop.Checked && IsCovered())
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // Whether a visible window above this one overlaps it.
        bool IsCovered()
        {
            Rectangle mine = Bounds;
            for (IntPtr h = Native.GetWindow(Handle, Native.GW_HWNDPREV); h != IntPtr.Zero; h = Native.GetWindow(h, Native.GW_HWNDPREV))
            {
                Native.RECT r;
                if (Native.IsWindowVisible(h) && !Native.IsIconic(h) && Native.GetWindowRect(h, out r) &&
                    mine.IntersectsWith(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)))
                    return true;
            }
            return false;
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
                text = "\"" + (title.Length > 50 ? title.Substring(0, 49) + "…" : title) + "\"";
            }
            if (savedLabel.Text != text)
                savedLabel.Text = text;
            forget.Visible = savedTarget != IntPtr.Zero;
        }
    }
}
