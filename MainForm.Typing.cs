// Typing sessions: start, pause, resume and stop, plus the live highlight and stats.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading;

namespace AutoTyper
{
    enum Phase { Idle, Waiting, Typing, Paused }

    partial class MainForm
    {
        Phase phase = Phase.Idle;
        Typist typist;
        TypingPlan plan;
        Thread worker;
        int[] map;      // map[k] is the editor index of plan character k
        bool started;   // keys were sent in this session, so it can be paused and resumed
        readonly Stopwatch typingTime = new Stopwatch();
        volatile string message = "";
        volatile bool finished;
        volatile Exception failure;  // why the worker ended; null when all text was typed
        bool stopPressed;            // Stop was clicked while the worker ran: end, even if it paused first
        int selectionFrom, selectionLength;  // the selected part being typed (length 0: the whole text)

        // Highlight state.
        int highlighted;  // plan characters already shown as typed
        int marked = -1;  // editor index of the "next character" mark
        bool highlightShown, changingHighlight;

        void MainAction()
        {
            switch (phase)
            {
                case Phase.Idle: StartTyping(); break;
                case Phase.Typing: typist.Pause(); break;
                case Phase.Paused: ResumeTyping(); break;
            }
        }

        void StartTyping()
        {
            string source = editor.Text;
            int from = 0, length = source.Length;
            if (editor.SelectionLength > 0)  // type only the selected part
            {
                from = Math.Min(editor.SelectionStart, source.Length);
                length = Math.Min(editor.SelectionLength, source.Length - from);
            }
            string text = BuildPlanText(source, from, length, out map);
            if (text.Trim().Length == 0)
            {
                status.Text = "Please enter some text first.";
                editor.Focus();
                return;
            }
            selectionFrom = from;
            selectionLength = length < source.Length ? length : 0;
            ClearHighlight(true);
            plan = new TypingPlan(text, speed.Value, (double)typo.Value / 100.0, null, codeMode.Checked);
            typist = new Typist(Handle, shiftEnter.Checked, pauseOnClick.Checked) { Remote = remote.SelectedIndex };
            started = false;
            typingTime.Reset();
            progress.Value = 0;
            progress.BarColor = Theme.Accent;
            SaveSettings();

            // A remembered window that's hidden (closed to the tray) would be typed into invisibly.
            IntPtr saved = remember.Checked && SavedTargetAlive() && Native.IsShown(savedTarget) ? savedTarget : IntPtr.Zero;
            Launch(saved, saved != IntPtr.Zero ? "Switching to the saved box…  (Esc cancels)"
                                               : "Now click into the box where the text should go…  (Esc cancels)");
        }

        void ResumeTyping()
        {
            IntPtr target = typist.Target;
            if (!Native.IsShown(target))  // closed or hidden since the pause: pick the box again
                target = IntPtr.Zero;
            Launch(target, target != IntPtr.Zero ? "Switching back…" : "Click into the box to continue typing…  (Esc cancels)");
        }

        void StopTyping()
        {
            if (worker != null)
            {
                stopPressed = true;  // the click itself may pause the worker first
                typist.Stop();       // the worker ends, and Poll() finishes up
            }
            else if (phase == Phase.Paused)
            {
                SetPhase(Phase.Idle);
                pill.Set("Stopped", Theme.Muted);
                status.Text = "Stopped. Start Typing begins again from the start.";
                RestoreSelection();
            }
        }

        // A run of the selected part leaves that part selected again, so the next Start types it again.
        void RestoreSelection()
        {
            if (selectionLength > 0 && selectionFrom + selectionLength <= editor.TextLength)
                editor.Select(selectionFrom, selectionLength);
        }

        // Runs or continues the session on a worker thread. bringBack is a window to switch
        // to; without one, the user picks the box by clicking into it.
        void Launch(IntPtr bringBack, string waitMessage)
        {
            // This must happen here: Windows only lets the app the user just clicked switch windows.
            if (bringBack != IntPtr.Zero)
            {
                if (Native.IsIconic(bringBack))
                    Native.ShowWindow(bringBack, Native.SW_RESTORE);
                Native.SetForegroundWindow(bringBack);
            }
            message = waitMessage;
            failure = null;
            finished = false;
            stopPressed = false;
            typist.Rearm();
            Typist t = typist;
            TypingPlan p = plan;
            worker = new Thread(() => Work(t, p, bringBack)) { IsBackground = true };
            SetPhase(Phase.Waiting);
            worker.Start();
            Poll();
            pollTimer.Start();
        }

        void Work(Typist t, TypingPlan p, IntPtr bringBack)
        {
            try
            {
                if (bringBack == IntPtr.Zero || !t.UseTarget(bringBack))
                {
                    if (bringBack != IntPtr.Zero)
                        message = "Couldn't switch to that window. Click into the box instead…  (Esc cancels)";
                    t.WaitForClick();
                }
                message = t.InRemoteMode ? "Typing in Remote PC mode…  Press Esc to pause."
                                         : "Typing…  Press Esc or switch windows to pause.";
                t.Typing = true;
                t.Wait(0.3 + 0.4 * new Random().NextDouble());
                t.EraseDirty();
                t.Run(p.Actions(t.Progress));
            }
            catch (Exception e)
            {
                failure = e;
                message = e.Message;
            }
            finally
            {
                t.Typing = false;
                finished = true;
            }
        }

