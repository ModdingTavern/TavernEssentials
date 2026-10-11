using System;
using System.Text;

namespace TavernEssentials.LogGuard
{
    internal static class TokenScrub
    {
        internal const string HiddenToken = "[token hidden]";
        internal const string HiddenValue = "[hidden]";
        const int MinHeader = 19;
        const int MinPayload = 16;

        internal static string Scrub(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return Secrets(Jwts(s));
        }

        static bool Run(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                || c == '+' || c == '/' || c == '_' || c == '-' || c == '=';
        }

        static bool Word(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
        }

        static int RunEnd(string s, int i)
        {
            while (i < s.Length && Run(s[i])) i++;
            return i;
        }

        internal static string Jwts(string s)
        {
            int at = s.IndexOf("eyJ", StringComparison.Ordinal);
            if (at < 0) return s;
            StringBuilder b = null;
            int copied = 0;
            while (at >= 0)
            {
                int next = at + 1;
                if (at == 0 || !Word(s[at - 1]))
                {
                    int head = RunEnd(s, at + 3);
                    next = head;
                    if (head - at >= MinHeader && head < s.Length && s[head] == '.')
                    {
                        int body = RunEnd(s, head + 1);
                        if (body - head - 1 >= MinPayload)
                        {
                            int end = body;
                            if (end < s.Length && s[end] == '.') end = RunEnd(s, end + 1);
                            if (b == null) b = new StringBuilder(s.Length);
                            b.Append(s, copied, at - copied).Append(HiddenToken);
                            copied = end;
                            next = end;
                        }
                    }
                }
                at = next >= s.Length ? -1 : s.IndexOf("eyJ", next, StringComparison.Ordinal);
            }
            if (b == null) return s;
            b.Append(s, copied, s.Length - copied);
            return b.ToString();
        }

        static bool KeyChar(char c)
        {
            return Word(c) || c == '_' || c == '-';
        }

        static bool Secret(string s, int start, int end)
        {
            int n = end - start;
            if (n == 8 && string.Compare(s, start, "password", 0, 8, StringComparison.OrdinalIgnoreCase) == 0) return true;
            return n >= 5 && string.Compare(s, end - 5, "token", 0, 5, StringComparison.OrdinalIgnoreCase) == 0;
        }

        internal static string Secrets(string s)
        {
            int i = s.IndexOf('"');
            if (i < 0) return s;
            StringBuilder b = null;
            int copied = 0;
            while (i >= 0 && i < s.Length)
            {
                int k = i + 1;
                int e = k;
                while (e < s.Length && e - k < 64 && KeyChar(s[e])) e++;
                int next;
                if (e == k || e >= s.Length || s[e] != '"') next = k;
                else if (!Secret(s, k, e)) next = e;
                else
                {
                    int p = e + 1;
                    while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
                    if (p >= s.Length || s[p] != ':') next = e;
                    else
                    {
                        p++;
                        while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
                        if (p >= s.Length || s[p] != '"') next = e;
                        else
                        {
                            int v = p + 1;
                            int q = v;
                            while (q < s.Length && s[q] != '"') q += s[q] == '\\' ? 2 : 1;
                            if (q > s.Length) q = s.Length;
                            if (b == null) b = new StringBuilder(s.Length);
                            b.Append(s, copied, v - copied).Append(HiddenValue);
                            copied = q;
                            next = q + 1;
                        }
                    }
                }
                i = next >= s.Length ? -1 : s.IndexOf('"', next);
            }
            if (b == null) return s;
            b.Append(s, copied, s.Length - copied);
            return b.ToString();
        }
    }
}
