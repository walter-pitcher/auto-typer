// The human-like typing plan: which keys to press and when. Pure logic, no Windows calls.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AutoTyper
{
    enum ActionKind { Wait, Key, Back, Pos, Select }

    struct TypingAction
    {
        public ActionKind Kind;
        public char Char;       // Key: the character; Select: 'S' = Shift+Home, 'E' = Shift+End
        public char Low;        // Key: second half of a character like an emoji (a surrogate pair), or '\0'
        public double Seconds;  // pause length, or how long the key is held
        public int Pos;         // text[0..Pos) is now typed correctly

        public static TypingAction Wait(double s) { return new TypingAction { Kind = ActionKind.Wait, Seconds = s }; }
        public static TypingAction Key(char c, double hold) { return new TypingAction { Kind = ActionKind.Key, Char = c, Seconds = hold }; }
        public static TypingAction Key(char c, char low, double hold)
        {
            return new TypingAction { Kind = ActionKind.Key, Char = c, Low = low, Seconds = hold };
        }
        public static TypingAction Back(double hold) { return new TypingAction { Kind = ActionKind.Back, Seconds = hold }; }
        public static TypingAction At(int pos) { return new TypingAction { Kind = ActionKind.Pos, Pos = pos }; }
        public static TypingAction Select(bool toLineStart, double hold)
        {
            return new TypingAction { Kind = ActionKind.Select, Char = toLineStart ? 'S' : 'E', Seconds = hold };
        }
    }

    class TypingPlan
    {
        // Pauses make the real speed lower than the raw key speed; this factor makes
        // the measured speed land close to the WPM setting.
        public const double SpeedCalibration = 0.85;
        // Each 1% of typos costs about 8.5% more time to fix them; keys go that much
        // faster so the WPM setting stays true (up to a point, to stay human).
        const double TypoCost = 8.5, MaxTypoSpeedup = 1.6;

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

        // The finger that types each key on a QWERTY keyboard: 0-3 are the left hand
        // (pinky to index), 4-7 the right hand (index to pinky).
        static readonly string[] Fingers = { "`1qaz", "2wsx", "3edc", "45rtfgvb", "67yuhjnm", "8ik,", "9ol.", "0p;/-=[]'\\" };

        const string ShiftedSymbols = "~!@#$%^&*()_+{}|:\"<>?";

        // Code editors often close these by themselves: type "{" and a "}" appears after the cursor.
        const string Openers = "{[(>";

        readonly string text;
        readonly bool code;
        readonly Random rng;
        volatile int wpm, typoPermille;
        double rhythm = 1.0;
        char prev = '\0';
        double lastHold = 0.0;

        // code: Code editor mode, for editors that indent new lines and close brackets on their own.
        public TypingPlan(string text, int wpm, double typoRate, Random rng, bool code = false)
        {
            this.text = text;
            this.code = code;
            Wpm = wpm;
            TypoRate = typoRate;
            this.rng = rng ?? new Random();
        }

        public string Text { get { return text; } }

        // Both can be changed from another thread while typing; the next key uses the new value.
        public int Wpm { get { return wpm; } set { wpm = Math.Max(10, value); } }
        public double TypoRate
        {
            get { return typoPermille / 1000.0; }
            set { typoPermille = (int)Math.Round(Math.Max(0.0, value) * 1000); }
        }

        // Seconds per character.
        double Nominal { get { return 60.0 / (wpm * 5) / Math.Min(1 + TypoCost * TypoRate, MaxTypoSpeedup); } }
        double BaseTime { get { return Nominal * SpeedCalibration; } }
        double Pace { get { return Nominal / 0.12; } }  // faster typists pause less; 1.0 at 100 WPM

        // The actions that type text[start..]. Starting again after a pause warms up again.
        public IEnumerable<TypingAction> Actions(int start)
        {
            int i = start, noTypoAt = -1;
            double wait, hold;
            rhythm = 1.2;  // a little slow at first, like anyone who just started typing
            prev = '\0';
            lastHold = 0.0;
            // Picking up at the start of a line after a pause: the editor's own indent may still be there.
            if (code && i > 0 && i < text.Length && text[i - 1] == '\n')
                foreach (TypingAction a in ReplaceAutoIndent(i))
                    yield return a;
            while (i < text.Length)
            {
                char ch = text[i];
                if (code && ch == '\n')
                {
                    foreach (TypingAction a in CodeNewLine(i))
                        yield return a;
                    i++;
                    continue;
                }
                int word = WordStartingAt(i);
                if (i != noTypoAt && Neighbors.ContainsKey(char.ToLowerInvariant(ch)) && rng.NextDouble() < TypoRate)
                {
                    string typed = Mistake(i);
                    for (int k = 0; k < typed.Length; k++)
                    {
                        NextKey(typed[k], k == 0 ? word : 0, out wait, out hold);
                        yield return TypingAction.Wait(wait);
                        yield return TypingAction.Key(typed[k], hold);
                    }
                    // Notice the mistake, then erase back to the last correct letter.
                    int keep = 0;
                    while (keep < typed.Length && i + keep < text.Length && typed[keep] == text[i + keep])
                        keep++;
                    yield return TypingAction.Wait(Uniform(0.2, 0.6) * Pace);
                    for (int k = 0; k < typed.Length - keep; k++)
                    {
                        yield return TypingAction.Back(Uniform(0.03, 0.07));
                        yield return TypingAction.Wait(Uniform(0.05, 0.12) * Pace);
                    }
                    yield return TypingAction.Wait(Uniform(0.1, 0.3) * Pace);
                    prev = '\0';
                    lastHold = 0.0;
                    rhythm = Math.Max(rhythm, 1.15);  // a bit more careful right after a slip
                    i += keep;
                    noTypoAt = i;
                    yield return TypingAction.At(i);
                    continue;
                }
                NextKey(ch, word, out wait, out hold);
                yield return TypingAction.Wait(wait);
                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    // One character in two halves (an emoji): one key, no pause point in between.
                    yield return TypingAction.Key(ch, text[i + 1], hold);
                    i += 2;
                }
                else
                {
                    yield return TypingAction.Key(ch, hold);
                    i++;
                }
                yield return TypingAction.At(i);
            }
        }

        // Code editor mode: a new line, typed so the editor's helpers don't get in the way.
        IEnumerable<TypingAction> CodeNewLine(int i)
        {
            double wait, hold;
            char last = i > 0 ? text[i - 1] : '\n';
            int k = i - 1;
            while (k >= 0 && (text[k] == ' ' || text[k] == '\t'))
                k--;
            if (k >= 0 && Openers.IndexOf(text[k]) >= 0)  // "{" or "{ " at the end of the line
            {
                // The editor may have closed the bracket after the cursor ("{|}"). Select whatever it
                // put there, so Enter replaces it; the text brings its own closing bracket later.
                yield return TypingAction.Wait(Uniform(0.08, 0.2) * Pace);
                yield return TypingAction.Select(false, Uniform(0.04, 0.08));
            }
            else if (char.IsLetterOrDigit(last) || last == '_')
            {
                // After a word an autocomplete pop-up may be open, and Enter would pick a suggestion
                // instead of starting a new line. A space closes the pop-up; then take it back.
                yield return TypingAction.Wait(Uniform(0.05, 0.12) * Pace);
                yield return TypingAction.Key(' ', Uniform(0.035, 0.07));
                yield return TypingAction.Wait(Uniform(0.06, 0.14) * Pace);
                yield return TypingAction.Back(Uniform(0.03, 0.07));
            }
            NextKey('\n', 0, out wait, out hold);
            yield return TypingAction.Wait(wait);
            yield return TypingAction.Key('\n', hold);
            yield return TypingAction.At(i + 1);
            foreach (TypingAction a in ReplaceAutoIndent(i + 1))
                yield return a;
        }

        // Right after Enter the editor may have indented the new line on its own. Select that indent
        // (Shift+Home), so the next key typed replaces it and the line gets exactly the text's indent.
        IEnumerable<TypingAction> ReplaceAutoIndent(int next)
        {
            yield return TypingAction.Wait(Uniform(0.06, 0.16) * Pace);
            yield return TypingAction.Select(true, Uniform(0.04, 0.08));
            if (next < text.Length && text[next] == '\t')
            {
                // Tab on a selection would indent it instead of replacing it: clear the selection first.
                yield return TypingAction.Wait(Uniform(0.04, 0.1) * Pace);
                yield return TypingAction.Key(' ', Uniform(0.035, 0.07));
                yield return TypingAction.Wait(Uniform(0.04, 0.1) * Pace);
                yield return TypingAction.Back(Uniform(0.03, 0.07));
            }
            prev = '\n';  // so the next key gets the usual little pause at the start of a line
        }

        // Length of the word that starts at text[i], or 0 if no word starts there.
        int WordStartingAt(int i)
        {
            if (!char.IsLetterOrDigit(text[i]) || (i > 0 && !char.IsWhiteSpace(text[i - 1])))
                return 0;
            int n = 0;
            while (i + n < text.Length && char.IsLetterOrDigit(text[i + n]))
                n++;
            return n;
        }

        void NextKey(char ch, int word, out double wait, out double hold)
        {
            hold = Math.Min(Uniform(0.035, 0.085), BaseTime * 0.45);
            wait = Math.Max(Interval(ch, word) - lastHold, 0.005);
            prev = ch;
            lastHold = hold;
        }

        // Time from the previous key press to this one.
        double Interval(char ch, int word)
        {
            // Slow drift so the speed is never constant (focus, warm-up, tiredness).
            rhythm += Gauss(0.03) + (1.0 - rhythm) * 0.05;
            rhythm = Clamp(rhythm, 0.75, 1.35);

            double baseTime = BaseTime;
            double t = baseTime * rhythm * Math.Exp(Gauss(0.3));
            char a = char.ToLowerInvariant(prev), b = char.ToLowerInvariant(ch);
            int fa = Finger(a), fb = Finger(b);
            if (prev == ch)
                t *= 0.85;  // same key again
            else if (prev != '\0' && FastBigrams.Contains(new string(new[] { a, b })))
                t *= 0.75;
            else if (fa >= 0 && fa == fb)
                t *= 1.25;  // the same finger has to move to another key
            else if (fa >= 0 && fb >= 0 && (fa < 4) != (fb < 4))
                t *= 0.9;   // the other hand was already in place
            if (char.IsUpper(ch) || ShiftedSymbols.IndexOf(ch) >= 0)
                t += Uniform(0.03, 0.09);
            else if (char.IsDigit(ch))
                t *= 1.25;  // reaching up to the number row
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
            if (word > 5)
                pause += Uniform(0.0, 0.025) * (word - 5);  // long words take a moment to plan
            return t + pause * Pace;
        }

        static int Finger(char c)
        {
            for (int f = 0; f < Fingers.Length; f++)
                if (Fingers[f].IndexOf(c) >= 0)
                    return f;
            return -1;
        }

        // What gets typed when a typo happens at text[i].
        string Mistake(int i)
        {
            char ch = text[i];
            char last = i > 0 ? text[i - 1] : '\0';
            char nxt = i + 1 < text.Length ? text[i + 1] : '\0';
            bool nextDiffers = char.IsLetter(nxt) && char.ToLowerInvariant(nxt) != char.ToLowerInvariant(ch);
            double r = rng.NextDouble();
            string typed;
            int j;
            if (char.IsLower(ch) && char.IsUpper(last) && r < 0.4)
            {
                typed = char.ToUpperInvariant(ch).ToString();  // Shift let go too late: "THe"
                j = i + 1;
            }
            else if (r < 0.15 && nextDiffers)
            {
                typed = new string(new[] { nxt, ch });  // swapped letters: "teh"
                j = i + 2;
            }
            else if (r < 0.25)
            {
                typed = new string(ch, 2);  // key pressed twice
                j = i + 1;
            }
            else if (r < 0.35 && nextDiffers)
            {
                typed = nxt.ToString();  // skipped a letter
                j = i + 2;
            }
            else
            {
                string options = Neighbors[char.ToLowerInvariant(ch)];
                char wrong = options[rng.Next(options.Length)];
                typed = (char.IsUpper(ch) ? char.ToUpperInvariant(wrong) : wrong).ToString();  // nearby key
                j = i + 1;
            }
            // Often a letter or two more get typed before the mistake is noticed. In code, not
            // brackets or quotes: editors pair those up, and erasing them again can leave one behind.
            double e = rng.NextDouble();
            int extra = e < 0.45 ? 0 : e < 0.80 ? 1 : 2;
            var sb = new StringBuilder(typed);
            // Never emoji halves or accent marks either: one Backspace can erase more than one of those.
            for (int k = j; k < j + extra && k < text.Length && text[k] != '\n' && Plain(text[k]) && (!code || char.IsLetterOrDigit(text[k])); k++)
                sb.Append(text[k]);
            return sb.ToString();
        }

        // A character that stands on its own: not half of an emoji, an accent mark or an invisible joiner.
        static bool Plain(char c)
        {
            if (char.IsSurrogate(c))
                return false;
            UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
            return cat != UnicodeCategory.NonSpacingMark && cat != UnicodeCategory.SpacingCombiningMark &&
                   cat != UnicodeCategory.EnclosingMark && cat != UnicodeCategory.Format;
        }

        double Uniform(double a, double b) { return a + (b - a) * rng.NextDouble(); }

        double Gauss(double sigma)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        static double Clamp(double v, double lo, double hi) { return Math.Min(Math.Max(v, lo), hi); }
    }
}
