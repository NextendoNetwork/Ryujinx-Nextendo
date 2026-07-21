using Gommon;
using System;
using System.Linq;
using System.Text;

namespace Ryujinx.HLE.Debugger
{
    public static class Helpers
    {
        public static byte CalculateChecksum(string cmd)
        {
            byte checksum = 0;
            foreach (char x in cmd)
            {
                unchecked
                {
                    checksum += (byte)x;
                }
            }

            return checksum;
        }

        public static string FromHex(string hexString)
        {
            if (string.IsNullOrEmpty(hexString))
                return string.Empty;

            byte[] bytes = Convert.FromHexString(hexString);
            return Encoding.ASCII.GetString(bytes);
        }

        public static string ToHex(byte[] bytes) => string.Join("", bytes.Select(x => $"{x:x2}"));

        public static string ToHex(string str) => ToHex(Encoding.ASCII.GetBytes(str));

        public static string ToBinaryFormat(string str) => ToBinaryFormat(Encoding.ASCII.GetBytes(str));
        public static string ToBinaryFormat(byte[] bytes) =>
            bytes.Select(x =>
                x switch
                {
                    (byte)'#' => "}\x03",
                    (byte)'$' => "}\x04",
                    (byte)'*' => "}\x0a",
                    (byte)'}' => "}\x5d",
                    _ => Convert.ToChar(x).ToString()
                }
            ).JoinToString(string.Empty);
    }
}
