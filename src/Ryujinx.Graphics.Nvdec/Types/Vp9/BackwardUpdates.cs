using Ryujinx.Common.Memory;
using Ryujinx.Graphics.Video;
using System;

namespace Ryujinx.Graphics.Nvdec.Types.Vp9
{
    struct BackwardUpdates
    {
        public Array7<Array3<Array2<uint>>> InterModeCounts;
        public Array4<Array10<uint>> YModeCounts;
        public Array10<Array10<uint>> UvModeCounts;
        public Array16<Array4<uint>> PartitionCounts;
        public Array4<Array3<uint>> SwitchableInterpsCount;
        public Array4<Array2<uint>> IntraInterCount;
        public Array5<Array2<uint>> CompInterCount;
        public Array5<Array2<Array2<uint>>> SingleRefCount;
        public Array5<Array2<uint>> CompRefCount;
        public Array2<Array4<uint>> Tx32x32;
        public Array2<Array3<uint>> Tx16x16;
        public Array2<Array2<uint>> Tx8x8;
        public Array3<Array2<uint>> MbSkipCount;
        public Array4<uint> Joints;
        public Array2<Array2<uint>> Sign;
        public Array2<Array11<uint>> Classes;
        public Array2<Array2<uint>> Class0;
        public Array2<Array10<Array2<uint>>> Bits;
        public Array2<Array2<Array4<uint>>> Class0Fp;
        public Array2<Array4<uint>> Fp;
        public Array2<Array2<uint>> Class0Hp;
        public Array2<Array2<uint>> Hp;
        public Array4<Array2<Array2<Array6<Array6<Array4<uint>>>>>> CoefCounts;
        public Array4<Array2<Array2<Array6<Array6<uint>>>>> EobCounts;

        public BackwardUpdates(ref Vp9BackwardUpdates counts)
        {
            InterModeCounts = new Array7<Array3<Array2<uint>>>();
            
            Span<Array3<Array2<uint>>> interModeCountsSpan1 = InterModeCounts.AsSpan();
            Span<Array4<uint>> interModeSpan1 = counts.InterMode.AsSpan();

            for (int i = 0; i < 7; i++)
            {
                Span<Array2<uint>> interModeCountsSpan2 = interModeCountsSpan1[i].AsSpan();
                Span<uint> interModeCountsSpan20 = interModeCountsSpan2[0].AsSpan();
                Span<uint> interModeCountsSpan21 = interModeCountsSpan2[1].AsSpan();
                Span<uint> interModeCountsSpan22 = interModeCountsSpan2[2].AsSpan();
                Span<uint> interModeSpan2 = interModeSpan1[i].AsSpan();
                
                interModeCountsSpan20[0] = interModeSpan2[2];
                interModeCountsSpan20[1] = interModeSpan2[0] + interModeSpan2[1] + interModeSpan2[3];
                interModeCountsSpan21[0] = interModeSpan2[0];
                interModeCountsSpan21[1] = interModeSpan2[1] + interModeSpan2[3];
                interModeCountsSpan22[0] = interModeSpan2[1];
                interModeCountsSpan22[1] = interModeSpan2[3];
            }

            YModeCounts = counts.YMode;
            UvModeCounts = counts.UvMode;
            PartitionCounts = counts.Partition;
            SwitchableInterpsCount = counts.SwitchableInterp;
            IntraInterCount = counts.IntraInter;
            CompInterCount = counts.CompInter;
            SingleRefCount = counts.SingleRef;
            CompRefCount = counts.CompRef;
            Tx32x32 = counts.Tx32x32;
            Tx16x16 = counts.Tx16x16;
            Tx8x8 = counts.Tx8x8;
            MbSkipCount = counts.Skip;
            Joints = counts.Joints;
            Sign = counts.Sign;
            Classes = counts.Classes;
            Class0 = counts.Class0;
            Bits = counts.Bits;
            Class0Fp = counts.Class0Fp;
            Fp = counts.Fp;
            Class0Hp = counts.Class0Hp;
            Hp = counts.Hp;
            CoefCounts = counts.Coef;
            EobCounts = counts.EobBranch;
        }
    }
}
