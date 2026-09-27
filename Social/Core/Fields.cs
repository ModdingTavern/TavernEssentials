using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TavernEssentials.Social
{
    internal sealed class Fields
    {
        internal const int MaxLines = 256;
        internal const int MaxKey = 24;

        readonly List<KeyValuePair<string, string>> _items = new List<KeyValuePair<string, string>>();

        internal int Count { get { return _items.Count; } }

        internal Fields Add(string key, string value)
        {
            if (!ValidKey(key)) throw new ArgumentException("bad field key: " + key);
            if (value == null) throw new ArgumentNullException("value");
            _items.Add(new KeyValuePair<string, string>(key, value));
            return this;
        }

        internal Fields Add(string key, long value)
        {
            return Add(key, value.ToString(CultureInfo.InvariantCulture));
        }

        internal string Get(string key)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Key == key) return _items[i].Value;
            return null;
        }

        internal bool TryGetInt(string key, out int value)
        {
            string s = Get(key);
            value = 0;
            return s != null && int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        internal List<string> GetAll(string key)
        {
            List<string> all = new List<string>();
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Key == key) all.Add(_items[i].Value);
            return all;
        }

        internal string Encode()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _items.Count; i++)
            {
                sb.Append(_items[i].Key).Append('=');
                Escape(_items[i].Value, sb);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        internal static Fields Decode(string text, out string why)
        {
            if (text == null) { why = "no text"; return null; }
            Fields f = new Fields();
            int at = 0;
            while (at < text.Length)
            {
                int end = text.IndexOf('\n', at);
                if (end < 0) { why = "unterminated line"; return null; }
                if (f._items.Count >= MaxLines) { why = "too many lines"; return null; }
                int eq = text.IndexOf('=', at, end - at);
                if (eq <= at) { why = "line without a key"; return null; }
                string key = text.Substring(at, eq - at);
                if (!ValidKey(key)) { why = "bad key"; return null; }
                string value = Unescape(text, eq + 1, end, out why);
                if (value == null) return null;
                f._items.Add(new KeyValuePair<string, string>(key, value));
                at = end + 1;
            }
            why = null;
            return f;
        }

        internal static bool ValidKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > MaxKey) return false;
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            }
            return true;
        }

        static void Escape(string value, StringBuilder sb)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c < 0x20 || c > 0x7E) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
        }

        static string Unescape(string text, int from, int to, out string why)
        {
            StringBuilder sb = new StringBuilder(to - from);
            for (int i = from; i < to; i++)
            {
                char c = text[i];
                if (c < 0x20 || c > 0x7E) { why = "raw character outside printable ASCII"; return null; }
                if (c != '\\') { sb.Append(c); continue; }
                if (i + 1 >= to) { why = "dangling escape"; return null; }
                char n = text[++i];
                if (n == '\\') sb.Append('\\');
                else if (n == 'n') sb.Append('\n');
                else if (n == 'r') sb.Append('\r');
                else if (n == 'u')
                {
                    int code;
                    if (i + 4 >= to || !int.TryParse(text.Substring(i + 1, 4), NumberStyles.AllowHexSpecifier,
                            CultureInfo.InvariantCulture, out code))
                    {
                        why = "bad \\u escape";
                        return null;
                    }
                    sb.Append((char)code);
                    i += 4;
                }
                else { why = "unknown escape"; return null; }
            }
            why = null;
            return sb.ToString();
        }
    }
}
