using System;
using System.Security.Cryptography;
using System.Text;

namespace TavernEssentials.Social
{
    internal sealed class PublicKey
    {
        internal const int MinBits = 2048;
        internal const int MaxBits = 4096;

        readonly byte[] _n;
        readonly byte[] _e;
        internal readonly string Fingerprint;

        PublicKey(byte[] n, byte[] e)
        {
            _n = n;
            _e = e;
            Fingerprint = Keys.FingerprintOf(n, e);
        }

        internal string ModulusBase64 { get { return Convert.ToBase64String(_n); } }
        internal string ExponentBase64 { get { return Convert.ToBase64String(_e); } }
        internal string Short { get { return Keys.Short(Fingerprint); } }

        internal static PublicKey FromBase64(string n, string e, out string why)
        {
            byte[] nb = Keys.FromBase64(n);
            byte[] eb = Keys.FromBase64(e);
            if (nb == null || eb == null)
            {
                why = "not base64";
                return null;
            }
            return From(nb, eb, out why);
        }

        internal static PublicKey From(byte[] n, byte[] e, out string why)
        {
            n = Keys.Strip(n);
            e = Keys.Strip(e);
            if (n.Length == 0 || e.Length == 0) { why = "empty"; return null; }
            int bits = (n.Length - 1) * 8 + BitLength(n[0]);
            if (bits < MinBits) { why = "key too small (" + bits + " bits)"; return null; }
            if (bits > MaxBits) { why = "key too large (" + bits + " bits)"; return null; }
            if ((n[n.Length - 1] & 1) == 0) { why = "even modulus"; return null; }
            if (e.Length > 4) { why = "exponent too large"; return null; }
            if ((e[e.Length - 1] & 1) == 0) { why = "even exponent"; return null; }
            if (e.Length == 1 && e[0] < 3) { why = "exponent too small"; return null; }
            why = null;
            return new PublicKey(n, e);
        }

        static int BitLength(byte b)
        {
            int bits = 0;
            while (b != 0) { bits++; b >>= 1; }
            return bits;
        }

        internal RSAParameters Parameters()
        {
            RSAParameters p = new RSAParameters();
            p.Modulus = (byte[])_n.Clone();
            p.Exponent = (byte[])_e.Clone();
            return p;
        }

        internal bool Verify(byte[] data, byte[] signature)
        {
            if (data == null || signature == null || signature.Length != _n.Length) return false;
            RSACryptoServiceProvider rsa = null;
            try
            {
                rsa = Keys.NewProvider();
                rsa.ImportParameters(Parameters());
                return rsa.VerifyHash(Keys.Hash(data), Keys.Sha256Oid, signature);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (rsa != null) rsa.Clear();
            }
        }

        internal bool SameAs(PublicKey other)
        {
            return other != null && other.Fingerprint == Fingerprint;
        }
    }

    internal sealed class KeyPair
    {
        readonly string _privateXml;
        internal readonly PublicKey Public;

        KeyPair(string privateXml, PublicKey pub)
        {
            _privateXml = privateXml;
            Public = pub;
        }

        internal string PrivateXml { get { return _privateXml; } }

        internal static KeyPair Generate(int bits)
        {
            RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(bits);
            try
            {
                rsa.PersistKeyInCsp = false;
                string why;
                KeyPair k = FromPrivateXml(rsa.ToXmlString(true), out why);
                if (k == null) throw new CryptographicException("a freshly generated key failed its own check: " + why);
                return k;
            }
            finally
            {
                rsa.Clear();
            }
        }

