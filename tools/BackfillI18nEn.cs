// One-shot: backfill missing lang keys with English text.
// csc /nologo /out:tools\BackfillI18nEn.exe tools\BackfillI18nEn.cs
// tools\BackfillI18nEn.exe
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

class BackfillI18nEn
{
    static int Main(string[] args)
    {
        var root = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        if (!File.Exists(Path.Combine(root, "UiStrings.cs")))
            root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        if (!File.Exists(Path.Combine(root, "UiStrings.cs")))
            root = @"C:\Users\Administrator\KaiAosRenew";

        Backfill(
            Path.Combine(root, "UiStrings.cs"),
            Style.D,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ro" });
        Backfill(
            Path.Combine(root, "KaiAosNotifyClient", "ClientUiStrings.cs"),
            Style.Dict,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ro" });
        Console.WriteLine("Done.");
        return 0;
    }

    enum Style { D, Dict }

    static void Backfill(string path, Style style, HashSet<string> skip)
    {
        Console.WriteLine("Backfilling " + path + " (" + style + ")...");
        var text = File.ReadAllText(path);
        var methodRe = new Regex(@"private static Dictionary<string, string> ([A-Za-z]+)\(\)\s*\{", RegexOptions.Multiline);
        var matches = methodRe.Matches(text);
        var methods = new List<MethodSpan>();
        foreach (Match m in matches)
        {
            int startBrace = m.Index + m.Length - 1;
            int end = FindMatchingBrace(text, startBrace);
            methods.Add(new MethodSpan
            {
                Name = m.Groups[1].Value,
                Start = m.Index,
                End = end,
                Full = text.Substring(m.Index, end - m.Index + 1)
            });
        }

        var en = methods.First(x => x.Name == "En");
        string enInner = ExtractInner(en.Full, style);
        var enMap = ParsePairs(enInner, style);
        Console.WriteLine("  EN keys: " + enMap.Count);

        // Replace from end so earlier indices stay valid.
        var ordered = methods.OrderByDescending(x => x.Start).ToList();
        var newText = text;
        foreach (var meth in ordered)
        {
            if (string.Equals(meth.Name, "En", StringComparison.OrdinalIgnoreCase)) continue;
            if (skip.Contains(meth.Name))
            {
                Console.WriteLine("  Skip " + meth.Name);
                continue;
            }

            // Re-read method from current newText using name scan near original region is hard;
            // instead rebuild from original meth.Full which is still correct because we only
            // mutate later (higher Start) regions.
            string full = meth.Full;
            int absInnerStart, absInnerEnd;
            string inner = ExtractInner(full, style, out absInnerStart, out absInnerEnd);
            var have = new HashSet<string>(ParsePairs(inner, style).Keys, StringComparer.Ordinal);
            var missing = enMap.Keys.Where(k => !have.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Console.WriteLine("  " + meth.Name + ": have " + have.Count + ", missing " + missing.Count);
            if (missing.Count == 0) continue;

            var sb = new StringBuilder();
            sb.Append(inner.TrimEnd());
            if (!inner.TrimEnd().EndsWith(",", StringComparison.Ordinal) && style == Style.D)
            {
                // last D() entry may omit trailing comma; add one before backfill
                sb.Append(",");
            }
            else if (!inner.TrimEnd().EndsWith(",", StringComparison.Ordinal) && style == Style.Dict)
            {
                sb.Append(",");
            }
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("                // EN baseline backfill");
            for (int i = 0; i < missing.Count; i++)
            {
                var k = missing[i];
                var v = enMap[k];
                bool last = i == missing.Count - 1;
                if (style == Style.D)
                {
                    sb.Append("                \"");
                    sb.Append(Escape(k));
                    sb.Append("\", \"");
                    sb.Append(Escape(v));
                    sb.Append(last ? "\"" : "\",");
                    sb.AppendLine();
                }
                else
                {
                    sb.Append("                { \"");
                    sb.Append(Escape(k));
                    sb.Append("\", \"");
                    sb.Append(Escape(v));
                    sb.Append(last ? "\" }" : "\" },");
                    sb.AppendLine();
                }
            }

            string newInner = sb.ToString().TrimEnd() + Environment.NewLine + "            ";
            string newFull = full.Substring(0, absInnerStart) + newInner + full.Substring(absInnerEnd);
            newText = newText.Substring(0, meth.Start) + newFull + newText.Substring(meth.End + 1);
        }

        File.WriteAllText(path, newText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine("  Wrote " + path);
    }

    static int FindMatchingBrace(string text, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                i = SkipString(text, i);
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        throw new Exception("Unbalanced brace at " + openIndex);
    }

    static int SkipString(string text, int i)
    {
        // text[i] == '"'
        i++;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length) { i += 2; continue; }
            if (c == '"') return i;
            i++;
        }
        return text.Length - 1;
    }

    static string ExtractInner(string full, Style style)
    {
        int a, b;
        return ExtractInner(full, style, out a, out b);
    }

    static string ExtractInner(string full, Style style, out int innerStart, out int innerEnd)
    {
        if (style == Style.D)
        {
            int d = full.IndexOf("return D(", StringComparison.Ordinal);
            if (d < 0) throw new Exception("return D( not found");
            innerStart = d + "return D(".Length;
            // find matching ); for D( — last ");" before method end is wrong if strings contain );
            // Find the closing paren of D( by scanning with string awareness.
            int depth = 1;
            int i = innerStart;
            while (i < full.Length)
            {
                char c = full[i];
                if (c == '"') { i = SkipString(full, i) + 1; continue; }
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        innerEnd = i;
                        return full.Substring(innerStart, innerEnd - innerStart);
                    }
                }
                i++;
            }
            throw new Exception("D( not closed");
        }
        else
        {
            int ret = full.IndexOf("return new Dictionary", StringComparison.Ordinal);
            if (ret < 0) throw new Exception("return new Dictionary not found");
            int open = full.IndexOf('{', ret);
            // first { after return is method? No — method already opened. Structure:
            // private static ... Name() { return new Dictionary... { ... }; }
            // The { after Dictionary is the initializer.
            // Find "return new Dictionary" then next { that starts initializer.
            // Actually: return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ... };
            int initOpen = -1;
            for (int i = ret; i < full.Length; i++)
            {
                if (full[i] == '"') { i = SkipString(full, i); continue; }
                if (full[i] == '{')
                {
                    // Could be the method body already passed — we're inside method Full which starts at "private..."
                    // First { is method body (included in Full from match). Full starts at "private static..."
                    // So first { is method, second relevant is initializer after return.
                    if (i > ret) { initOpen = i; break; }
                }
            }
            if (initOpen < 0) throw new Exception("dict initializer not found");
            innerStart = initOpen + 1;
            int end = FindMatchingBrace(full, initOpen);
            innerEnd = end;
            return full.Substring(innerStart, innerEnd - innerStart);
        }
    }

