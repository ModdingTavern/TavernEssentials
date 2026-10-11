using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using NLog;

namespace TavernEssentials.LogGuard
{
    internal static class LogGuardFeature
    {
        internal const string Unchecked = "[line hidden: LogGuard could not check it]";

        static int _first;
        static int _failed;

        internal static bool Start()
        {
            if (LogGuardConfig.Mode == LogGuardConfig.ModeOff)
            {
                Log.Msg("switched off (logguard_mode=off): join tokens and passwords are logged as they come.");
                return false;
            }
            HarmonyLib.Harmony h = new HarmonyLib.Harmony("tavernessentials.logguard");
            MethodInfo nlog = NLogTarget();
            bool game = nlog != null && Patch(h, nlog, "NLogPrefix");
            if (nlog == null) Log.Warn("NLog seam missing: NLog.LoggerImpl.Write - the game's own logs are not checked.");
            MethodInfo tavern = TavernTarget();
            bool lib = tavern != null && Patch(h, tavern, "TavernPrefix");
            if (tavern == null) Log.Msg("TavernLib's logger not found: only the game's own logs are checked.");
            if (!game && !lib) return false;
            Log.Msg("ready: join tokens and passwords are hidden in "
                + (game && lib ? "the game's logs and TavernLib's." : game ? "the game's logs." : "TavernLib's logs."));
            return true;
        }

        internal static MethodInfo NLogTarget()
        {
            return Find(typeof(LogEventInfo).Assembly.GetType("NLog.LoggerImpl", false), "Write", "logEvent", typeof(LogEventInfo));
        }

        internal static MethodInfo TavernTarget()
        {
            return Find(AccessTools.TypeByName("TavernLib.TavernLogger"), "Log", "msg", typeof(object));
        }

        static MethodInfo Find(Type t, string name, string param, Type type)
        {
            if (t == null) return null;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (m.Name != name || m.IsGenericMethodDefinition) continue;
                foreach (ParameterInfo p in m.GetParameters())
                    if (p.Name == param && p.ParameterType == type) return m;
            }
            return null;
        }

        static bool Patch(HarmonyLib.Harmony h, MethodInfo target, string prefix)
        {
            try
            {
                HarmonyMethod m = new HarmonyMethod(typeof(LogGuardFeature).GetMethod(prefix, BindingFlags.Public | BindingFlags.Static));
                m.priority = Priority.First;
                h.Patch(target, m);
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("could not patch " + target.DeclaringType.FullName + "." + target.Name + ": " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        public static void NLogPrefix(LogEventInfo logEvent)
        {
            if (logEvent == null) return;
            try
            {
                if (Clean(logEvent)) Hid("the game's log");
            }
            catch (Exception e)
            {
                try
                {
                    if (Risky(logEvent))
                    {
                        logEvent.Message = Unchecked;
                        logEvent.Parameters = null;
                    }
                }
                catch (Exception) { }
                Failed(e);
            }
        }

        static bool Clean(LogEventInfo logEvent)
        {
            bool hid = false;
            string m = logEvent.Message;
            string sm = TokenScrub.Scrub(m);
            if (!ReferenceEquals(sm, m))
            {
                logEvent.Message = sm;
                hid = true;
            }
            object[] ps = logEvent.Parameters;
            if (ps == null) return hid;
            object[] copy = null;
            for (int i = 0; i < ps.Length; i++)
            {
                object now;
                string s = ps[i] as string;
                if (s != null)
                {
                    string t = TokenScrub.Scrub(s);
                    if (ReferenceEquals(s, t)) continue;
                    now = t;
                }
                else if (ps[i] is JwtSecurityToken) now = TokenScrub.Scrub(ps[i].ToString());
                else continue;
                if (copy == null) copy = (object[])ps.Clone();
                copy[i] = now;
            }
            if (copy == null) return hid;
            logEvent.Parameters = copy;
            return true;
        }

        static bool Risky(LogEventInfo logEvent)
        {
            if (Risky(logEvent.Message)) return true;
            object[] ps = logEvent.Parameters;
            if (ps == null) return false;
            foreach (object p in ps)
                if (Risky(p as string) || p is JwtSecurityToken) return true;
            return false;
        }

        static bool Risky(string s)
        {
            return s != null && (s.IndexOf("eyJ", StringComparison.Ordinal) >= 0 || s.IndexOf('"') >= 0);
        }

        public static void TavernPrefix(ref object msg)
        {
            string s = msg as string;
            if (s == null) return;
            try
            {
                string t = TokenScrub.Scrub(s);
                if (ReferenceEquals(s, t)) return;
                msg = t;
                Hid("TavernLib's log");
            }
            catch (Exception e)
            {
                if (Risky(s)) msg = Unchecked;
                Failed(e);
            }
        }

        static void Hid(string where)
        {
            if (Interlocked.Exchange(ref _first, 1) == 0) Log.Msg("hid the first token this run, in " + where + ".");
        }

        static void Failed(Exception e)
        {
            if (Interlocked.Exchange(ref _failed, 1) == 0)
                Log.Warn("could not check a log line (" + e.GetType().Name + ": " + e.Message
                    + "); such a line is hidden whole when it could hold a token. Said once a run.");
        }
    }

    internal static class Log
    {
        internal static readonly FeatureLog Out = new FeatureLog();

        internal static void Msg(string s) { Out.Msg(s); }
        internal static void Warn(string s) { Out.Warn(s); }
        internal static void Error(string s) { Out.Error(s); }
    }

    internal static class LogGuardConfig
    {
        internal const string ModeOn = "on";
        internal const string ModeOff = "off";

        internal static string Mode = ModeOn;

        internal static readonly string[] Template =
        {
            "# ---- LogGuard: player join tokens and passwords never reach the server's logs. Server only.",
            "",
            "# on = hide them (a join token works as that player's password on this server). off = log them as before.",
            "logguard_mode=on",
        };

        internal static void Load(Settings s, List<string> notes)
        {
            string v = s.Text("logguard_mode", ModeOn).ToLowerInvariant();
            if (v == ModeOn || v == ModeOff) Mode = v;
            else
            {
                Mode = ModeOn;
                notes.Add("config logguard_mode=" + v + " is not on or off; on stays.");
            }
        }
    }
}
