using System;

namespace TavernEssentials
{
    internal static class ServerNames
    {
        internal static Func<string> Listed = null;

        internal static Func<int> ListedLimit = null;

        internal static Action<bool> FriendsWork = null;

        internal static void SayFriendsWork(bool yes)
        {
            Action<bool> said = FriendsWork;
            if (said != null) said(yes);
        }

        internal static string Here(string gameName)
        {
            Func<string> listed = Listed;
            string name = null;
            if (listed != null)
            {
                try { name = listed(); }
                catch (Exception) { name = null; }
            }
            if (!string.IsNullOrEmpty(name)) return name;
            return gameName ?? "";
        }

        internal static int Limit()
        {
            Func<int> limit = ListedLimit;
            if (limit == null) return 0;
            try
            {
                int n = limit();
                return n > 0 ? n : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
