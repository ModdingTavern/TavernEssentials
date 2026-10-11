using System.Globalization;
using System.Text;

namespace TavernEssentials.Social
{
    internal static class Proto
    {
        internal const int WireType = 35;
        internal const uint Version = 1;

        internal const uint Hello = 1;
        internal const uint Challenge = 2;
        internal const uint Proof = 3;
        internal const uint Welcome = 4;
        internal const uint Roster = 5;
        internal const uint Reject = 6;
        internal const uint Presence = 7;
        internal const uint Offer = 8;
        internal const uint Consent = 9;
        internal const uint Record = 10;
        internal const uint Show = 11;
        internal const uint Mutual = 12;
        internal const uint Known = 13;
        internal const uint Away = 14;
        internal const uint Withdraw = 15;
        internal const uint Withdrawn = 16;

        internal const string StatementTag = "TavernSocial-auth-v1";
        internal const string FriendTag = "TavernSocial-friend-v1";
        internal const string KnownTagText = "TavernSocial-known-v1";
        internal const string UnfriendTag = "TavernSocial-unfriend-v1";
        internal const string UnfriendIndexTag = "TavernSocial-unfriend-index-v1";

        internal static string KnownTag(string serverFingerprint, string fingerprint)
        {
            byte[] h = Keys.Hash(Encoding.ASCII.GetBytes(KnownTagText + "\n" + serverFingerprint + "\n" + fingerprint + "\n"));
            byte[] b = new byte[9];
            System.Array.Copy(h, b, 9);
            return Keys.ToBase64Url(b);
        }

        internal static byte[] Statement(string serverFingerprint, string nonce, int sessionId, string name)
        {
            string s = StatementTag + "\n"
                + serverFingerprint + "\n"
                + nonce + "\n"
                + sessionId.ToString(CultureInfo.InvariantCulture) + "\n"
                + name + "\n";
            return Encoding.UTF8.GetBytes(s);
        }

        internal static byte[] FriendStatement(string aFingerprint, string bFingerprint, long ts, string serverFingerprint)
        {
            string s = FriendTag + "\n"
                + aFingerprint + "\n"
                + bFingerprint + "\n"
                + ts.ToString(CultureInfo.InvariantCulture) + "\n"
                + serverFingerprint + "\n";
            return Encoding.UTF8.GetBytes(s);
        }

        internal static byte[] UnfriendStatement(string removerFingerprint, string friendFingerprint, long ts, string serverFingerprint)
        {
            string s = UnfriendTag + "\n"
                + removerFingerprint + "\n"
                + friendFingerprint + "\n"
                + ts.ToString(CultureInfo.InvariantCulture) + "\n"
                + serverFingerprint + "\n";
            return Encoding.UTF8.GetBytes(s);
        }

        internal static string UnfriendIndex(string removerFingerprint, string friendFingerprint, long ts, string serverFingerprint)
        {
            byte[] tag = Encoding.ASCII.GetBytes(UnfriendIndexTag + "\n");
            byte[] statement = UnfriendStatement(removerFingerprint, friendFingerprint, ts, serverFingerprint);
            byte[] all = new byte[tag.Length + statement.Length];
            System.Array.Copy(tag, all, tag.Length);
            System.Array.Copy(statement, 0, all, tag.Length, statement.Length);
            return Keys.ToBase64Url(Keys.Hash(all));
        }

        internal static bool LooksLikeIndex(string s)
        {
            return Keys.LooksLikeFingerprint(s);
        }

        internal static string KindName(uint kind)
        {
            switch (kind)
            {
                case Hello: return "hello";
                case Challenge: return "challenge";
                case Proof: return "proof";
                case Welcome: return "welcome";
                case Roster: return "roster";
                case Reject: return "reject";
                case Presence: return "presence";
                case Offer: return "offer";
                case Consent: return "consent";
                case Record: return "record";
                case Show: return "show";
                case Mutual: return "mutual";
                case Known: return "known";
                case Away: return "away";
                case Withdraw: return "withdraw";
                case Withdrawn: return "withdrawn";
                default: return "kind " + kind.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
