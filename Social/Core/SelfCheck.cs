using System;
using System.Diagnostics;
using System.Text;

namespace TavernEssentials.Social
{
    internal sealed class CryptoCheck
    {
        internal bool Ok;
        internal string Why;
        internal long SignMs;
        internal long VerifyMs;

        internal static CryptoCheck Run(KeyPair key)
        {
            CryptoCheck c = new CryptoCheck();
            byte[] probe = Encoding.UTF8.GetBytes("TavernSocial self-check " + DateTime.UtcNow.Ticks);
            Stopwatch sw = Stopwatch.StartNew();
            byte[] sig = key.Sign(probe);
            c.SignMs = sw.ElapsedMilliseconds;
            sw = Stopwatch.StartNew();
            bool good = key.Public.Verify(probe, sig);
            c.VerifyMs = sw.ElapsedMilliseconds;
            sig[sig.Length / 2] ^= 0x10;
            bool bad = key.Public.Verify(probe, sig);
            c.Ok = good && !bad;
            if (!good) c.Why = "a fresh signature did not verify";
            else if (bad) c.Why = "a tampered signature verified";
            return c;
        }

        internal string Describe()
        {
            return "sign " + SignMs + " ms, verify " + VerifyMs + " ms, tampering caught";
        }
    }

    internal sealed class KeyBoot
    {
        internal KeyFileResult File;
        internal CryptoCheck Check;
        internal long LoadMs;
        internal string ExportNote;
        internal string ExportError;

        internal bool Usable
        {
            get { return File != null && File.Key != null && Check != null && Check.Ok; }
        }

        internal static KeyBoot Run(string path, bool allowImport, string exportPath)
        {
            KeyBoot b = new KeyBoot();
            Stopwatch sw = Stopwatch.StartNew();
            b.File = KeyStore.LoadOrCreate(path, allowImport);
            b.LoadMs = sw.ElapsedMilliseconds;
            if (b.File.Key == null) return b;
            b.Check = CryptoCheck.Run(b.File.Key);
            if (exportPath != null && b.Check.Ok)
            {
                try { b.ExportNote = KeyStore.Export(b.File.Key, exportPath); }
                catch (Exception e) { b.ExportError = e.Message; }
            }
            return b;
        }
    }
}
