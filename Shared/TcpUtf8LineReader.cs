using System;
using System.Text;

namespace KaiAosRenew
{
    /// <summary>
    /// Stateful UTF-8 decode + newline framing for TCP notify streams.
    /// Avoids splitting multi-byte characters across Read() chunk boundaries.
    /// </summary>
    internal sealed class TcpUtf8LineReader
    {
        public const int DefaultMaxLineChars = 1024 * 1024;

        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly char[] _chars;
        private readonly int _maxLineChars;

        public TcpUtf8LineReader(int maxLineChars = DefaultMaxLineChars)
        {
            _maxLineChars = maxLineChars < 1024 ? 1024 : maxLineChars;
            _chars = new char[4096];
        }

        /// <summary>
        /// Append bytes and invoke <paramref name="onLine"/> for each complete line (without trailing \n).
        /// Returns false if the pending buffer exceeds the max line length (caller should drop the peer).
        /// </summary>
        public bool Append(byte[] buf, int offset, int count, Action<string> onLine)
        {
            if (buf == null || count <= 0) return true;
            var remaining = count;
            var pos = offset;
            while (remaining > 0)
            {
                var n = Math.Min(remaining, _chars.Length);
                int bytesUsed;
                int charsUsed;
                bool completed;
                _decoder.Convert(buf, pos, n, _chars, 0, _chars.Length, false,
                    out bytesUsed, out charsUsed, out completed);
                if (bytesUsed <= 0)
                    break;
                pos += bytesUsed;
                remaining -= bytesUsed;
                for (var i = 0; i < charsUsed; i++)
                {
                    var c = _chars[i];
                    if (c == '\n')
                    {
                        EmitLine(onLine);
                        continue;
                    }
                    if (c == '\r')
                        continue;
                    if (_sb.Length >= _maxLineChars)
                        return false;
                    _sb.Append(c);
                }
            }
            return _sb.Length < _maxLineChars;
        }

        private void EmitLine(Action<string> onLine)
        {
            if (onLine == null)
            {
                _sb.Length = 0;
                return;
            }
            var line = _sb.ToString().Trim();
            _sb.Length = 0;
            if (line.Length > 0)
                onLine(line);
        }
    }
}
