// Look and feel: colors, the custom-drawn controls the window uses, and saved settings.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AutoTyper
{
    static class Theme
    {
        public static readonly Color Window = Color.FromArgb(243, 244, 246);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(220, 224, 230);
        public static readonly Color Text = Color.FromArgb(17, 24, 39);
        public static readonly Color Muted = Color.FromArgb(107, 114, 128);
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color Green = Color.FromArgb(22, 163, 74);
        public static readonly Color Amber = Color.FromArgb(217, 119, 6);
        public static readonly Color Red = Color.FromArgb(220, 38, 38);
        public static readonly Color Track = Color.FromArgb(229, 231, 235);
        // Text highlight while typing.
        public static readonly Color TypedBack = Color.FromArgb(220, 252, 231);
        public static readonly Color TypedText = Color.FromArgb(21, 128, 61);
        public static readonly Color NextBack = Color.FromArgb(253, 224, 71);
        public static readonly Color PausedBack = Color.FromArgb(253, 186, 116);

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // color mixed toward white; amount 0 = color, 1 = white.
        public static Color Tint(Color color, double amount)
        {
            return Color.FromArgb((int)(color.R + (255 - color.R) * amount), (int)(color.G + (255 - color.G) * amount),
                                  (int)(color.B + (255 - color.B) * amount));
        }

        public static Color Darken(Color color, double amount)
        {
            return Color.FromArgb((int)(color.R * (1 - amount)), (int)(color.G * (1 - amount)), (int)(color.B * (1 - amount)));
        }
    }

    // White rounded panel with a thin border.
    class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Window);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8))
            using (var fill = new SolidBrush(Theme.Card))
            using (var pen = new Pen(Theme.Border))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    enum ButtonKind { Primary, Success, Warning, Secondary }

    class FlatButton : Button
    {
        ButtonKind kind;
        bool hover, down;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public ButtonKind Kind
        {
            get { return kind; }
            set { kind = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill, text, border;
            if (kind == ButtonKind.Secondary)
            {
                fill = down ? Theme.Track : hover ? Color.FromArgb(249, 250, 251) : Theme.Card;
                text = Theme.Text;
                border = Theme.Border;
            }
            else
            {
                Color c = kind == ButtonKind.Success ? Theme.Green : kind == ButtonKind.Warning ? Theme.Amber : Theme.Accent;
                fill = down ? Theme.Darken(c, 0.25) : hover ? Theme.Darken(c, 0.12) : c;
                text = Color.White;
                border = fill;
            }
            if (!Enabled)
            {
                fill = kind == ButtonKind.Secondary ? Theme.Card : Theme.Tint(fill, 0.55);
                text = kind == ButtonKind.Secondary ? Theme.Tint(Theme.Muted, 0.4) : Color.White;
            }
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 7))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // Thin rounded progress bar. Value is 0..1000.
    class ProgressStrip : Control
    {
        int value;
        Color bar = Theme.Accent;

        public ProgressStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
        }

        public int Value
        {
            get { return value; }
            set { int v = Math.Max(0, Math.Min(1000, value)); if (v != this.value) { this.value = v; Invalidate(); } }
        }

        public Color BarColor
        {
            get { return bar; }
            set { if (value != bar) { bar = value; Invalidate(); } }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float r = Height / 2f;
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), r))
            using (var brush = new SolidBrush(Theme.Track))
                g.FillPath(brush, path);
            float w = (Width - 1) * value / 1000f;
            if (w >= 1)
                using (var path = Theme.Rounded(new RectangleF(0, 0, Math.Max(w, Height), Height - 1), r))
                using (var brush = new SolidBrush(bar))
                    g.FillPath(brush, path);
        }
    }

    // Small rounded status badge with a colored dot.
    class Pill : Control
    {
        Color color = Theme.Muted;

        public Pill()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
        }

        public void Set(string text, Color color)
        {
            this.color = color;
            Text = text;
            Size sz = TextRenderer.MeasureText(text, Font);
            Size = new Size(sz.Width + Font.Height + 14, Font.Height + 10);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f))
            using (var brush = new SolidBrush(Theme.Tint(color, 0.86)))
                g.FillPath(brush, path);
            float dot = Font.Height * 0.45f;
            using (var brush = new SolidBrush(color))
                g.FillEllipse(brush, Height / 2f - dot / 2 + 2, (Height - dot) / 2f, dot, dot);
            var rect = new Rectangle((int)(Height / 2f + dot), 0, Width - (int)(Height / 2f + dot), Height);
            TextRenderer.DrawText(g, Text, Font, rect, Theme.Darken(color, 0.2),
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.Left);
        }
    }

    // A label that repaints without flicker, for text that changes many times a second.
    class SteadyLabel : Label
    {
        public SteadyLabel()
        {
            DoubleBuffered = true;
        }
    }

    // Plain-text editor: shows a hint when empty and never pastes formatting.
    class TextEditor : RichTextBox
    {
        const int WM_PAINT = 0x000F;
        static readonly Font PlaceholderFont = new Font("Segoe UI", 10F);

        public string Placeholder = "";

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.V) || keyData == (Keys.Shift | Keys.Insert))
            {
                PastePlain();
                return true;
            }
            // RichEdit formatting shortcuts (alignment, line spacing) make no sense for plain text.
            Keys key = keyData & Keys.KeyCode;
            if ((keyData & Keys.Modifiers) == Keys.Control &&
                (key == Keys.L || key == Keys.E || key == Keys.R || key == Keys.J || key == Keys.D1 || key == Keys.D2 || key == Keys.D5))
                return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void PastePlain()
        {
            if (ReadOnly || !Clipboard.ContainsText())
                return;
            SelectedText = Clipboard.GetText().Replace("\r\n", "\n");
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT && TextLength == 0 && Placeholder.Length > 0)
                using (Graphics g = CreateGraphics())
                    TextRenderer.DrawText(g, Placeholder, PlaceholderFont, new Rectangle(2, 1, ClientSize.Width - 4, ClientSize.Height),
                                          Theme.Muted, BackColor, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    static class AppIcon
    {
        // The icon built into the exe (assets\Window.ico: 16-64 px, so the title bar and the
        // taskbar each get a sharp size). If it's missing, a simple one is drawn instead.
        public static Icon Create()
        {
            try
            {
                using (var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("Window.ico"))
                    if (stream != null)
                        return new Icon(stream);
            }
            catch (Exception) { }
            return Drawn();
        }

        static Icon Drawn()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (var path = Theme.Rounded(new RectangleF(1, 1, 30, 30), 7))
                    using (var brush = new SolidBrush(Theme.Accent))
                        g.FillPath(brush, path);
                    using (var font = new Font("Segoe UI", 15F, FontStyle.Bold, GraphicsUnit.Pixel))
                        g.DrawString("Aa", font, Brushes.White, 3, 6);
                    g.FillRectangle(Brushes.White, 26, 8, 2, 16);  // text cursor
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    // Options and the last text, kept between runs in %APPDATA%\AutoTyper.
    class SettingsStore
    {
        readonly string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoTyper");
        readonly Dictionary<string, string> values = new Dictionary<string, string>();

        public SettingsStore()
        {
            try
            {
                foreach (string line in File.ReadAllLines(Path.Combine(dir, "settings.txt")))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                        values[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
            }
            catch (Exception) { }  // first run, or unreadable: use defaults
        }

        public int Int(string key, int def, int min, int max)
        {
            string s;
            int v;
            return values.TryGetValue(key, out s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                ? Math.Min(Math.Max(v, min), max) : def;
        }

        public decimal Decimal(string key, decimal def, decimal min, decimal max)
        {
            string s;
            decimal v;
            return values.TryGetValue(key, out s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out v)
                ? Math.Min(Math.Max(v, min), max) : def;
        }

        public bool Bool(string key, bool def)
        {
            string s;
            return values.TryGetValue(key, out s) ? s == "1" : def;
        }

        public void Set(string key, object value)
        {
            values[key] = value is bool ? ((bool)value ? "1" : "0") : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public string LoadText()
        {
            try { return File.ReadAllText(Path.Combine(dir, "text.txt"), Encoding.UTF8); }
            catch (Exception) { return ""; }
        }

        public void Save(string text)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var lines = new List<string>();
                foreach (var kv in values)
                    lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(Path.Combine(dir, "settings.txt"), lines.ToArray());
                string textPath = Path.Combine(dir, "text.txt");
                if (text.Length > 0)
                    File.WriteAllText(textPath, text, Encoding.UTF8);
                else if (File.Exists(textPath))
                    File.Delete(textPath);
            }
            catch (Exception) { }  // settings are a convenience; never fail because of them
        }
    }
}
