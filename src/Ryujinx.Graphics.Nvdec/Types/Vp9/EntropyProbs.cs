using Ryujinx.Common.Memory;
using Ryujinx.Graphics.Video;
using System;

namespace Ryujinx.Graphics.Nvdec.Types.Vp9
{
    struct EntropyProbs
    {
#pragma warning disable CS0649 // Field is never assigned to
        public Array10<Array10<Array8<byte>>> KfYModeProbE0ToE7;
        public Array10<Array10<byte>> KfYModeProbE8;
        public Array3<byte> Padding384;
        public Array7<byte> SegTreeProbs;
        public Array3<byte> SegPredProbs;
        public Array15<byte> Padding391;
        public Array10<Array8<byte>> KfUvModeProbE0ToE7;
        public Array10<byte> KfUvModeProbE8;
        public Array6<byte> Padding3FA;
        public Array7<Array4<byte>> InterModeProb;
        public Array4<byte> IntraInterProb;
        public Array10<Array8<byte>> UvModeProbE0ToE7;
        public Array2<Array1<byte>> Tx8x8Prob;
        public Array2<Array2<byte>> Tx16x16Prob;
        public Array2<Array3<byte>> Tx32x32Prob;
        public Array4<byte> YModeProbE8;
        public Array4<Array8<byte>> YModeProbE0ToE7;
        public Array16<Array4<byte>> KfPartitionProb;
        public Array16<Array4<byte>> PartitionProb;
        public Array10<byte> UvModeProbE8;
        public Array4<Array2<byte>> SwitchableInterpProb;
        public Array5<byte> CompInterProb;
        public Array4<byte> SkipProbs;
        public Array3<byte> Joints;
        public Array2<byte> Sign;
        public Array2<Array1<byte>> Class0;
        public Array2<Array3<byte>> Fp;
        public Array2<byte> Class0Hp;
        public Array2<byte> Hp;
        public Array2<Array10<byte>> Classes;
        public Array2<Array2<Array3<byte>>> Class0Fp;
        public Array2<Array10<byte>> Bits;
        public Array5<Array2<byte>> SingleRefProb;
        public Array5<byte> CompRefProb;
        public Array17<byte> Padding58F;
        public Array4<Array2<Array2<Array6<Array6<Array4<byte>>>>>> CoefProbs;
#pragma warning restore CS0649

