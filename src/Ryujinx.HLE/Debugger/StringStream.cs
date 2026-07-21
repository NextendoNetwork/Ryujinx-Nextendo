using System.Diagnostics;
using System.Globalization;

namespace Ryujinx.HLE.Debugger
{
    internal class StringStream
    {
        private readonly string _data;
        private int _position;

        public StringStream(string s)
        {
            _data = s;
        }
        
        public bool IsEmpty => _position >= _data.Length;

        public char ReadChar() => _data[_position++];

        public string ReadUntil(char needle)
        {
            int needlePos = _data.IndexOf(needle, _position);

            if (needlePos == -1)
            {
                needlePos = _data.Length;
            }

            string result = _data.Substring(_position, needlePos - _position);
            _position = needlePos + 1;
            return result;
        }

        public string ReadLength(int len)
        {
            string result = _data.Substring(_position, len);
            _position += len;
            return result;
        }

        public string ReadRemaining()
        {
            string result = _data[_position..];
            _position = _data.Length;
            return result;
        }

        public ulong ReadRemainingAsHex() 
            => ulong.Parse(ReadRemaining(), NumberStyles.HexNumber);

        public ulong ReadUntilAsHex(char needle) 
            => ulong.Parse(ReadUntil(needle), NumberStyles.HexNumber);

        public ulong ReadLengthAsHex(int len) 
            => ulong.Parse(ReadLength(len), NumberStyles.HexNumber);

        public ulong ReadLengthAsLittleEndianHex(int len)
        {
            Debug.Assert(len % 2 == 0);

            ulong result = 0;
            int pos = 0;
            while (pos < len)
            {
                result += ReadLengthAsHex(2) << (4 * pos);
                pos += 2;
            }
            return result;
        }

        public ulong? ReadRemainingAsThreadUid()
        {
            string s = ReadRemaining();
            return s == "-1" ? null : ulong.Parse(s, NumberStyles.HexNumber);
        }

        public bool ConsumePrefix(string prefix)
        {
            if (_data[_position..].StartsWith(prefix))
            {
                _position += prefix.Length;
                return true;
            }
            return false;
        }

        public bool ConsumeRemaining(string match)
        {
            if (_data[_position..] == match)
            {
                _position += match.Length;
                return true;
            }
            return false;
        }
    }
}