    static Dictionary<string, string> ParsePairs(string body, Style style)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        int i = 0;
        while (i < body.Length)
        {
            while (i < body.Length && char.IsWhiteSpace(body[i])) i++;
            if (i >= body.Length) break;

            if (style == Style.Dict)
            {
                if (body[i] == '/') { SkipComment(body, ref i); continue; }
                if (body[i] != '{') { i++; continue; }
                i++;
                while (i < body.Length && char.IsWhiteSpace(body[i])) i++;
            }
            else
            {
                if (body[i] == '/') { SkipComment(body, ref i); continue; }
            }

            if (i >= body.Length || body[i] != '"') { i++; continue; }
            string key = ReadString(body, ref i);
            while (i < body.Length && (char.IsWhiteSpace(body[i]) || body[i] == ',')) i++;
            if (i >= body.Length || body[i] != '"') continue;
            string val = ReadString(body, ref i);
            map[key] = val;

            while (i < body.Length && char.IsWhiteSpace(body[i])) i++;
            if (style == Style.Dict)
            {
                if (i < body.Length && body[i] == '}') i++;
                while (i < body.Length && (char.IsWhiteSpace(body[i]) || body[i] == ',')) i++;
            }
            else
            {
                while (i < body.Length && (body[i] == ',' || char.IsWhiteSpace(body[i]))) i++;
            }
        }
        return map;
    }

    static void SkipComment(string body, ref int i)
    {
        if (i + 1 < body.Length && body[i] == '/' && body[i + 1] == '/')
        {
            while (i < body.Length && body[i] != '\n') i++;
            return;
        }
        if (i + 1 < body.Length && body[i] == '/' && body[i + 1] == '*')
        {
            i += 2;
            while (i + 1 < body.Length && !(body[i] == '*' && body[i + 1] == '/')) i++;
            i = Math.Min(body.Length, i + 2);
        }
    }

    static string ReadString(string s, ref int i)
    {
        if (s[i] != '"') throw new Exception("Expected quote at " + i);
        i++;
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '"') { i++; return sb.ToString(); }
            if (c == '\\' && i + 1 < s.Length)
            {
                char n = s[i + 1];
                switch (n)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    default: sb.Append(c); sb.Append(n); break;
                }
                i += 2;
                continue;
            }
            sb.Append(c);
            i++;
        }
        throw new Exception("Unterminated string");
    }

    static string Escape(string s)
    {
        if (s == null) return "";
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    class MethodSpan
    {
        public string Name;
        public int Start;
        public int End;
        public string Full;
    }
}
