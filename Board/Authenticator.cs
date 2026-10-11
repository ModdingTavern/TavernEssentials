using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Alta.Api.DataTransferModels.Models.Responses;
using Newtonsoft.Json.Linq;

namespace TavernEssentials.Board
{
    class AuthResult
    {
        public bool Ok;
        public string Jwt;
        public bool CleanFail;
        public string Message;
        public bool QuestScene;
    }

    static class Authenticator
    {
        const int AuthPort = 1762;
        const int TimeoutMs = 8000;

        public static AuthResult AuthenticateFor(string host, string ip)
        {
            AuthResult res = new AuthResult();

            string username = CurrentUsername();
            if (string.IsNullOrEmpty(username))
            {
                res.CleanFail = true;
                res.Message = "TavernEssentials: couldn't read your username to authenticate.";
                return res;
            }

            bool tokenIsNew;
            string unusable, damaged;
            string token = TokenCache.GetOrCreate(ip, username, out tokenIsNew, out unusable, out damaged);
            if (token == null)
            {
                res.CleanFail = true;
                res.Message = "Your identity for \"" + host + "\" is not usable right now (" + unusable + "), so nothing was sent.";
                return res;
            }

            if (tokenIsNew)
            {
                string why = TokenCache.Save(ip, username, token, damaged);
                if (why != null)
                {
                    res.CleanFail = true;
                    res.Message = "Could not save your identity for \"" + host + "\": " + why;
                    return res;
                }
                Log.Msg("join: saved a new identity for \"" + host + "\" before asking it.");
            }

            JObject resp;
            try
            {
                resp = Handshake(ip, username, token);
            }
            catch (Exception e)
            {
                res.Message = e.Message;
                return res;
            }

            string status = resp["status"] != null ? (string)resp["status"] : null;
            if (status == "ok")
            {
                ulong userId = 0;
                if (resp["user_id"] != null) userId = (ulong)resp["user_id"];
                res.QuestScene = ReadQuestScene(resp);
                res.Ok = true;
                res.Jwt = Jwt.MintIdentity(userId, username, token);
                return res;
            }

            res.CleanFail = true;
            if (status == "needs_password")
                res.Message = "\"" + host + "\" needs a password - join it from the launcher for now.";
            else if (status == "wrong_password")
                res.Message = "Wrong password for \"" + host + "\".";
            else if (status == "not_whitelisted")
                res.Message = "You're not whitelisted on \"" + host + "\" - apply from the launcher.";
            else
                res.Message = resp["message"] != null ? (string)resp["message"] : "Server refused the join.";
            return res;
        }

        static bool ReadQuestScene(JObject resp)
        {
            JToken q = resp["quest_scene_required"];
            if (q == null || q.Type == JTokenType.Null) return false;
            try
            {
                return (bool)q;
            }
            catch (Exception)
            {
                try
                {
                    Log.Warn("auth sent an unreadable quest_scene_required ("
                        + q.Type + ") - assuming a normal overworld.");
                }
                catch (Exception) { }
                return false;
            }
        }

        static JObject Handshake(string ip, string username, string token)
        {
            using (TcpClient client = new TcpClient())
            {
                IAsyncResult ar = client.BeginConnect(ip, AuthPort, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(TimeoutMs) || !client.Connected)
                    throw new Exception("no auth service at " + ip + ":" + AuthPort);
                client.EndConnect(ar);
                client.ReceiveTimeout = TimeoutMs;
                client.SendTimeout = TimeoutMs;

                JObject payload = new JObject();
                payload["username"] = username;
                payload["token"] = token;
                byte[] outb = Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None));

                NetworkStream s = client.GetStream();
                s.Write(outb, 0, outb.Length);

