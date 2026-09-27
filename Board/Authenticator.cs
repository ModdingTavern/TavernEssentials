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
        public bool Unreachable;
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
            string token = TokenCache.GetOrCreate(ip, username, out tokenIsNew);

            JObject resp;
            try
            {
                resp = Handshake(ip, username, token);
            }
            catch (Exception e)
            {
                res.Unreachable = true;
                res.Message = e.Message;
                return res;
            }

            string status = resp["status"] != null ? (string)resp["status"] : null;
            if (status == "ok")
            {
                ulong userId = 0;
                if (resp["user_id"] != null) userId = (ulong)resp["user_id"];
                res.QuestScene = ReadQuestScene(resp);
                if (tokenIsNew) TokenCache.Save(ip, username, token);
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

                byte[] buf = new byte[8192];
                int n = s.Read(buf, 0, buf.Length);
                if (n <= 0) throw new Exception("empty auth response");
                return JObject.Parse(Encoding.UTF8.GetString(buf, 0, n));
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

        public static string GetOrCreate(string host, string username, out bool isNew)
        {
            isNew = false;
            string existing = ReadToken(FileFor(host, username), username);
            if (existing != null) return existing;

            string legacy = ReadToken(LegacyFileFor(username), username);
            if (legacy != null) return legacy;

            isNew = true;
            return NewToken();
        }

        static string ReadToken(string path, string username)
        {
            try
            {
                if (!File.Exists(path)) return null;
                JObject d = JObject.Parse(File.ReadAllText(path));
                string u = d["username"] != null ? (string)d["username"] : "";
                string t = d["token"] != null ? (string)d["token"] : null;
                if (!string.IsNullOrEmpty(t)
                    && string.Equals(u, username, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            catch (Exception) { }
            return null;
        }

        public static void Save(string host, string username, string token)
        {
            try
            {
                Directory.CreateDirectory(Dir());
                JObject d = new JObject();
                d["username"] = username;
                d["host"] = host;
                d["token"] = token;
                string path = FileFor(host, username);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, d.ToString(Newtonsoft.Json.Formatting.None));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Log.Warn("could not persist token for \"" + host + "\": " + e.Message);
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