        void Poll()
        {
            bool done = finished;  // read first, so everything the worker did before is visible below
            if (typist.Typing)
            {
                started = true;
                if (!typingTime.IsRunning)
                    typingTime.Start();
                if (phase == Phase.Waiting)
                    SetPhase(Phase.Typing);
            }
            else
                typingTime.Stop();
            status.Text = message;
            UpdateHighlight(typist.Progress, false);
            UpdateStats();
            if (done)
                Finish();
        }

        void Finish()
        {
            pollTimer.Stop();
            typingTime.Stop();
            worker = null;
            if (remember.Checked && typist.Target != IntPtr.Zero)
                SaveTarget(typist.Target);
            Exception f = failure;
            var stop = f as Stopped;
            if (stop != null && stop.CanResume && started && !stopPressed)
            {
                SetPhase(Phase.Paused);
                status.Text = stop.Message + "  Press Resume to go on from the marked letter.";
                return;
            }
            SetPhase(Phase.Idle);
            RestoreSelection();
            if (f == null)
            {
                pill.Set("Done", Theme.Green);
                progress.BarColor = Theme.Green;
                // Symbols a remote window may have dropped (or turned into something else).
                int unsure = typist.UnsureSymbols;
                status.Text = unsure == 0 ? "Done! All " + plan.Text.Length.ToString("N0") + " characters typed."
                    : "Done. Check " + unsure + (unsure == 1 ? " symbol" : " symbols") + " on the other PC.";
            }
            else if (stop != null)
            {
                pill.Set(started ? "Stopped" : "Cancelled", Theme.Muted);
                status.Text = started ? "Stopped." : "Cancelled.";
            }
            else
            {
                pill.Set("Error", Theme.Red);
                status.Text = "Error: " + f.Message;
            }
        }

        void SetPhase(Phase p)
        {
            phase = p;
            bool idle = p == Phase.Idle;
            editor.ReadOnly = !idle;  // the saved text must not change until the session ends
            codeMode.Enabled = idle;  // the typing plan is built with it
            stopButton.Enabled = !idle;
            stopButton.Text = p == Phase.Waiting && !started ? "Cancel" : "Stop";
            mainButton.Enabled = p != Phase.Waiting;
            switch (p)
            {
                case Phase.Idle:
                    mainButton.Text = "Start Typing";
                    mainButton.Kind = ButtonKind.Primary;
                    pill.Set("Ready", Theme.Muted);
                    break;
                case Phase.Waiting:
                    mainButton.Text = "Waiting for the box…";
                    mainButton.Kind = ButtonKind.Primary;
                    pill.Set("Waiting", Theme.Accent);
                    break;
                case Phase.Typing:
                    mainButton.Text = "Pause";
                    mainButton.Kind = ButtonKind.Warning;
                    pill.Set("Typing", Theme.Green);
                    progress.BarColor = Theme.Accent;
                    break;
                case Phase.Paused:
                    mainButton.Text = "Resume";
                    mainButton.Kind = ButtonKind.Success;
                    pill.Set("Paused", Theme.Amber);
                    progress.BarColor = Theme.Amber;
                    break;
            }
            if (highlightShown)
                UpdateHighlight(typist.Progress, true);  // the mark's color follows the phase
            UpdateCounts();
        }

        void UpdateStats()
        {
            int total = plan.Text.Length, done = typist.Progress;
            progress.Value = (int)(1000L * done / total);
            double seconds = typingTime.Elapsed.TotalSeconds;
            double perSecond = speed.Value * 5 / 60.0;
            var sb = new StringBuilder(done.ToString("N0") + " / " + total.ToString("N0"));
            if (seconds > 3 && done > 10)
            {
                sb.Append("  ·  " + (done / 5.0 / (seconds / 60)).ToString("0") + " WPM");
                if (seconds > 8)
                    perSecond = done / seconds;
            }
            sb.Append(done < total ? "  ·  " + Duration((total - done) / perSecond) + " left" : "  ·  took " + Duration(seconds));
            stats.Text = sb.ToString();
        }

        // The text to type from source[from..from+length), with every kind of line break as '\n'
        // and no other control characters. map[k] is the source index of result[k].
        static string BuildPlanText(string source, int from, int length, out int[] map)
        {
            var sb = new StringBuilder(length);
            var index = new List<int>(length);
            for (int i = from; i < from + length; i++)
            {
                char c = source[i];
                if (c == '\r')
                {
                    if (i + 1 < from + length && source[i + 1] == '\n')
                        continue;
                    c = '\n';
                }
                else if (c == '\v' || c == '\f' || c == '\u0085' || c == (char)0x2028 || c == (char)0x2029)
                    c = '\n';  // line breaks from Word and other apps (Shift+Enter, page breaks)
                else if ((c < ' ' && c != '\t' && c != '\n') || c == '\u007F')
                    continue;  // other control characters would type nothing useful
                sb.Append(c);
                index.Add(i);
            }
            map = index.ToArray();
            return sb.ToString();
        }