                string reply = Reply.ReadObject(s, Reply.Cap, TimeoutMs);
                if (reply.Trim().Length == 0) throw new Exception("empty auth response");
                return JObject.Parse(reply);
            }
        }

        static string CurrentUsername()
        {
            try
            {
                object info = ApiAccess.ApiClient.UserClient.LoggedInUserInfo;
                UserInfo ui = info as UserInfo;
                if (ui != null && !string.IsNullOrEmpty(ui.Username)) return ui.Username;
            }
            catch (Exception) { }
            try
            {
                System.IdentityModel.Tokens.Jwt.JwtSecurityToken id =
                    ApiAccess.ApiClient.UserCredentials.IdentityToken;
                if (id != null)
                    foreach (System.Security.Claims.Claim c in id.Claims)
                        if (c.Type == "Username" && !string.IsNullOrEmpty(c.Value)) return c.Value;
            }
            catch (Exception) { }
            return null;
        }
    }

    static class TokenCache
    {
        static string Dir()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(Path.Combine(appData, "TheModdingTavern"), "tokens");
        }

        static string Safe(string s)
        {
            if (s == null) s = "";
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_')
                    sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "x";
        }

        static string FileFor(string host, string username)
        {
            return Path.Combine(Dir(), ".token_" + Safe(host) + "__" + Safe(username) + ".json");
        }

        static string LegacyFileFor(string username)
        {
            return Path.Combine(Dir(), ".token_" + Safe(username) + ".json");
        }

        enum Found { None, Token, InUse, OtherName, Damaged }

        public static string GetOrCreate(string host, string username, out bool isNew, out string why, out string damaged)
        {
            isNew = false;
            damaged = null;
            string own = FileFor(host, username);
            Found f;
            string token = ReadToken(own, username, out why, out f);
            if (f == Found.Token) return token;
            if (f == Found.InUse || f == Found.OtherName) return null;
            if (f == Found.Damaged) damaged = own;
            else
            {
                string legacy = ReadToken(LegacyFileFor(username), username, out why, out f);
                if (f == Found.Token) return legacy;
                if (f == Found.InUse) return null;
            }
            why = null;
            isNew = true;
            return NewToken();
        }

        static string ReadToken(string path, string username, out string why, out Found found)
        {
            why = null;
            found = Found.None;
            string text;
            try
            {
                if (!File.Exists(path)) return null;
                text = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                found = Found.InUse;
                why = Path.GetFileName(path) + " could not be opened: " + e.Message + "; try again in a moment";
                return null;
            }
            string u = null, t = null;
            try
            {
                JObject d = JObject.Parse(text);
                u = d["username"] != null ? (string)d["username"] : "";
                t = d["token"] != null ? (string)d["token"] : null;
            }
            catch (Exception) { }
            if (string.IsNullOrEmpty(t))
            {
                found = Found.Damaged;
                why = Path.GetFileName(path) + " is damaged (no token in it)";
                return null;
            }
            if (!string.Equals(u, username, StringComparison.OrdinalIgnoreCase))
            {
                found = Found.OtherName;
                why = Path.GetFileName(path) + " holds the identity of \"" + u + "\", another name";
                return null;
            }
            found = Found.Token;
            return t;
        }

        public static string Save(string host, string username, string token, string damaged)
        {
            string path = FileFor(host, username);
            string tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Dir());
                if (damaged != null && File.Exists(damaged))
                {
                    string kept = damaged + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                    File.Move(damaged, kept);
                    Log.Warn("join: " + Path.GetFileName(damaged) + " was damaged; it is kept as " + Path.GetFileName(kept)
                        + " and a new identity takes its place.");
                }
                if (File.Exists(path)) return "a token file for it appeared meanwhile";
                JObject d = new JObject();
                d["username"] = username;
                d["host"] = host;
                d["token"] = token;
                File.WriteAllText(tmp, d.ToString(Newtonsoft.Json.Formatting.None));
                File.Move(tmp, path);
                return null;
            }
            catch (Exception e)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                return e.Message;
            }
        }

        static string NewToken()
        {
            byte[] b = new byte[18];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
                rng.GetBytes(b);
            return Jwt.Base64Url(b);
        }
    }

    static class Jwt
    {
        public static string MintIdentity(ulong userId, string username, string tavernToken)
        {
            const string header = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
            string payload = "{"
                + "\"UserId\":\"" + userId.ToString() + "\","
                + "\"Username\":" + JsonString(username) + ","
                + "\"role\":\"Identity\","
                + "\"is_member\":\"True\","
                + "\"is_dev\":\"True\","
                + "\"TavernToken\":" + JsonString(tavernToken) + ","
                + "\"exp\":9999999999,"
                + "\"iss\":\"AltaWebAPI\","
                + "\"aud\":\"AltaClient\""
                + "}";
            string head = Base64Url(Encoding.UTF8.GetBytes(header));
            string body = Base64Url(Encoding.UTF8.GetBytes(payload));
            string signingInput = head + "." + body;
            byte[] sig;
            using (HMACSHA256 h = new HMACSHA256(Encoding.UTF8.GetBytes("offline")))
                sig = h.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
            return signingInput + "." + Base64Url(sig);
        }

        static string JsonString(string s)
        {
            if (s == null) s = "";
            StringBuilder sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') { sb.Append('\\'); sb.Append(c); }
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Base64Url(byte[] b)
        {
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
