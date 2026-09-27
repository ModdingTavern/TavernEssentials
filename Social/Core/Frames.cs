using System;
using System.Collections.Generic;
using System.Text;

namespace TavernEssentials.Social
{
    internal static class Frames
    {
        internal const int ChunkChars = 800;
        internal const int MaxChunks = 8;
        internal const int MaxChars = ChunkChars * MaxChunks;

        internal static List<string> Split(string payload)
        {
            if (payload == null) payload = "";
            if (payload.Length > MaxChars) return null;
            List<string> pieces = new List<string>();
            int at = 0;
            do
            {
                int len = Math.Min(ChunkChars, payload.Length - at);
                pieces.Add(payload.Substring(at, len));
                at += len;
            } while (at < payload.Length);
            return pieces;
        }
    }

    internal sealed class Reassembler
    {
        internal const double StaleSeconds = 10;

        readonly StringBuilder _buf = new StringBuilder();
        bool _open;
        uint _msg;
        uint _next;
        double _lastAt;

        internal int Abandoned;

        internal string Accept(uint msg, uint seq, bool fin, string piece, double now, out string error)
        {
            error = null;
            if (piece == null) piece = "";
            if (_open && now - _lastAt > StaleSeconds) Drop();

            if (piece.Length > Frames.ChunkChars)
            {
                Drop();
                error = "frame too long";
                return null;
            }
            if (seq == 0)
            {
                if (_open) Abandoned++;
                Drop();
                _open = true;
                _msg = msg;
            }
            else if (!_open || msg != _msg || seq != _next)
            {
                Drop();
                error = "frame out of sequence";
                return null;
            }
            if (seq >= Frames.MaxChunks || _buf.Length + piece.Length > Frames.MaxChars)
            {
                Drop();
                error = "message too long";
                return null;
            }

            _buf.Append(piece);
            _next = seq + 1;
            _lastAt = now;
            if (!fin) return null;
            string whole = _buf.ToString();
            Drop();
            return whole;
        }

        void Drop()
        {
            _open = false;
            _next = 0;
            _buf.Length = 0;
        }
    }
}
