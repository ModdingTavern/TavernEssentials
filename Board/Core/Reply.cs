using System;
using System.IO;
using System.Text;

namespace TavernEssentials.Board
{
    internal static class Reply
    {
        internal const int Cap = 64 * 1024;

        internal static string ReadObject(Stream s, int cap, int timeoutMs)
        {
            MemoryStream got = new MemoryStream();
            byte[] buf = new byte[4096];
            int depth = 0;
            bool started = false, inString = false, escaped = false;
            int start = Environment.TickCount;
            while (true)
            {
                int left = timeoutMs - (Environment.TickCount - start);
                if (left <= 0) throw new IOException("the auth reply took longer than " + timeoutMs + " ms");
                if (s.CanTimeout) s.ReadTimeout = left;
                int n = s.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                for (int i = 0; i < n; i++)
                {
                    if (got.Length >= cap)
                        throw new IOException("the auth reply ran past " + cap + " bytes");
                    byte b = buf[i];
                    got.WriteByte(b);
                    if (inString)
                    {
                        if (escaped) escaped = false;
                        else if (b == (byte)'\\') escaped = true;
                        else if (b == (byte)'"') inString = false;
                        continue;
                    }
                    if (b == (byte)'"') inString = true;
                    else if (b == (byte)'{') { depth++; started = true; }
                    else if (b == (byte)'}' && started) depth--;
                    if (started && depth <= 0)
                        return Encoding.UTF8.GetString(got.GetBuffer(), 0, (int)got.Length);
                }
            }
            return Encoding.UTF8.GetString(got.GetBuffer(), 0, (int)got.Length);
        }
    }
}
