using ARMeilleure.State;
using Ryujinx.Cpu;
using System;

namespace Ryujinx.HLE.Debugger.Gdb
{
    static class GdbRegisters
    {
        public const int Count64 = 68;
        public const int Count32 = 66;

        /*
        FPCR = FPSR & ~FpcrMask
        All of FPCR's bits are reserved in FPCR and vice versa,
        see ARM's documentation.
        */
        private const uint FpcrMask = 0xfc1fffff;

        #region 64-bit

        public static string ReadRegister64(this IExecutionContext state, int registerId) =>
            registerId switch
            {
                >= 0 and <= 31 => Helpers.ToHex(BitConverter.GetBytes(state.GetX(registerId))),
                32 => Helpers.ToHex(BitConverter.GetBytes(state.DebugPc)),
                33 => Helpers.ToHex(BitConverter.GetBytes(state.Pstate)),
                >= 34 and <= 65 => Helpers.ToHex(state.GetV(registerId - 34).ToArray()),
                66 => Helpers.ToHex(BitConverter.GetBytes(state.Fpsr)),
                67 => Helpers.ToHex(BitConverter.GetBytes(state.Fpcr)),
                _ => null
            };

        public static bool WriteRegister64(this IExecutionContext state, int registerId, StringStream ss)
        {
            switch (registerId)
            {
                case >= 0 and <= 31:
                    {
                        ulong value = ss.ReadLengthAsLittleEndianHex(16);
                        state.SetX(registerId, value);
                        return true;
                    }
                case 32:
                    {
                        ulong value = ss.ReadLengthAsLittleEndianHex(16);
                        state.DebugPc = value;
                        return true;
                    }
                case 33:
                    {
                        ulong value = ss.ReadLengthAsLittleEndianHex(8);
                        state.Pstate = (uint)value;
                        return true;
                    }
                case >= 34 and <= 65:
                    {
                        ulong value0 = ss.ReadLengthAsLittleEndianHex(16);
                        ulong value1 = ss.ReadLengthAsLittleEndianHex(16);
                        state.SetV(registerId - 34, new V128(value0, value1));
                        return true;
                    }
                case 66:
                    {
                        ulong value = ss.ReadLengthAsLittleEndianHex(8);
                        state.Fpsr = (uint)value;
                        return true;
                    }
                case 67:
                    {
                        ulong value = ss.ReadLengthAsLittleEndianHex(8);
                        state.Fpcr = (uint)value;
                        return true;
                    }
                default:
                    return false;
            }
        }

        #endregion

        #region 32-bit

        public static string ReadRegister32(this IExecutionContext state, int registerId)
        {
            switch (registerId)
            {
                case >= 0 and <= 14:
                    return Helpers.ToHex(BitConverter.GetBytes((uint)state.GetX(registerId)));
                case 15:
                    return Helpers.ToHex(BitConverter.GetBytes((uint)state.DebugPc));
                case 16:
                    return Helpers.ToHex(BitConverter.GetBytes(state.Pstate));
                case >= 17 and <= 32:
                    return Helpers.ToHex(state.GetV(registerId - 17).ToArray());
                case >= 33 and <= 64:
                    int reg = (registerId - 33);
                    int n = reg / 2;
                    int shift = reg % 2;
                    ulong value = state.GetV(n).Extract<ulong>(shift);
                    return Helpers.ToHex(BitConverter.GetBytes(value));
                case 65:
                    return Helpers.ToHex(BitConverter.GetBytes(state.Fpscr));
                default:
                    return null;
            }
        }

        public static bool WriteRegister32(this IExecutionContext state, int registerId, StringStream ss)
        {
            switch (registerId)
            {
                case >= 0 and <= 14:
                    {
                        ulong value = ss.ReadLengthAsLittleEndianHex(8);
                        state.SetX(registerId, value);
                        return true;
                    }
                case 15:
                    {
                        ulong value = ss.ReadLengthAsLittleEndianHex(8);
                        state.DebugPc = value;
                        return true;
                    }
                case 16:
                    {
                        ulong value = ss.ReadLengthAsLittleEndianHex(8);
                        state.Pstate = (uint)value;
                        return true;
                    }
                case >= 17 and <= 32:
                    {
                        ulong value0 = ss.ReadLengthAsLittleEndianHex(16);
                        ulong value1 = ss.ReadLengthAsLittleEndianHex(16);
                        state.SetV(registerId - 17, new V128(value0, value1));
                        return true;
                    }
                case >= 33 and <= 64:
                    {
                        ulong value = ss.ReadLengthAsLittleEndianHex(16);
                        int regId = (registerId - 33);
                        int regNum = regId / 2;
                        int shift = regId % 2;
                        V128 reg = state.GetV(regNum);
                        reg.Insert(shift, value);
                        return true;
                    }
                case 65:
                    {
                        ulong value = ss.ReadLengthAsLittleEndianHex(8);
                        state.Fpsr = (uint)value & FpcrMask;
                        state.Fpcr = (uint)value & ~FpcrMask;
                        return true;
                    }
                default:
                    return false;
            }
        }

        #endregion
    }
}
