using ARMeilleure.State;
using System.Runtime.InteropServices;

namespace ARMeilleure.Instructions
{
    static partial class SoftFallback
    {
        [UnmanagedCallersOnly]
        public static V128 Decrypt(V128 value, V128 roundKey)
        {
            return CryptoHelper.AesInvSubBytes(CryptoHelper.AesInvShiftRows(value ^ roundKey));
        }

        [UnmanagedCallersOnly]
        public static V128 Encrypt(V128 value, V128 roundKey)
        {
            return CryptoHelper.AesSubBytes(CryptoHelper.AesShiftRows(value ^ roundKey));
        }

        [UnmanagedCallersOnly]
        public static V128 InverseMixColumns(V128 value)
        {
            return CryptoHelper.AesInvMixColumns(value);
        }

        [UnmanagedCallersOnly]
        public static V128 MixColumns(V128 value)
        {
            return CryptoHelper.AesMixColumns(value);
        }
    }
}
