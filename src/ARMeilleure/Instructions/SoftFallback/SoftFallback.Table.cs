using ARMeilleure.State;
using System;
using System.Runtime.InteropServices;

namespace ARMeilleure.Instructions
{
    static partial class SoftFallback
    {
        [UnmanagedCallersOnly]
        public static V128 Tbl1(V128 vector, int bytes, V128 tb0)
        {
            return TblOrTbx(default, vector, bytes, tb0);
        }

        [UnmanagedCallersOnly]
        public static V128 Tbl2(V128 vector, int bytes, V128 tb0, V128 tb1)
        {
            return TblOrTbx(default, vector, bytes, tb0, tb1);
        }

        [UnmanagedCallersOnly]
        public static V128 Tbl3(V128 vector, int bytes, V128 tb0, V128 tb1, V128 tb2)
        {
            return TblOrTbx(default, vector, bytes, tb0, tb1, tb2);
        }

        [UnmanagedCallersOnly]
        public static V128 Tbl4(V128 vector, int bytes, V128 tb0, V128 tb1, V128 tb2, V128 tb3)
        {
            return TblOrTbx(default, vector, bytes, tb0, tb1, tb2, tb3);
        }

        [UnmanagedCallersOnly]
        public static V128 Tbx1(V128 dest, V128 vector, int bytes, V128 tb0)
        {
            return TblOrTbx(dest, vector, bytes, tb0);
        }

        [UnmanagedCallersOnly]
        public static V128 Tbx2(V128 dest, V128 vector, int bytes, V128 tb0, V128 tb1)
        {
            return TblOrTbx(dest, vector, bytes, tb0, tb1);
        }

        [UnmanagedCallersOnly]
        public static V128 Tbx3(V128 dest, V128 vector, int bytes, V128 tb0, V128 tb1, V128 tb2)
        {
            return TblOrTbx(dest, vector, bytes, tb0, tb1, tb2);
        }

        [UnmanagedCallersOnly]
        public static V128 Tbx4(V128 dest, V128 vector, int bytes, V128 tb0, V128 tb1, V128 tb2, V128 tb3)
        {
            return TblOrTbx(dest, vector, bytes, tb0, tb1, tb2, tb3);
        }

        private static V128 TblOrTbx(V128 dest, V128 vector, int bytes, params ReadOnlySpan<V128> tb)
        {
            byte[] res = new byte[16];

            if (dest != default)
            {
                Buffer.BlockCopy(dest.ToArray(), 0, res, 0, bytes);
            }

            byte[] table = new byte[tb.Length * 16];

            for (byte index = 0; index < tb.Length; index++)
            {
                Buffer.BlockCopy(tb[index].ToArray(), 0, table, index * 16, 16);
            }

            byte[] v = vector.ToArray();

            for (byte index = 0; index < bytes; index++)
            {
                byte tblIndex = v[index];

                if (tblIndex < table.Length)
                {
                    res[index] = table[tblIndex];
                }
            }

            return new V128(res);
        }
    }
}
