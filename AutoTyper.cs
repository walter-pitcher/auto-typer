// Auto Typer - types your text into any other window, like a human would.
//
// Paste text, click "Start Typing", then click into any input box in another
// program. Typing starts right after that click. Typed text is highlighted as it
// goes. Esc, switching windows or clicking pauses; Resume continues from the same
// letter.
//
// Files:
//   TypingPlan.cs        what to type and when (human-like timing and typos)
//   Typist.cs            sending keys to the target window, pausing
//   MainForm.cs          the window layout and options
//   MainForm.Typing.cs   typing sessions, highlight and stats
//   Controls.cs          colors, custom controls and saved settings
//
// Build with build.bat. It uses the C# compiler that ships with Windows
// (.NET Framework 4), so nothing needs to be installed to build or to run.

using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Auto Typer")]
[assembly: AssemblyProduct("Auto Typer")]
[assembly: AssemblyDescription("Types text into any window like a human.")]
[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]

namespace AutoTyper
{
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