        internal static KeyPair FromPrivateXml(string xml, out string why)
        {
            if (string.IsNullOrEmpty(xml)) { why = "empty"; return null; }
            RSACryptoServiceProvider rsa = null;
            try
            {
                rsa = Keys.NewProvider();
                rsa.FromXmlString(xml);
                RSAParameters p = rsa.ExportParameters(true);
                if (p.D == null || p.D.Length == 0) { why = "no private part"; return null; }
                PublicKey pub = PublicKey.From(p.Modulus, p.Exponent, out why);
                if (pub == null) return null;
                KeyPair k = new KeyPair(xml, pub);
                byte[] probe = Encoding.UTF8.GetBytes("TavernSocial key check");
                if (!pub.Verify(probe, k.Sign(probe)))
                {
                    why = "its private and public halves do not belong together";
                    return null;
                }
                return k;
            }
            catch (Exception e)
            {
                why = "unreadable (" + e.GetType().Name + ")";
                return null;
            }
            finally
            {
                if (rsa != null) rsa.Clear();
            }
        }

        internal byte[] Sign(byte[] data)
        {
            RSACryptoServiceProvider rsa = Keys.NewProvider();
            try
            {
                rsa.FromXmlString(_privateXml);
                return rsa.SignHash(Keys.Hash(data), Keys.Sha256Oid);
            }
            finally
            {
                rsa.Clear();
            }
        }
    }

    internal static class Keys
    {
        internal const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
        internal const int Bits = 2048;
        internal const int NonceBytes = 32;

        static readonly object RngGate = new object();
        static readonly RNGCryptoServiceProvider Rng = new RNGCryptoServiceProvider();

        internal static RSACryptoServiceProvider NewProvider()
        {
            RSACryptoServiceProvider rsa = new RSACryptoServiceProvider();
            rsa.PersistKeyInCsp = false;
            return rsa;
        }

        internal static byte[] Hash(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(data);
            }
        }

        internal static string FingerprintOf(byte[] n, byte[] e)
        {
            string s = "tsk1:" + Convert.ToBase64String(Strip(n)) + ":" + Convert.ToBase64String(Strip(e));
            return ToBase64Url(Hash(Encoding.ASCII.GetBytes(s)));
        }

        internal static string Short(string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint)) return "?";
            return fingerprint.Length <= 8 ? fingerprint : fingerprint.Substring(0, 8);
        }

        internal static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            StringBuilder sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                int run = 0;
                while (i + run < text.Length && IsBase64UrlChar(text[i + run])) run++;
                if (run == 0) { sb.Append(text[i]); i++; continue; }
                string word = text.Substring(i, run);
                sb.Append(run >= 43 ? word.Substring(0, 8) + "..." : word);
                i += run;
            }
            return sb.ToString();
        }

        internal static bool LooksLikeFingerprint(string s)
        {
            if (s == null || s.Length != 43) return false;
            for (int i = 0; i < s.Length; i++)
                if (!IsBase64UrlChar(s[i])) return false;
            return true;
        }

        internal static string NewNonce()
        {
            byte[] b = new byte[NonceBytes];
            lock (RngGate) Rng.GetBytes(b);
            return ToBase64Url(b);
        }

        internal static bool LooksLikeNonce(string s)
        {
            byte[] b = FromBase64Url(s);
            return b != null && b.Length == NonceBytes;
        }

        internal static byte[] Strip(byte[] b)
        {
            if (b == null) return new byte[0];
            int i = 0;
            while (i < b.Length && b[i] == 0) i++;
            if (i == 0) return b;
            byte[] r = new byte[b.Length - i];
            Buffer.BlockCopy(b, i, r, 0, r.Length);
            return r;
        }

        internal static string ToBase64Url(byte[] b)
        {
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        internal static byte[] FromBase64Url(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            for (int i = 0; i < s.Length; i++)
                if (!IsBase64UrlChar(s[i])) return null;
            string t = s.Replace('-', '+').Replace('_', '/');
            switch (t.Length % 4)
            {
                case 2: t += "=="; break;
                case 3: t += "="; break;
                case 1: return null;
            }
            return FromBase64(t);
        }

        internal static byte[] FromBase64(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try { return Convert.FromBase64String(s); }
            catch (FormatException) { return null; }
        }

        static bool IsBase64UrlChar(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
        }
    }
}
