@echo off
rem Builds dist\AutoTyper.exe with the C# compiler that ships with Windows (.NET Framework 4).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%~dp0dist" mkdir "%~dp0dist"
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0dist\AutoTyper.exe" "%~dp0AutoTyper.cs" "%~dp0TypingPlan.cs" "%~dp0Typist.cs" "%~dp0Controls.cs" "%~dp0MainForm.cs" "%~dp0MainForm.Typing.cs"
