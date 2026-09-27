namespace TavernEssentials.Social
{
    internal static class Log
    {
        internal static readonly FeatureLog Out = new FeatureLog();

        internal static void Msg(string s) { Out.Msg(s); }
        internal static void Warn(string s) { Out.Warn(s); }
        internal static void Error(string s) { Out.Error(s); }
        internal static void Detail(string s) { Out.Detail(s); }
    }
}
