using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace TavernEssentials.Social
{
    internal sealed class KeyFileResult
    {
        internal KeyPair Key;
        internal string Error;
        internal bool Created;
        internal string Note;
    }

    internal static class KeyStore
    {
        internal const string IdentityFile = "identity.priv.xml";
        internal const string ImportFile = "import.priv.xml";
        internal const string ServerFile = "server.priv.xml";

        internal static KeyFileResult LoadOrCreate(string path, bool allowImport)
        {
            KeyFileResult r = new KeyFileResult();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                if (!File.Exists(path) && File.Exists(tmp)) Recover(path, tmp, r);
                if (allowImport) ApplyImport(path, r);

                if (File.Exists(path))
                {
                    string why;
                    r.Key = KeyPair.FromPrivateXml(File.ReadAllText(path), out why);
                    if (r.Key == null)
                        r.Error = Path.GetFileName(path) + " will not load (" + why + "). It has not been touched."
                            + " Restore it from a backup, or move it out of the folder to start over with a new key.";
                    return r;
                }

                KeyPair k = KeyPair.Generate(Keys.Bits);
                WriteNew(path, k.PrivateXml);
                r.Key = k;
                r.Created = true;
                return r;
            }
            catch (Exception e)
            {
                r.Key = null;
                r.Error = "key file problem: " + e.GetType().Name + ": " + e.Message;
                return r;
            }
        }

        static void Recover(string path, string tmp, KeyFileResult r)
        {
            string why;
            KeyPair k = KeyPair.FromPrivateXml(File.ReadAllText(tmp), out why);
            if (k != null)
            {
                File.Move(tmp, path);
                r.Note = "finished an interrupted key write (" + k.Public.Short + ").";
                return;
            }
            File.Move(tmp, path + ".broken-" + Stamp());
            r.Note = "set aside an unfinished key write (" + why + ").";
        }

        static void ApplyImport(string path, KeyFileResult r)
        {
            string dir = Path.GetDirectoryName(path);
            string import = Path.Combine(dir, ImportFile);
            if (!File.Exists(import)) return;

            string why;
            KeyPair incoming = KeyPair.FromPrivateXml(File.ReadAllText(import), out why);
            if (incoming == null)
            {
                r.Note = ImportFile + " was not used (" + why + "). Nothing was changed.";
                return;
            }

            string stamp = Stamp();
            string done = Path.Combine(dir, "import.done-" + stamp + ".priv.xml");
            if (File.Exists(path))
            {
                string ignored;
                KeyPair current = KeyPair.FromPrivateXml(File.ReadAllText(path), out ignored);
                if (current != null && current.Public.SameAs(incoming.Public))
                {
                    File.Move(import, done);
                    r.Note = "the imported key is the one already in use (" + incoming.Public.Short + ").";
                    return;
                }
                string kept = Path.Combine(dir, "identity.replaced-" + stamp + ".priv.xml");
                WriteReplace(path, incoming.PrivateXml, kept);
                File.Move(import, done);
                r.Note = "imported key " + incoming.Public.Short + ". The previous key is kept as " + Path.GetFileName(kept) + ".";
                return;
            }
            WriteNew(path, incoming.PrivateXml);
            File.Move(import, done);
            r.Note = "imported key " + incoming.Public.Short + ".";
        }

        internal static string Export(KeyPair key, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!File.Exists(path))
            {
                WriteNew(path, key.PrivateXml);
                return "identity copied to " + path + ".";
            }
            if (File.ReadAllText(path) == key.PrivateXml) return null;
            string kept = Path.Combine(Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)) + ".replaced-" + Stamp() + ".priv.xml");
            WriteReplace(path, key.PrivateXml, kept);
            return "identity copied to " + path + "; the different key that was there is kept as " + Path.GetFileName(kept) + ".";
        }

        internal static void WriteNew(string path, string text)
        {
            string tmp = path + ".tmp";
            WriteThrough(tmp, text);
            File.Move(tmp, path);
        }

        internal static void WriteReplace(string path, string text, string backup)
        {
            string tmp = path + ".tmp";
            WriteThrough(tmp, text);
            File.Replace(tmp, path, backup);
        }

        static void WriteThrough(string tmp, string text)
        {
            byte[] b = new UTF8Encoding(false).GetBytes(text);
            using (FileStream f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                f.Write(b, 0, b.Length);
        }

        internal static string Stamp()
        {
            return DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        }
    }
}
