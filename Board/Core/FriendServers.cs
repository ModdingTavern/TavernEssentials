namespace TavernEssentials.Board
{
    internal sealed class FriendServers
    {
        internal const string FileName = "friend_servers.txt";
        internal const string RowMark = " *";
        internal const string InfoMark = "TE friends";

        static readonly string[] Header =
        {
            "# Board's marks: the servers where your game found TavernEssentials' friends working,",
            "# one community-list address per line. TavernEssentials adds and removes them as you visit;",
            "# the boards show a * after their names.",
        };

        readonly JoinedServers _file;

        internal FriendServers(string path)
        {
            _file = new JoinedServers(path, Header);
        }

        internal string Path { get { return _file.Path; } }

        internal bool Marked(string address)
        {
            return !string.IsNullOrEmpty(address) && _file.Contains(address);
        }

        internal string Said(string address, string name, bool friendsWork)
        {
            if (string.IsNullOrEmpty(address)) return null;
            string shown = "\"" + (string.IsNullOrEmpty(name) ? address : name) + "\"";
            string error;
            if (friendsWork)
            {
                if (_file.Add(address, out error)) return "marked " + shown + " on the boards: friends work there.";
            }
            else if (_file.Remove(address, out error))
                return "took the mark off " + shown + ": friends do not work there now.";
            return error == null ? null : "could not change the mark on " + shown + ": " + error;
        }

        internal static string RowName(string name, bool marked)
        {
            if (name == null) return null;
            string bare = name.TrimEnd(' ', '*');
            if (bare.Length == 0) bare = name;
            return marked ? bare + RowMark : bare;
        }

        internal static string Unmarked(string field)
        {
            return field == null ? null : field.Replace(InfoMark, "");
        }
    }
}
