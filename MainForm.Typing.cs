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
            string text = BuildPlanText(source, from, length, skipIndent.Checked, out map);
            if (text.Trim().Length == 0)
            {
                status.Text = "Please enter some text first.";
                editor.Focus();
                return;
            }
            ClearHighlight();
            plan = new TypingPlan(text, speed.Value, (double)typo.Value / 100.0, null);
            typist = new Typist(Handle, shiftEnter.Checked, pauseOnClick.Checked);
            started = false;
            typingTime.Reset();
            progress.Value = 0;
            progress.BarColor = Theme.Accent;
            SaveSettings();

            IntPtr saved = remember.Checked && SavedTargetAlive() ? savedTarget : IntPtr.Zero;
            Launch(saved, saved != IntPtr.Zero ? "Switching to the saved box…  (Esc cancels)"
                                               : "Now click into the box where the text should go…  (Esc cancels)");
        }

        void ResumeTyping()
        {
            IntPtr target = typist.Target;
            if (target != IntPtr.Zero && !Native.IsWindow(target))
                target = IntPtr.Zero;
            Launch(target, target != IntPtr.Zero ? "Switching back…" : "Click into the box to continue typing…  (Esc cancels)");
        }

        void StopTyping()
        {
            if (worker != null)
                typist.Stop();  // the worker ends, and Poll() finishes up
            else if (phase == Phase.Paused)
            {
                SetPhase(Phase.Idle);
                pill.Set("Stopped", Theme.Muted);
                status.Text = "Stopped. Start Typing begins again from the start.";
            }
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
                message = "Typing…  Press Esc or switch windows to pause.";
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
            if (stop != null && stop.CanResume && started)
            {
                SetPhase(Phase.Paused);
                status.Text = stop.Message + "  Press Resume to go on from the marked letter.";
                return;
            }
            SetPhase(Phase.Idle);
            if (f == null)
            {
                pill.Set("Done", Theme.Green);
                progress.BarColor = Theme.Green;
                status.Text = "Done! All " + plan.Text.Length.ToString("N0") + " characters typed.";
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
            skipIndent.Enabled = idle;
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

        // The text to type from source[from..from+length), with line breaks as '\n' and, if asked,
        // without the spaces that start each line. map[k] is the source index of result[k].
        static string BuildPlanText(string source, int from, int length, bool skipIndent, out int[] map)
        {
            var sb = new StringBuilder(length);
            var index = new List<int>(length);
            bool lineStart = false;
            for (int i = from; i < from + length; i++)
            {
                char c = source[i];
                if (c == '\r')
                {
                    if (i + 1 < from + length && source[i + 1] == '\n')
                        continue;
                    c = '\n';
                }
                if (lineStart && skipIndent && (c == ' ' || c == '\t'))
                    continue;
                lineStart = c == '\n';
                sb.Append(c);
                index.Add(i);
            }
            map = index.ToArray();
            return sb.ToString();
        }

        // Shows text[0..pos) as typed and marks the next character, keeping it in view.
        void UpdateHighlight(int pos, bool force)
        {
            if (map == null || map.Length == 0)
                return;
            pos = Math.Min(pos, map.Length);
            int next = pos < map.Length ? map[pos] : -1;
            if (pos == highlighted && next == marked && !force)
                return;
            changingHighlight = true;
            Redraw(false);
            try
            {
                if (marked >= 0)
                    Mark(marked, 1, Theme.Card, Theme.Text);
                if (pos > highlighted)
                {
                    int a = map[highlighted], b = map[pos - 1] + 1;
                    Mark(a, b - a, Theme.TypedBack, Theme.TypedText);
                }
                highlighted = pos;
                marked = next;
                if (next >= 0)
                    Mark(next, 1, phase == Phase.Paused ? Theme.PausedBack : Theme.NextBack, Theme.Text);
                editor.Select(next >= 0 ? next : map[pos - 1] + 1, 0);
                editor.ScrollToCaret();
                highlightShown = true;
            }
            finally
            {
                Redraw(true);
                changingHighlight = false;
            }
        }

        void ClearHighlight()
        {
            if (!highlightShown)
                return;
            changingHighlight = true;
            int start = editor.SelectionStart, length = editor.SelectionLength;
            var scroll = new Native.POINT();
            Native.SendMessage(editor.Handle, Native.EM_GETSCROLLPOS, IntPtr.Zero, ref scroll);
            Redraw(false);
            try
            {
                Mark(0, editor.TextLength, Theme.Card, Theme.Text);
                editor.Select(start, length);
                Native.SendMessage(editor.Handle, Native.EM_SETSCROLLPOS, IntPtr.Zero, ref scroll);
            }
            finally
            {
                Redraw(true);
                changingHighlight = false;
            }
            editor.ClearUndo();  // so Undo never brings the highlight back
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
