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

    internal sealed class TickFailures
    {
        internal const double Every = 60;

        readonly FeatureLog _log;
        int _more;
        string _latest;
        double _nextLine = -1;

        internal TickFailures(FeatureLog log)
        {
            _log = log;
        }

        internal void Failed(Exception e, double now)
        {
            if (_nextLine < 0)
            {
                _nextLine = now + Every;
                _log.Warn("update failed (in full this once; after this, a count at most once a minute): " + e);
                return;
            }
            _more++;
            _latest = e.GetType().Name + ": " + e.Message;
            if (now >= _nextLine) Count(now);
        }

        internal void Quiet(double now)
        {
            if (_nextLine < 0 || now < _nextLine) return;
            if (_more > 0) Count(now);
            else _nextLine = -1;
        }

        void Count(double now)
        {
            _log.Warn("update failed " + _more + " more time(s) since the last line, the latest: " + _latest);
            _more = 0;
            _nextLine = now + Every;
        }
    }
}
