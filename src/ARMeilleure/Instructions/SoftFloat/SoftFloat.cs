using ARMeilleure.State;
using System;
using System.Diagnostics;

namespace ARMeilleure.Instructions
{
    static class SoftFloat
    {
        static SoftFloat()
        {
            RecipEstimateTable = BuildRecipEstimateTable();
            RecipSqrtEstimateTable = BuildRecipSqrtEstimateTable();
        }

        public static readonly byte[] RecipEstimateTable;
        public static readonly byte[] RecipSqrtEstimateTable;

        private static byte[] BuildRecipEstimateTable()
        {
            byte[] tbl = new byte[256];

            for (int idx = 0; idx < 256; idx++)
            {
                uint src = (uint)idx + 256u;

                Debug.Assert(src is >= 256u and < 512u);

                src = (src << 1) + 1u;

                uint aux = (1u << 19) / src;

                uint dst = (aux + 1u) >> 1;

                Debug.Assert(dst is >= 256u and < 512u);

                tbl[idx] = (byte)(dst - 256u);
            }

            return tbl;
        }

        private static byte[] BuildRecipSqrtEstimateTable()
        {
            byte[] tbl = new byte[384];

            for (int idx = 0; idx < 384; idx++)
            {
                uint src = (uint)idx + 128u;

                Debug.Assert(src is >= 128u and < 512u);

                if (src < 256u)
                {
                    src = (src << 1) + 1u;
                }
                else
                {
                    src = (src >> 1) << 1;
                    src = (src + 1u) << 1;
                }

                uint aux = 512u;

                while (src * (aux + 1u) * (aux + 1u) < (1u << 28))
                {
                    aux++;
                }

                uint dst = (aux + 1u) >> 1;

                Debug.Assert(dst is >= 256u and < 512u);

                tbl[idx] = (byte)(dst - 256u);
            }

            return tbl;
        }

        public static void FPProcessException(FPException exc, ExecutionContext context)
        {
            FPProcessException(exc, context, context.Fpcr);
        }

        public static void FPProcessException(FPException exc, ExecutionContext context, FPCR fpcr)
        {
            int enable = (int)exc + 8;

            if ((fpcr & (FPCR)(1 << enable)) != 0)
            {
                throw new NotImplementedException("Floating-point trap handling.");
            }
            else
            {
                context.Fpsr |= (FPSR)(1 << (int)exc);
            }
        }

        extension(FPCR fpcr)
        {
            public FPRoundingMode RoundingMode 
            {
                get 
                {
                    const int RModeShift = 22;
                    
                    return (FPRoundingMode)(((uint)fpcr >> RModeShift) & 3u);
                }
            }
        }
    }
}
