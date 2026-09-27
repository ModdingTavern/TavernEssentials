using System.IO;
using System.Text;

namespace TavernEssentials.Backend
{
    internal static class Welcome
    {
        internal const int Id = -7000;
        internal const string Title = "Welcome!";

        internal static readonly string Message = "Thanks for playing with " + Mod.Name + "!";

        internal static void Mark(string path, string version)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, version + "\r\n", new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }
}
