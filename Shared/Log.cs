using System;

namespace TavernEssentials
{
    internal sealed class FeatureLog
    {
        internal Action<string> Info;
        internal Action<string> Warning;
        internal Action<string> Failure;
        internal bool Verbose;

        internal void Msg(string s) { Fire(Info, s); }
        internal void Warn(string s) { Fire(Warning, s); }
        internal void Error(string s) { Fire(Failure, s); }

        internal void Detail(string s)
        {
            if (Verbose) Fire(Info, s);
        }

        static void Fire(Action<string> h, string s)
        {
            if (h == null) return;
            try { h(s); } catch { }
        }
    }
}