        public void Convert(ref Vp9EntropyProbs fc)
        {
            Span<Array10<Array9<byte>>> kfYModeProbSpan1 = fc.KfYModeProb.AsSpan();
            Span<Array10<Array8<byte>>> kfYModeProbE0ToE7Span1 = KfYModeProbE0ToE7.AsSpan();
            
            for (int i = 0; i < 10; i++)
            {
                Span<Array9<byte>> kfYModeProbSpan2 = kfYModeProbSpan1[i].AsSpan();
                Span<Array8<byte>> kfYModeProbE0ToE7Span2 = kfYModeProbE0ToE7Span1[i].AsSpan();
                
                for (int j = 0; j < 10; j++)
                {
                    Span<byte> kfYModeProbSpan3 = kfYModeProbSpan2[j].AsSpan();
                    Span<byte> kfYModeProbE0ToE7Span3 = kfYModeProbE0ToE7Span2[j].AsSpan();
                    
                    for (int k = 0; k < 9; k++)
                    {
                        kfYModeProbSpan3[k] = k < 8 ? kfYModeProbE0ToE7Span3[k] : KfYModeProbE8[i][j];
                    }
                }
            }

            fc.SegTreeProb = SegTreeProbs;
            fc.SegPredProb = SegPredProbs;
            
            Span<Array3<byte>> interModeProbSpan1 = fc.InterModeProb.AsSpan();
            Span<Array4<byte>> gInterModeProbSpan1 = InterModeProb.AsSpan();

            for (int i = 0; i < 7; i++)
            {
                Span<byte> interModeProbSpan2 = interModeProbSpan1[i].AsSpan();
                Span<byte> gInterModeProbSpan2 = gInterModeProbSpan1[i].AsSpan();
                
                for (int j = 0; j < 3; j++)
                {
                    interModeProbSpan2[j] = gInterModeProbSpan2[j];
                }
            }

            fc.IntraInterProb = IntraInterProb;

            Span<Array9<byte>> kfUvModeProbSpan1 = fc.KfUvModeProb.AsSpan();
            Span<Array8<byte>> kfUvModeProbE0ToE7Span1 = KfUvModeProbE0ToE7.AsSpan();
            Span<Array9<byte>> uvModeProbSpan1 = fc.UvModeProb.AsSpan();
            Span<Array8<byte>> uvModeProbE0ToE7Span1 = UvModeProbE0ToE7.AsSpan();
            
            for (int i = 0; i < 10; i++)
            {
                Span<byte> kfUvModeProbSpan2 = kfUvModeProbSpan1[i].AsSpan();
                Span<byte> kfUvModeProbE0ToE7Span2 = kfUvModeProbE0ToE7Span1[i].AsSpan();
                Span<byte> uvModeProbSpan2 = uvModeProbSpan1[i].AsSpan();
                Span<byte> uvModeProbE0ToE7Span2 = uvModeProbE0ToE7Span1[i].AsSpan();
                
                for (int j = 0; j < 9; j++)
                {
                    kfUvModeProbSpan2[j] = j < 8 ? kfUvModeProbE0ToE7Span2[j] : KfUvModeProbE8[i];
                    uvModeProbSpan2[j] = j < 8 ? uvModeProbE0ToE7Span2[j] : UvModeProbE8[i];
                }
            }

            fc.Tx8x8Prob = Tx8x8Prob;
            fc.Tx16x16Prob = Tx16x16Prob;
            fc.Tx32x32Prob = Tx32x32Prob;
            
            Span<Array9<byte>> yModeProbSpan1 = fc.YModeProb.AsSpan();
            Span<Array8<byte>> yModeProbE0ToE7Span1 = YModeProbE0ToE7.AsSpan();

            for (int i = 0; i < 4; i++)
            {
                Span<byte> yModeProbSpan2 = yModeProbSpan1[i].AsSpan();
                Span<byte> yModeProbE0ToE7Span2 = yModeProbE0ToE7Span1[i].AsSpan();
                
                for (int j = 0; j < 9; j++)
                {
                    yModeProbSpan2[j] = j < 8 ? yModeProbE0ToE7Span2[j] : YModeProbE8[i];
                }
            }
            
            Span<Array3<byte>> kfPartitionProbSpan1 = fc.KfPartitionProb.AsSpan();
            Span<Array4<byte>> gKfPartitionProbSpan1 = KfPartitionProb.AsSpan();
            Span<Array3<byte>> partitionProbSpan1 = fc.PartitionProb.AsSpan();
            Span<Array4<byte>> gPartitionProbSpan1 = PartitionProb.AsSpan();

            for (int i = 0; i < 16; i++)
            {
                Span<byte> kfPartitionProbSpan2 = kfPartitionProbSpan1[i].AsSpan();
                Span<byte> gKfPartitionProbSpan2 = gKfPartitionProbSpan1[i].AsSpan();
                Span<byte> partitionProbSpan2 = partitionProbSpan1[i].AsSpan();
                Span<byte> gPartitionProbSpan2 = gPartitionProbSpan1[i].AsSpan();
                
                for (int j = 0; j < 3; j++)
                {
                    kfPartitionProbSpan2[j] = gKfPartitionProbSpan2[j];
                    partitionProbSpan2[j] = gPartitionProbSpan2[j];
                }
            }

            fc.SwitchableInterpProb = SwitchableInterpProb;
            fc.CompInterProb = CompInterProb;
            fc.SkipProb[0] = SkipProbs[0];
            fc.SkipProb[1] = SkipProbs[1];
            fc.SkipProb[2] = SkipProbs[2];
            fc.Joints = Joints;
            fc.Sign = Sign;
            fc.Class0 = Class0;
            fc.Fp = Fp;
            fc.Class0Hp = Class0Hp;
            fc.Hp = Hp;
            fc.Classes = Classes;
            fc.Class0Fp = Class0Fp;
            fc.Bits = Bits;
            fc.SingleRefProb = SingleRefProb;
            fc.CompRefProb = CompRefProb;
            
            Span<Array2<Array2<Array6<Array6<Array3<byte>>>>>> coefProbsSpan1 = fc.CoefProbs.AsSpan();
            Span<Array2<Array2<Array6<Array6<Array4<byte>>>>>> gCoefProbsSpan1 = CoefProbs.AsSpan();

            for (int i = 0; i < 4; i++)
            {
                Span<Array2<Array6<Array6<Array3<byte>>>>> coefProbsSpan2 = coefProbsSpan1[i].AsSpan();
                Span<Array2<Array6<Array6<Array4<byte>>>>> gCoefProbsSpan2 = gCoefProbsSpan1[i].AsSpan();
                
                for (int j = 0; j < 2; j++)
                {
                    Span<Array6<Array6<Array3<byte>>>> coefProbsSpan3 = coefProbsSpan2[j].AsSpan();
                    Span<Array6<Array6<Array4<byte>>>> gCoefProbsSpan3 = gCoefProbsSpan2[j].AsSpan();
                    
                    for (int k = 0; k < 2; k++)
                    {
                        Span<Array6<Array3<byte>>> coefProbsSpan4 = coefProbsSpan3[k].AsSpan();
                        Span<Array6<Array4<byte>>> gCoefProbsSpan4 = gCoefProbsSpan3[k].AsSpan();
                        
                        for (int l = 0; l < 6; l++)
                        {
                            Span<Array3<byte>> coefProbsSpan5 = coefProbsSpan4[l].AsSpan();
                            Span<Array4<byte>> gCoefProbsSpan5 = gCoefProbsSpan4[l].AsSpan();
                            
                            for (int m = 0; m < 6; m++)
                            {
                                Span<byte> coefProbsSpan6 = coefProbsSpan5[m].AsSpan();
                                Span<byte> gCoefProbsSpan6 = gCoefProbsSpan5[m].AsSpan();
                                
                                for (int n = 0; n < 3; n++)
                                {
                                    coefProbsSpan6[n] = gCoefProbsSpan6[n];
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