        // Shows text[0..pos) as typed and marks the next character, keeping it in view.
        // Recoloring works by selecting text, and every selection scrolls the box to it. This runs
        // many times a second, so drawing stays off until the end (no in-between scrolls on screen),
        // the view goes back to where it was, and it only moves when the next letter is out of
        // sight. Otherwise the text visibly shakes up and down while typing.
        void UpdateHighlight(int pos, bool force)
        {
            if (map == null || map.Length == 0 || plan == null)
                return;
            pos = Math.Min(pos, map.Length);
            int markAt = MarkIndex(pos);
            if (pos == highlighted && markAt == marked && !force)
                return;
            changingHighlight = true;
            int before = FirstVisibleLine();
            int first = int.MaxValue, last = -1;  // changed characters
            Redraw(false);
            try
            {
                if (marked >= 0)
                {
                    bool typed = highlighted > 0 && marked <= map[highlighted - 1];
                    Mark(marked, 1, typed ? Theme.TypedBack : Theme.Card, typed ? Theme.TypedText : Theme.Text);
                    Span(ref first, ref last, marked, marked);
                }
                if (pos > highlighted)
                {
                    int a = map[highlighted], b = map[pos - 1] + 1;
                    Mark(a, b - a, Theme.TypedBack, Theme.TypedText);
                    Span(ref first, ref last, a, b - 1);
                }
                highlighted = pos;
                marked = markAt;
                if (markAt >= 0)
                {
                    Mark(markAt, 1, phase == Phase.Paused ? Theme.PausedBack : Theme.NextBack, Theme.Text);
                    Span(ref first, ref last, markAt, markAt);
                }
                int caret = markAt >= 0 ? markAt : map[pos - 1] + 1;
                editor.Select(caret, 0);
                ScrollToLine(before);
                if (!InView(caret))
                    editor.ScrollToCaret();
                highlightShown = true;
            }
            finally
            {
                Native.SendMessage(editor.Handle, Native.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                if (FirstVisibleLine() != before || last < 0)
                    editor.Invalidate();
                else
                    RepaintChars(first, last);
                changingHighlight = false;
            }
        }

        // Where the "next letter" mark goes: the next character, or, since a line break can't show
        // a color, the first character after a run of them. Past the end: no mark.
        int MarkIndex(int pos)
        {
            string text = plan.Text;
            for (int k = pos; k < text.Length && k < map.Length; k++)
                if (text[k] != '\n')
                    return map[k];
            return pos < map.Length && pos > 0 ? map[pos - 1] : -1;  // only line breaks left
        }

        static void Span(ref int first, ref int last, int from, int to)
        {
            first = Math.Min(first, from);
            last = Math.Max(last, to);
        }

        // Scrolling by lines rather than pixels: pixel positions stop working past 65,535 pixels.
        int FirstVisibleLine()
        {
            return Native.SendMessage(editor.Handle, Native.EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
        }

        void ScrollToLine(int line)
        {
            int now = FirstVisibleLine();
            if (now != line)
                Native.SendMessage(editor.Handle, Native.EM_LINESCROLL, IntPtr.Zero, (IntPtr)(line - now));
        }

        // Whether the whole line of this character is inside the visible part of the box.
        bool InView(int index)
        {
            int y = editor.GetPositionFromCharIndex(Math.Min(index, Math.Max(0, editor.TextLength - 1))).Y;
            return y >= 0 && y + editor.Font.Height <= editor.ClientSize.Height;
        }

        // Repaint just the lines from character `first` to `last`.
        void RepaintChars(int first, int last)
        {
            int top = editor.GetPositionFromCharIndex(first).Y;
            int bottom = editor.GetPositionFromCharIndex(Math.Min(last, Math.Max(0, editor.TextLength - 1))).Y + editor.Font.Height;
            editor.Invalidate(new Rectangle(0, top - 2, editor.ClientSize.Width, bottom - top + 4));
        }

        // clearUndo: drop the coloring steps from Undo, so Undo never brings the highlight back. Only
        // before an edit: right after one, it would also drop the edit itself.
        void ClearHighlight(bool clearUndo)
        {
            if (!highlightShown)
                return;
            changingHighlight = true;
            int start = editor.SelectionStart, length = editor.SelectionLength;
            int line = FirstVisibleLine();
            Redraw(false);
            try
            {
                Mark(0, editor.TextLength, Theme.Card, Theme.Text);
                editor.Select(start, length);
                ScrollToLine(line);
            }
            finally
            {
                Redraw(true);
                changingHighlight = false;
            }
            if (clearUndo)
                editor.ClearUndo();
            highlightShown = false;
            highlighted = 0;
            marked = -1;
        }

        void Mark(int start, int length, Color back, Color fore)
        {
            editor.Select(start, length);
            editor.SelectionBackColor = back;
            editor.SelectionColor = fore;
        }

        void Redraw(bool on)
        {
            Native.SendMessage(editor.Handle, Native.WM_SETREDRAW, on ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
            if (on)
                editor.Invalidate();
        }
    }
}
