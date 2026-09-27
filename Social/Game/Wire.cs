using System;
using System.Collections.Generic;
using System.Reflection;
using Alta.Global;
using Alta.Networking;
using HarmonyLib;
using AltaStream = Alta.Serialization.Stream;

namespace TavernEssentials.Social
{
    internal static class Wire
    {
        const int MaxMessageBytes = 1097;

        static ConnectionChannel _channel;
        static bool _channelResolved;
        static uint _nextMsg;

        static void Codec(AltaStream s, ref uint ver, ref uint kind, ref uint msg, ref uint seq, ref uint fin, ref string payload)
        {
            s.SerializeBits(ref ver, 8);
            s.SerializeBits(ref kind, 8);
            s.SerializeBits(ref msg, 16);
            s.SerializeBits(ref seq, 8);
            s.SerializeBits(ref fin, 8);
            s.SerializeString(ref payload, AltaStream.StringEncoding.UTF8);
        }

        internal static bool Send(Connection connection, uint kind, Fields body)
        {
            if (connection == null) return false;
            List<string> pieces = Frames.Split(body.Encode());
            if (pieces == null)
            {
                Log.Warn("a " + Proto.KindName(kind) + " message was too long to send.");
                return false;
            }
            ResolveChannel();
            _nextMsg = (_nextMsg + 1) & 0xFFFF;
            uint msg = _nextMsg;
            for (int i = 0; i < pieces.Count; i++)
            {
                uint ver = Proto.Version, k = kind, m = msg, seq = (uint)i, fin = i == pieces.Count - 1 ? 1u : 0u;
                string piece = pieces[i];
                try
                {
                    if (!connection.Send(_channel, (MessageType)Proto.WireType,
                        delegate(Connection c, AltaStream s) { Codec(s, ref ver, ref k, ref m, ref seq, ref fin, ref piece); }))
                        return false;
                }
                catch (Exception e)
                {
                    Log.Warn("sending a " + Proto.KindName(kind) + " failed: " + e.Message);
                    return false;
                }
            }
            return true;
        }

        internal static bool Read(AltaStream s, out uint kind, out uint msg, out uint seq, out bool fin, out string payload)
        {
            uint ver = 0, k = 0, m = 0, sq = 0, f = 0;
            string p = null;
            kind = 0; msg = 0; seq = 0; fin = false; payload = null;
            try
            {
                Codec(s, ref ver, ref k, ref m, ref sq, ref f, ref p);
            }
            catch
            {
                return false;
            }
            if (ver != Proto.Version) return false;
            kind = k;
            msg = m;
            seq = sq;
            fin = f == 1;
            payload = p ?? "";
            return true;
        }

        static void ResolveChannel()
        {
            if (_channelResolved) return;
            _channelResolved = true;
            try
            {
                ConnectionChannel best = null;
                foreach (ConnectionChannel ch in GlobalSettings<NetworkingSettings>.Instance.Channels)
                {
                    if (!ch.IsReliable) continue;
                    if (best == null || ch.MaximumSendInterval < best.MaximumSendInterval) best = ch;
                }
                _channel = best;
                if (best == null) Log.Warn("no reliable channel found; using the connection's default.");
                else Log.Detail("channel: " + best.name + " (reliable, " + best.MaximumSendInterval.ToString("0.000") + " s).");
            }
            catch (Exception e)
            {
                Log.Warn("channel lookup failed, using the connection's default: " + e.Message);
            }
        }

        internal static bool MessageTypeWide(out string detail)
        {
            try
            {
                Type reader = AccessTools.TypeByName("Alta.Networking.Internal.MessageProcessor");
                Type writer = AccessTools.TypeByName("Alta.Networking.Internal.OutgoingPacketManager");
                MethodBase read = reader == null ? null : AccessTools.Method(reader, "ProcessSingleMessageFromData",
                    new Type[] { typeof(Connection), typeof(ArraySegment<byte>), typeof(ConnectionChannel), typeof(MessageType).MakeByRefType() });
                MethodBase write = writer == null ? null : AccessTools.Method(writer, "StartSerialize",
                    new Type[] { typeof(Connection), typeof(MessageType) });
                if (read == null || write == null)
                {
                    detail = "the game's message " + (read == null ? "reader" : "writer") + " was not found";
                    return false;
                }
                string r = PrefixOwners(read), w = PrefixOwners(write);
                if (r == null || w == null)
                {
                    detail = "the 8-bit message type patch is missing on the " + (r == null ? "reading" : "writing") + " side";
                    return false;
                }
                detail = r == w ? r : r + " / " + w;
                return true;
            }
            catch (Exception e)
            {
                detail = "the message type check failed: " + e.Message;
                return false;
            }
        }

        static string PrefixOwners(MethodBase m)
        {
            Patches p = HarmonyLib.Harmony.GetPatchInfo(m);
            if (p == null || p.Prefixes.Count == 0) return null;
            List<string> owners = new List<string>();
            foreach (Patch x in p.Prefixes)
                if (!owners.Contains(x.owner)) owners.Add(x.owner);
            return string.Join(", ", owners.ToArray());
        }

        internal static string SelfTest(out int bytes)
        {
            bytes = 0;
            try
            {
                string payload = new Fields().Add("name", "Ünï").Add("pad", "").Encode();
                payload = payload.Substring(0, payload.Length - 1) + new string('x', Frames.ChunkChars - payload.Length) + "\n";
                uint ver = Proto.Version, kind = Proto.Proof, msg = 65535, seq = 7, fin = 1;
                string body = payload;

                uint[] buffer = new uint[1024];
                Alta.Serialization.StreamWriter w = new Alta.Serialization.StreamWriter(buffer);
                Codec(w, ref ver, ref kind, ref msg, ref seq, ref fin, ref body);
                bytes = (int)w.AlignAndFlush();

                Alta.Serialization.StreamReader r = new Alta.Serialization.StreamReader(buffer, bytes);
                uint k, m, sq;
                bool f;
                string back;
                if (!Read(r, out k, out m, out sq, out f, out back)) return "the frame did not read back";
                if (k != Proto.Proof || m != 65535 || sq != 7 || !f || back != payload) return "the frame read back different";
                if (bytes > MaxMessageBytes) return "a full frame is " + bytes + " bytes, over the game's " + MaxMessageBytes;
                return null;
            }
            catch (Exception e)
            {
                return "threw " + e.GetType().Name + ": " + e.Message;
            }
        }
    }
}
