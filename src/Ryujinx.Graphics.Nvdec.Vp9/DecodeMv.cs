using Ryujinx.Common.Memory;
using Ryujinx.Graphics.Nvdec.Vp9.Dsp;
using Ryujinx.Graphics.Nvdec.Vp9.Types;
using Ryujinx.Graphics.Video;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Ryujinx.Graphics.Nvdec.Vp9
{
    internal static class DecodeMv
    {
        private const int RefNeighbours = 8;

        private static PredictionMode ReadIntraMode(ref Reader r, ReadOnlySpan<byte> p)
        {
            return (PredictionMode)r.ReadTree(Luts.IntraModeTree, p);
        }

        private static PredictionMode ReadIntraModeY(ref Vp9Common cm, ref MacroBlockD xd, ref Reader r, int sizeGroup)
        {
            PredictionMode yMode = ReadIntraMode(ref r, cm.Fc.Value.YModeProb[sizeGroup].AsSpan());
            if (!xd.Counts.IsNull)
            {
                ++xd.Counts.Value.YMode[sizeGroup][(int)yMode];
            }

            return yMode;
        }

        private static PredictionMode ReadIntraModeUv(ref Vp9Common cm, ref MacroBlockD xd, ref Reader r, byte yMode)
        {
            PredictionMode uvMode = ReadIntraMode(ref r, cm.Fc.Value.UvModeProb[yMode].AsSpan());
            if (!xd.Counts.IsNull)
            {
                ++xd.Counts.Value.UvMode[yMode][(int)uvMode];
            }

            return uvMode;
        }

        private static PredictionMode ReadInterMode(ref Vp9Common cm, ref MacroBlockD xd, ref Reader r, int ctx)
        {
            int mode = r.ReadTree(Luts.InterModeTree, cm.Fc.Value.InterModeProb[ctx].AsSpan());
            if (!xd.Counts.IsNull)
            {
                ++xd.Counts.Value.InterMode[ctx][mode];
            }

            return PredictionMode.NearestMv + mode;
        }

        private static int ReadSegmentId(ref Reader r, ReadOnlySpan<byte> segTreeProbs)
        {
            return r.ReadTree(Luts.SegmentTree, segTreeProbs);
        }

        private static ReadOnlySpan<byte> GetTxProbs(ref Vp9EntropyProbs fc, TxSize maxTxSize, int ctx)
        {
            switch (maxTxSize)
            {
                case TxSize.Tx8X8:
                    return fc.Tx8x8Prob[ctx].AsSpan();
                case TxSize.Tx16X16:
                    return fc.Tx16x16Prob[ctx].AsSpan();
                case TxSize.Tx32X32:
                    return fc.Tx32x32Prob[ctx].AsSpan();
                default:
                    Debug.Assert(false, "Invalid maxTxSize.");
                    return ReadOnlySpan<byte>.Empty;
            }
        }

        private static Span<uint> GetTxCounts(ref Vp9BackwardUpdates counts, TxSize maxTxSize, int ctx)
        {
            switch (maxTxSize)
            {
                case TxSize.Tx8X8:
                    return counts.Tx8x8[ctx].AsSpan();
                case TxSize.Tx16X16:
                    return counts.Tx16x16[ctx].AsSpan();
                case TxSize.Tx32X32:
                    return counts.Tx32x32[ctx].AsSpan();
                default:
                    Debug.Assert(false, "Invalid maxTxSize.");
                    return Span<uint>.Empty;
            }
        }

        private static TxSize ReadSelectedTxSize(ref Vp9Common cm, ref MacroBlockD xd, TxSize maxTxSize, ref Reader r)
        {
            int ctx = xd.GetTxSizeContext();
            ReadOnlySpan<byte> txProbs = GetTxProbs(ref cm.Fc.Value, maxTxSize, ctx);
            TxSize txSize = (TxSize)r.Read(txProbs[0]);
            if (txSize != TxSize.Tx4X4 && maxTxSize >= TxSize.Tx16X16)
            {
                txSize += r.Read(txProbs[1]);
                if (txSize != TxSize.Tx8X8 && maxTxSize >= TxSize.Tx32X32)
                {
                    txSize += r.Read(txProbs[2]);
                }
            }

            if (!xd.Counts.IsNull)
            {
                ++GetTxCounts(ref xd.Counts.Value, maxTxSize, ctx)[(int)txSize];
            }

            return txSize;
        }

        private static TxSize ReadTxSize(ref Vp9Common cm, ref MacroBlockD xd, bool allowSelect, ref Reader r)
        {
            TxMode txMode = cm.TxMode;
            BlockSize bsize = xd.Mi[0].Value.SbType;
            TxSize maxTxSize = Luts.MaxTxSizeLookup[(int)bsize];
            if (allowSelect && txMode == TxMode.TxModeSelect && bsize >= BlockSize.Block8X8)
            {
                return ReadSelectedTxSize(ref cm, ref xd, maxTxSize, ref r);
            }

            return (TxSize)Math.Min((int)maxTxSize, (int)Luts.TxModeToBiggestTxSize[(int)txMode]);
        }

        private static int DecGetSegmentId(ref Vp9Common cm, ArrayPtr<byte> segmentIds, int miOffset, int xMis,
            int yMis)
        {
            int segmentId = int.MaxValue;

            for (int y = 0; y < yMis; y++)
            {
                for (int x = 0; x < xMis; x++)
                {
                    segmentId = Math.Min(segmentId, segmentIds[miOffset + (y * cm.MiCols) + x]);
                }
            }

            Debug.Assert(segmentId is >= 0 and < Constants.MaxSegments);
            return segmentId;
        }

        private static void SetSegmentId(ref Vp9Common cm, int miOffset, int xMis, int yMis, int segmentId)
        {
            Debug.Assert(segmentId is >= 0 and < Constants.MaxSegments);

            for (int y = 0; y < yMis; y++)
            {
                for (int x = 0; x < xMis; x++)
                {
                    cm.CurrentFrameSegMap[miOffset + (y * cm.MiCols) + x] = (byte)segmentId;
                }
            }
        }

        private static void CopySegmentId(
            ref Vp9Common cm,
            ArrayPtr<byte> lastSegmentIds,
            ArrayPtr<byte> currentSegmentIds,
            int miOffset,
            int xMis,
            int yMis)
        {
            for (int y = 0; y < yMis; y++)
            {
                for (int x = 0; x < xMis; x++)
                {
                    currentSegmentIds[miOffset + (y * cm.MiCols) + x] = (byte)(!lastSegmentIds.IsNull
                        ? lastSegmentIds[miOffset + (y * cm.MiCols) + x]
                        : 0);
                }
            }
        }

        private static int ReadIntraSegmentId(ref Vp9Common cm, int miOffset, int xMis, int yMis, ref Reader r)
        {
            ref Segmentation seg = ref cm.Seg;
            int segmentId;

            if (!seg.Enabled)
            {
                return 0; // Default for disabled segmentation
            }

            if (!seg.UpdateMap)
            {
                CopySegmentId(ref cm, cm.LastFrameSegMap, cm.CurrentFrameSegMap, miOffset, xMis, yMis);
                return 0;
            }

            segmentId = ReadSegmentId(ref r, cm.Fc.Value.SegTreeProb.AsSpan());
            SetSegmentId(ref cm, miOffset, xMis, yMis, segmentId);
            return segmentId;
        }

        private static int ReadInterSegmentId(
            ref Vp9Common cm,
            ref MacroBlockD xd,
            int miRow,
            int miCol,
            ref Reader r,
            int xMis,
            int yMis)
        {
            ref Segmentation seg = ref cm.Seg;
            ref ModeInfo mi = ref xd.Mi[0].Value;
            int predictedSegmentId, segmentId;
            int miOffset = (miRow * cm.MiCols) + miCol;

            if (!seg.Enabled)
            {
                return 0; // Default for disabled segmentation
            }

            predictedSegmentId = !cm.LastFrameSegMap.IsNull
                ? DecGetSegmentId(ref cm, cm.LastFrameSegMap, miOffset, xMis, yMis)
                : 0;

            if (!seg.UpdateMap)
            {
                CopySegmentId(ref cm, cm.LastFrameSegMap, cm.CurrentFrameSegMap, miOffset, xMis, yMis);
                return predictedSegmentId;
            }

            if (seg.TemporalUpdate)
            {
                byte predProb = Segmentation.GetPredProbSegId(cm.Fc.Value.SegPredProb.AsSpan(), ref xd);
                mi.SegIdPredicted = (sbyte)r.Read(predProb);
                segmentId = mi.SegIdPredicted != 0
                    ? predictedSegmentId
                    : ReadSegmentId(ref r, cm.Fc.Value.SegTreeProb.AsSpan());
            }
            else
            {
                segmentId = ReadSegmentId(ref r, cm.Fc.Value.SegTreeProb.AsSpan());
            }

            SetSegmentId(ref cm, miOffset, xMis, yMis, segmentId);
            return segmentId;
        }

        private static int ReadSkip(ref Vp9Common cm, ref MacroBlockD xd, int segmentId, ref Reader r)
        {
            if (cm.Seg.IsSegFeatureActive(segmentId, SegLvlFeatures.Skip) != 0)
            {
                return 1;
            }

            int ctx = xd.GetSkipContext();
            int skip = r.Read(cm.Fc.Value.SkipProb[ctx]);
            if (!xd.Counts.IsNull)
            {
                ++xd.Counts.Value.Skip[ctx][skip];
            }

            return skip;
        }

        private static int ReadComponent(ref Reader r, ref Vp9EntropyProbs fc, int mvcomp, bool usehp)
        {
            int mag, d, fr, hp;
            bool sign = r.Read(fc.Sign[mvcomp]) != 0;
            MvClassType mvClass = (MvClassType)r.ReadTree(Luts.MvClassTree, fc.Classes[mvcomp].AsSpan());
            bool class0 = mvClass == MvClassType.Class0;

            // Integer part
            if (class0)
            {
                d = r.Read(fc.Class0[mvcomp][0]);
                mag = 0;
            }
            else
            {
                int n = (int)mvClass + Constants.Class0Bits - 1; // Number of bits

                Span<byte> bitsSpan = fc.Bits[mvcomp].AsSpan();
                
                d = 0;
                for (int i = 0; i < n; ++i)
                {
                    d |= r.Read(bitsSpan[i]) << i;
                }

                mag = Constants.Class0Size << ((int)mvClass + 2);
            }

            // Fractional part
            fr = r.ReadTree(Luts.MvFpTree, class0 ? fc.Class0Fp[mvcomp][d].AsSpan() : fc.Fp[mvcomp].AsSpan());

            // High precision part (if hp is not used, the default value of the hp is 1)
            hp = usehp ? r.Read(class0 ? fc.Class0Hp[mvcomp] : fc.Hp[mvcomp]) : 1;

            // Result
            mag += ((d << 3) | (fr << 1) | hp) + 1;
            return sign ? -mag : mag;
        }

        private static void Read(
            ref Reader r,
            ref Mv mv,
            ref Mv refr,
            ref Vp9EntropyProbs fc,
            Ptr<Vp9BackwardUpdates> counts,
            bool allowHp)
        {
            MvJointType jointType = (MvJointType)r.ReadTree(Luts.MvJointTree, fc.Joints.AsSpan());
            bool useHp = allowHp && refr.UseHp();
            Mv diff = new();

            if (Mv.JointVertical(jointType))
            {
                diff.Row = (short)ReadComponent(ref r, ref fc, 0, useHp);
            }

            if (Mv.JointHorizontal(jointType))
            {
                diff.Col = (short)ReadComponent(ref r, ref fc, 1, useHp);
            }

            diff.Inc(counts);

            mv.Row = (short)(refr.Row + diff.Row);
            mv.Col = (short)(refr.Col + diff.Col);
        }

        private static ReferenceMode ReadBlockReferenceMode(ref Vp9Common cm, ref MacroBlockD xd, ref Reader r)
        {
            if (cm.ReferenceMode == ReferenceMode.Select)
            {
                int ctx = PredCommon.GetReferenceModeContext(ref cm, ref xd);
                ReferenceMode mode = (ReferenceMode)r.Read(cm.Fc.Value.CompInterProb[ctx]);
                if (!xd.Counts.IsNull)
                {
                    ++xd.Counts.Value.CompInter[ctx][(int)mode];
                }

                return mode; // SingleReference or CompoundReference
            }

            return cm.ReferenceMode;
        }

        // Read the referncence frame
        private static void ReadRefFrames(
            ref Vp9Common cm,
            ref MacroBlockD xd,
            ref Reader r,
            int segmentId,
            Span<sbyte> refFrame)
        {
            ref Vp9EntropyProbs fc = ref cm.Fc.Value;

            if (cm.Seg.IsSegFeatureActive(segmentId, SegLvlFeatures.RefFrame) != 0)
            {
                refFrame[0] = (sbyte)cm.Seg.GetSegData(segmentId, SegLvlFeatures.RefFrame);
                refFrame[1] = Constants.None;
            }
            else
            {
                ReferenceMode mode = ReadBlockReferenceMode(ref cm, ref xd, ref r);
                if (mode == ReferenceMode.Compound)
                {
                    int idx = cm.RefFrameSignBias[cm.CompFixedRef];
                    int ctx = PredCommon.GetPredContextCompRefP(ref cm, ref xd);
                    int bit = r.Read(fc.CompRefProb[ctx]);
                    if (!xd.Counts.IsNull)
                    {
                        ++xd.Counts.Value.CompRef[ctx][bit];
                    }

                    refFrame[idx] = cm.CompFixedRef;
                    refFrame[idx == 0 ? 1 : 0] = cm.CompVarRef[bit];
                }
                else if (mode == ReferenceMode.Single)
                {
                    int ctx0 = PredCommon.GetPredContextSingleRefP1(ref xd);
                    int bit0 = r.Read(fc.SingleRefProb[ctx0][0]);
                    if (!xd.Counts.IsNull)
                    {
                        ++xd.Counts.Value.SingleRef[ctx0][0][bit0];
                    }

                    if (bit0 != 0)
                    {
                        int ctx1 = PredCommon.GetPredContextSingleRefP2(ref xd);
                        int bit1 = r.Read(fc.SingleRefProb[ctx1][1]);
                        if (!xd.Counts.IsNull)
                        {
                            ++xd.Counts.Value.SingleRef[ctx1][1][bit1];
                        }

                        refFrame[0] = (sbyte)(bit1 != 0 ? Constants.AltRefFrame : Constants.GoldenFrame);
                    }
                    else
                    {
                        refFrame[0] = Constants.LastFrame;
                    }

                    refFrame[1] = Constants.None;
                }
                else
                {
                    Debug.Assert(false, "Invalid prediction mode.");
                }
            }
        }

        private static byte ReadSwitchableInterpFilter(ref Vp9Common cm, ref MacroBlockD xd, ref Reader r)
        {
            int ctx = xd.GetPredContextSwitchableInterp();
            byte type = (byte)r.ReadTree(Luts.SwitchableInterpTree, cm.Fc.Value.SwitchableInterpProb[ctx].AsSpan());
            if (!xd.Counts.IsNull)
            {
                ++xd.Counts.Value.SwitchableInterp[ctx][type];
            }

            return type;
        }

        private static void ReadIntraBlockModeInfo(ref Vp9Common cm, ref MacroBlockD xd, ref ModeInfo mi, ref Reader r)
        {
            BlockSize bsize = mi.SbType;

            Span<BModeInfo> bmiSpan = mi.Bmi.AsSpan();

            switch (bsize)
            {
                case BlockSize.Block4X4:
                    for (int i = 0; i < 4; ++i)
                    {
                        bmiSpan[i].Mode = ReadIntraModeY(ref cm, ref xd, ref r, 0);
                    }

                    mi.Mode = bmiSpan[3].Mode;
                    break;
                case BlockSize.Block4X8:
                    bmiSpan[0].Mode = bmiSpan[2].Mode = ReadIntraModeY(ref cm, ref xd, ref r, 0);
                    bmiSpan[1].Mode = bmiSpan[3].Mode = mi.Mode = ReadIntraModeY(ref cm, ref xd, ref r, 0);
                    break;
                case BlockSize.Block8X4:
                    bmiSpan[0].Mode = bmiSpan[1].Mode = ReadIntraModeY(ref cm, ref xd, ref r, 0);
                    bmiSpan[2].Mode = bmiSpan[3].Mode = mi.Mode = ReadIntraModeY(ref cm, ref xd, ref r, 0);
                    break;
                default:
                    mi.Mode = ReadIntraModeY(ref cm, ref xd, ref r, Luts.SizeGroupLookup[(int)bsize]);
                    break;
            }

            mi.UvMode = ReadIntraModeUv(ref cm, ref xd, ref r, (byte)mi.Mode);

            // Initialize interp_filter here so we do not have to check for inter block
            // modes in GetPredContextSwitchableInterp()
            mi.InterpFilter = Constants.SwitchableFilters;

            Span<sbyte> refFrameSpan = mi.RefFrame.AsSpan();
            
            refFrameSpan[0] = Constants.IntraFrame;
            refFrameSpan[1] = Constants.None;
        }

        private static bool Assign(
            ref Vp9Common cm,
            ref MacroBlockD xd,
            PredictionMode mode,
            Span<Mv> mv,
            Span<Mv> refMv,
            Span<Mv> nearNearestMv,
            int isCompound,
            bool allowHp,
            ref Reader r)
        {
            bool ret = true;

            switch (mode)
            {
                case PredictionMode.NewMv:
                    {
                        for (int i = 0; i < 1 + isCompound; ++i)
                        {
                            Read(ref r, ref mv[i], ref refMv[i], ref cm.Fc.Value, xd.Counts, allowHp);
                            ret = ret && mv[i].IsValid();
                        }

                        break;
                    }
                case PredictionMode.NearMv:
                case PredictionMode.NearestMv:
                    {
                        nearNearestMv.CopyTo(mv);
                        break;
                    }
                case PredictionMode.ZeroMv:
                    {
                        mv.Clear();
                        break;
                    }
                default:
                    return false;
            }

            return ret;
        }

        private static bool ReadIsInterBlock(ref Vp9Common cm, ref MacroBlockD xd, int segmentId, ref Reader r)
        {
            if (cm.Seg.IsSegFeatureActive(segmentId, SegLvlFeatures.RefFrame) != 0)
            {
                return cm.Seg.GetSegData(segmentId, SegLvlFeatures.RefFrame) != Constants.IntraFrame;
            }

            int ctx = xd.GetIntraInterContext();
            bool isInter = r.Read(cm.Fc.Value.IntraInterProb[ctx]) != 0;
            if (!xd.Counts.IsNull)
            {
                ++xd.Counts.Value.IntraInter[ctx][isInter ? 1 : 0];
            }

            return isInter;
        }

        private static void DecFindBestRefs(bool allowHp, Span<Mv> mvlist, ref Mv bestMv, int refmvCount)
        {
            // Make sure all the candidates are properly clamped etc
            for (int i = 0; i < refmvCount; ++i)
            {
                mvlist[i].LowerPrecision(allowHp);
                bestMv = mvlist[i];
            }
        }

        private static bool AddRefListEb(Mv mv, ref int refCount, Span<Mv> mvRefList, bool earlyBreak)
        {
            if (refCount != 0)
            {
                if (Unsafe.As<Mv, int>(ref mv) != Unsafe.As<Mv, int>(ref mvRefList[0]))
                {
                    mvRefList[refCount] = mv;
                    refCount++;
                    return true;
                }
            }
            else
            {
                mvRefList[refCount++] = mv;
                if (earlyBreak)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDiffRefFrameAddEb(
            ref ModeInfo mbmi,
            sbyte refFrame,
            Span<sbyte> refSignBias,
            ref int refmvCount,
            Span<Mv> mvRefList,
            bool earlyBreak)
        {
            if (mbmi.IsInterBlock())
            {
                Span<sbyte> refFrameSpan = mbmi.RefFrame.AsSpan();
                Span<Mv> mvSpan = mbmi.Mv.AsSpan();
                
                if (refFrameSpan[0] != refFrame)
                {
                    if (AddRefListEb(mbmi.ScaleMv(0, refFrame, refSignBias), ref refmvCount, mvRefList,
                            earlyBreak))
                    {
                        return true;
                    }
                }

                if (mbmi.HasSecondRef() && refFrameSpan[1] != refFrame &&
                    Unsafe.As<Mv, int>(ref mvSpan[1]) != Unsafe.As<Mv, int>(ref mvSpan[0]))
                {
                    if (AddRefListEb(mbmi.ScaleMv(1, refFrame, refSignBias), ref refmvCount, mvRefList,
                            earlyBreak))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // This function searches the neighborhood of a given MB/SB
        // to try and find candidate reference vectors.
        private static int DecFindRefs(
            ref Vp9Common cm,
            ref MacroBlockD xd,
            PredictionMode mode,
            sbyte refFrame,
            Span<Position> mvRefSearch,
            Span<Mv> mvRefList,
            int miRow,
            int miCol,
            int block,
            int isSub8X8)
        {
            Span<sbyte> refSignBias = cm.RefFrameSignBias.AsSpan();
            int i, refmvCount = 0;
            bool differentRefFound = false;
            Ptr<MvRef> prevFrameMvs = cm.UsePrevFrameMvs
                ? new Ptr<MvRef>(ref cm.PrevFrameMvs[(miRow * cm.MiCols) + miCol])
                : Ptr<MvRef>.Null;
            ref TileInfo tile = ref xd.Tile;
            // If mode is nearestmv or newmv (uses nearestmv as a reference) then stop
            // searching after the first mv is found.
            bool earlyBreak = mode != PredictionMode.NearMv;

            // Blank the reference vector list
            mvRefList[..Constants.MaxMvRefCandidates].Clear();
            
            i = 0;
            if (isSub8X8 != 0)
            {
                // If the size < 8x8 we get the mv from the bmi substructure for the
                // nearest two blocks.
                for (i = 0; i < 2; ++i)
                {
                    ref Position mvRef = ref mvRefSearch[i];
                    if (tile.IsInside(miCol, miRow, cm.MiRows, ref mvRef))
                    {
                        ref ModeInfo candidateMi = ref xd.Mi[mvRef.Col + (mvRef.Row * xd.MiStride)].Value;
                        differentRefFound = true;

                        Span<sbyte> refFrameSpan = candidateMi.RefFrame.AsSpan();
                        
                        if (refFrameSpan[0] == refFrame)
                        {
                            if (AddRefListEb(candidateMi.GetSubBlockMv(0, mvRef.Col, block), ref refmvCount,
                                    mvRefList, earlyBreak))
                            {
                                goto Done;
                            }
                        }
                        else if (refFrameSpan[1] == refFrame)
                        {
                            if (AddRefListEb(candidateMi.GetSubBlockMv(1, mvRef.Col, block), ref refmvCount,
                                    mvRefList, earlyBreak))
                            {
                                goto Done;
                            }
                        }
                    }
                }
            }

            // Check the rest of the neighbors in much the same way
            // as before except we don't need to keep track of sub blocks or
            // mode counts.
            for (; i < RefNeighbours; ++i)
            {
                ref Position mvRef = ref mvRefSearch[i];
                if (tile.IsInside(miCol, miRow, cm.MiRows, ref mvRef))
                {
                    ref ModeInfo candidate = ref xd.Mi[mvRef.Col + (mvRef.Row * xd.MiStride)].Value;
                    differentRefFound = true;
                    
                    Span<sbyte> refFrameSpan = candidate.RefFrame.AsSpan();
                    Span<Mv> mvSpan = candidate.Mv.AsSpan();

                    if (refFrameSpan[0] == refFrame)
                    {
                        if (AddRefListEb(mvSpan[0], ref refmvCount, mvRefList, earlyBreak))
                        {
                            goto Done;
                        }
                    }
                    else if (refFrameSpan[1] == refFrame)
                    {
                        if (AddRefListEb(mvSpan[1], ref refmvCount, mvRefList, earlyBreak))
                        {
                            goto Done;
                        }
                    }
                }
            }

            // Check the last frame's mode and mv info.
            if (!prevFrameMvs.IsNull)
            {
                Span<sbyte> refFrameSpan = prevFrameMvs.Value.RefFrame.AsSpan();
                Span<Mv> mvSpan = prevFrameMvs.Value.Mv.AsSpan();
                
                if (refFrameSpan[0] == refFrame)
                {
                    if (AddRefListEb(mvSpan[0], ref refmvCount, mvRefList, earlyBreak))
                    {
                        goto Done;
                    }
                }
                else if (refFrameSpan[1] == refFrame)
                {
                    if (AddRefListEb(mvSpan[1], ref refmvCount, mvRefList, earlyBreak))
                    {
                        goto Done;
                    }
                }
            }

            // Since we couldn't find 2 mvs from the same reference frame
            // go back through the neighbors and find motion vectors from
            // different reference frames.
            if (differentRefFound)
            {
                for (i = 0; i < RefNeighbours; ++i)
                {
                    ref Position mvRef = ref mvRefSearch[i];
                    if (tile.IsInside(miCol, miRow, cm.MiRows, ref mvRef))
                    {
                        ref ModeInfo candidate = ref xd.Mi[mvRef.Col + (mvRef.Row * xd.MiStride)].Value;

                        // If the candidate is Intra we don't want to consider its mv.
                        if (IsDiffRefFrameAddEb(ref candidate, refFrame, refSignBias, ref refmvCount, mvRefList,
                                earlyBreak))
                        {
                            goto Done;
                        }
                    }
                }
            }

            // Since we still don't have a candidate we'll try the last frame.
            if (!prevFrameMvs.IsNull)
            {
                Span<sbyte> refFrameSpan = prevFrameMvs.Value.RefFrame.AsSpan();
                Span<Mv> mvSpan = prevFrameMvs.Value.Mv.AsSpan();
                
                if (refFrameSpan[0] != refFrame && refFrameSpan[0] > Constants.IntraFrame)
                {
                    Mv mv = mvSpan[0];
                    if (refSignBias[refFrameSpan[0]] != refSignBias[refFrame])
                    {
                        mv.Row *= -1;
                        mv.Col *= -1;
                    }

                    if (AddRefListEb(mv, ref refmvCount, mvRefList, earlyBreak))
                    {
                        goto Done;
                    }
                }

                if (refFrameSpan[1] > Constants.IntraFrame &&
                    refFrameSpan[1] != refFrame &&
                    Unsafe.As<Mv, int>(ref mvSpan[1]) !=
                    Unsafe.As<Mv, int>(ref mvSpan[0]))
                {
                    Mv mv = mvSpan[1];
                    if (refSignBias[refFrameSpan[1]] != refSignBias[refFrame])
                    {
                        mv.Row *= -1;
                        mv.Col *= -1;
                    }

                    if (AddRefListEb(mv, ref refmvCount, mvRefList, earlyBreak))
                    {
                        goto Done;
                    }
                }
            }

            if (mode == PredictionMode.NearMv)
            {
                refmvCount = Constants.MaxMvRefCandidates;
            }
            else
            {
                // We only care about the nearestmv for the remaining modes
                refmvCount = 1;
            }

        Done:
            // Clamp vectors
            for (i = 0; i < refmvCount; ++i)
            {
                mvRefList[i].ClampRef(ref xd);
            }

            return refmvCount;
        }

        private static void AppendSub8X8ForIdx(
            ref Vp9Common cm,
            ref MacroBlockD xd,
            Span<Position> mvRefSearch,
            PredictionMode bMode,
            int block,
            int refr,
            int miRow,
            int miCol,
            ref Mv bestSub8X8)
        {
            Span<Mv> mvList = stackalloc Mv[Constants.MaxMvRefCandidates];
            ref ModeInfo mi = ref xd.Mi[0].Value;
            Span<BModeInfo> bmi = mi.Bmi.AsSpan();
            int refmvCount;

            Debug.Assert(Constants.MaxMvRefCandidates == 2);

            refmvCount = DecFindRefs(ref cm, ref xd, bMode, mi.RefFrame[refr], mvRefSearch, mvList, miRow, miCol,
                block, 1);

            switch (block)
            {
                case 0:
                    bestSub8X8 = mvList[refmvCount - 1];
                    break;
                case 1:
                case 2:
                    if (bMode == PredictionMode.NearestMv)
                    {
                        bestSub8X8 = bmi[0].Mv[refr];
                    }
                    else
                    {
                        bestSub8X8 = new Mv();
                        for (int n = 0; n < refmvCount; ++n)
                        {
                            if (Unsafe.As<Mv, int>(ref bmi[0].Mv[refr]) != Unsafe.As<Mv, int>(ref mvList[n]))
                            {
                                bestSub8X8 = mvList[n];
                                break;
                            }
                        }
                    }

                    break;
                case 3:
                    if (bMode == PredictionMode.NearestMv)
                    {
                        bestSub8X8 = bmi[2].Mv[refr];
                    }
                    else
                    {
                        Span<Mv> candidates = stackalloc Mv[2 + Constants.MaxMvRefCandidates];
                        candidates[0] = bmi[1].Mv[refr];
                        candidates[1] = bmi[0].Mv[refr];
                        candidates[2] = mvList[0];
                        candidates[3] = mvList[1];
                        bestSub8X8 = new Mv();
                        for (int n = 0; n < 2 + Constants.MaxMvRefCandidates; ++n)
                        {
                            if (Unsafe.As<Mv, int>(ref bmi[2].Mv[refr]) != Unsafe.As<Mv, int>(ref candidates[n]))
                            {
                                bestSub8X8 = candidates[n];
                                break;
                            }
                        }
                    }

                    break;
                default:
                    Debug.Assert(false, "Invalid block index.");
                    break;
            }
        }

        private static byte GetModeContext(ref Vp9Common cm, ref MacroBlockD xd, Span<Position> mvRefSearch, int miRow,
            int miCol)
        {
            int contextCounter = 0;
            ref TileInfo tile = ref xd.Tile;

            // Get mode count from nearest 2 blocks
            for (int i = 0; i < 2; ++i)
            {
                ref Position mvRef = ref mvRefSearch[i];
                if (tile.IsInside(miCol, miRow, cm.MiRows, ref mvRef))
                {
                    ref ModeInfo candidate = ref xd.Mi[mvRef.Col + (mvRef.Row * xd.MiStride)].Value;
                    // Keep counts for entropy encoding.
                    contextCounter += Luts.Mode2Counter[(int)candidate.Mode];
                }
            }

            return (byte)Luts.CounterToContext[contextCounter];
        }

        private static void ReadInterBlockModeInfo(
            ref Vp9Common cm,
            ref MacroBlockD xd,
            ref ModeInfo mi,
            int miRow,
            int miCol,
            ref Reader r)
        {
            BlockSize bsize = mi.SbType;
            bool allowHp = cm.AllowHighPrecisionMv;
            Array2<Mv> bestRefMvs = new();
            Span<Mv> bestRefMvsSpan = bestRefMvs.AsSpan();
            int refr, isCompound;
            byte interModeCtx;
            Span<Position> mvRefSearch = Luts.MvRefBlocks[(int)bsize];

            ReadRefFrames(ref cm, ref xd, ref r, mi.SegmentId, mi.RefFrame.AsSpan());
            isCompound = mi.HasSecondRef() ? 1 : 0;
            interModeCtx = GetModeContext(ref cm, ref xd, mvRefSearch, miRow, miCol);

            if (cm.Seg.IsSegFeatureActive(mi.SegmentId, SegLvlFeatures.Skip) != 0)
            {
                mi.Mode = PredictionMode.ZeroMv;
                if (bsize < BlockSize.Block8X8)
                {
                    xd.ErrorInfo.Value.InternalError(CodecErr.UnsupBitstream,
                        "Invalid usage of segement feature on small blocks");
                    return;
                }
            }
            else
            {
                if (bsize >= BlockSize.Block8X8)
                {
                    mi.Mode = ReadInterMode(ref cm, ref xd, ref r, interModeCtx);
                }
                else
                {
                    // Sub 8x8 blocks use the nearestmv as a ref_mv if the bMode is NewMv.
                    // Setting mode to NearestMv forces the search to stop after the nearestmv
                    // has been found. After bModes have been read, mode will be overwritten
                    // by the last bMode.
                    mi.Mode = PredictionMode.NearestMv;
                }

                if (mi.Mode != PredictionMode.ZeroMv)
                {
                    Span<Mv> tmpMvs = stackalloc Mv[Constants.MaxMvRefCandidates];
                    Span<sbyte> refFrameSpan = mi.RefFrame.AsSpan();

                    for (refr = 0; refr < 1 + isCompound; ++refr)
                    {
                        sbyte frame = refFrameSpan[refr];
                        int refmvCount;

                        refmvCount = DecFindRefs(ref cm, ref xd, mi.Mode, frame, mvRefSearch, tmpMvs, miRow, miCol,
                            -1, 0);

                        DecFindBestRefs(allowHp, tmpMvs, ref bestRefMvsSpan[refr], refmvCount);
                    }
                }
            }

            mi.InterpFilter = cm.InterpFilter == Constants.Switchable
                ? ReadSwitchableInterpFilter(ref cm, ref xd, ref r)
                : cm.InterpFilter;

            if (bsize < BlockSize.Block8X8)
            {
                int num4X4W = 1 << xd.BmodeBlocksWl;
                int num4X4H = 1 << xd.BmodeBlocksHl;
                int idx, idy;
                PredictionMode bMode = 0;
                Array2<Mv> bestSub8X8 = new();
                Span<Mv> bestSub8X8Span = bestSub8X8.AsSpan();
                Span<BModeInfo> bmiSpan = mi.Bmi.AsSpan();
                const uint InvalidMv = 0x80008000;
                // Initialize the 2nd element as even though it won't be used meaningfully
                // if isCompound is false.
                Unsafe.As<Mv, uint>(ref bestSub8X8Span[1]) = InvalidMv;
                for (idy = 0; idy < 2; idy += num4X4H)
                {
                    for (idx = 0; idx < 2; idx += num4X4W)
                    {
                        int j = (idy * 2) + idx;
                        bMode = ReadInterMode(ref cm, ref xd, ref r, interModeCtx);

                        if (bMode is PredictionMode.NearestMv or PredictionMode.NearMv)
                        {
                            for (refr = 0; refr < 1 + isCompound; ++refr)
                            {
                                AppendSub8X8ForIdx(ref cm, ref xd, mvRefSearch, bMode, j, refr, miRow, miCol,
                                    ref bestSub8X8Span[refr]);
                            }
                        }

                        if (!Assign(ref cm, ref xd, bMode, bmiSpan[j].Mv.AsSpan(), bestRefMvs.AsSpan(), bestSub8X8.AsSpan(),
                                isCompound, allowHp, ref r))
                        {
                            xd.Corrupted |= true;
                            break;
                        }

                        if (num4X4H == 2)
                        {
                            bmiSpan[j + 2] = bmiSpan[j];
                        }

                        if (num4X4W == 2)
                        {
                            bmiSpan[j + 1] = bmiSpan[j];
                        }
                    }
                }

                mi.Mode = bMode;
                bmiSpan[3].Mv.AsSpan().CopyTo(mi.Mv.AsSpan());
            }
            else
            {
                xd.Corrupted |= !Assign(ref cm, ref xd, mi.Mode, mi.Mv.AsSpan(), bestRefMvs.AsSpan(), bestRefMvs.AsSpan(),
                    isCompound, allowHp, ref r);
            }
        }

        private static void ReadInterFrameModeInfo(
            ref Vp9Common cm,
            ref MacroBlockD xd,
            int miRow,
            int miCol,
            ref Reader r,
            int xMis,
            int yMis)
        {
            ref ModeInfo mi = ref xd.Mi[0].Value;
            bool interBlock;

            mi.SegmentId = (sbyte)ReadInterSegmentId(ref cm, ref xd, miRow, miCol, ref r, xMis, yMis);
            mi.Skip = (sbyte)ReadSkip(ref cm, ref xd, mi.SegmentId, ref r);
            interBlock = ReadIsInterBlock(ref cm, ref xd, mi.SegmentId, ref r);
            mi.TxSize = ReadTxSize(ref cm, ref xd, mi.Skip == 0 || !interBlock, ref r);

            if (interBlock)
            {
                ReadInterBlockModeInfo(ref cm, ref xd, ref mi, miRow, miCol, ref r);
            }
            else
            {
                ReadIntraBlockModeInfo(ref cm, ref xd, ref mi, ref r);
            }
        }

        private static PredictionMode LeftBlockMode(Ptr<ModeInfo> curMi, Ptr<ModeInfo> leftMi, int b)
        {
            if (b is 0 or 2)
            {
                if (leftMi.IsNull || leftMi.Value.IsInterBlock())
                {
                    return PredictionMode.DcPred;
                }

                return leftMi.Value.GetYMode(b + 1);
            }

            Debug.Assert(b is 1 or 3);
            return curMi.Value.Bmi[b - 1].Mode;
        }

        private static PredictionMode AboveBlockMode(Ptr<ModeInfo> curMi, Ptr<ModeInfo> aboveMi, int b)
        {
            if (b is 0 or 1)
            {
                if (aboveMi.IsNull || aboveMi.Value.IsInterBlock())
                {
                    return PredictionMode.DcPred;
                }

                return aboveMi.Value.GetYMode(b + 2);
            }

            Debug.Assert(b is 2 or 3);
            return curMi.Value.Bmi[b - 2].Mode;
        }

        private static ReadOnlySpan<byte> GetYModeProbs(
            ref Vp9EntropyProbs fc,
            Ptr<ModeInfo> mi,
            Ptr<ModeInfo> aboveMi,
            Ptr<ModeInfo> leftMi,
            int block)
        {
            PredictionMode above = AboveBlockMode(mi, aboveMi, block);
            PredictionMode left = LeftBlockMode(mi, leftMi, block);
            return fc.KfYModeProb[(int)above][(int)left].AsSpan();
        }

        private static void ReadIntraFrameModeInfo(
            ref Vp9Common cm,
            ref MacroBlockD xd,
            int miRow,
            int miCol,
            ref Reader r,
            int xMis,
            int yMis)
        {
            Ptr<ModeInfo> mi = xd.Mi[0];
            Ptr<ModeInfo> aboveMi = xd.AboveMi;
            Ptr<ModeInfo> leftMi = xd.LeftMi;
            BlockSize bsize = mi.Value.SbType;

            int miOffset = (miRow * cm.MiCols) + miCol;

            Span<sbyte> refFrameSpan = mi.Value.RefFrame.AsSpan();

            mi.Value.SegmentId = (sbyte)ReadIntraSegmentId(ref cm, miOffset, xMis, yMis, ref r);
            mi.Value.Skip = (sbyte)ReadSkip(ref cm, ref xd, mi.Value.SegmentId, ref r);
            mi.Value.TxSize = ReadTxSize(ref cm, ref xd, true, ref r);
            refFrameSpan[0] = Constants.IntraFrame;
            refFrameSpan[1] = Constants.None;

            Span<BModeInfo> bmiSpan;
            switch (bsize)
            {
                case BlockSize.Block4X4:
                    bmiSpan = mi.Value.Bmi.AsSpan();
                    
                    for (int i = 0; i < 4; ++i)
                    {
                        bmiSpan[i].Mode =
                            ReadIntraMode(ref r, GetYModeProbs(ref cm.Fc.Value, mi, aboveMi, leftMi, i));
                    }

                    mi.Value.Mode = bmiSpan[3].Mode;
                    break;
                case BlockSize.Block4X8:
                    bmiSpan = mi.Value.Bmi.AsSpan();
                    
                    bmiSpan[0].Mode = bmiSpan[2].Mode =
                        ReadIntraMode(ref r, GetYModeProbs(ref cm.Fc.Value, mi, aboveMi, leftMi, 0));
                    bmiSpan[1].Mode = bmiSpan[3].Mode = mi.Value.Mode =
                        ReadIntraMode(ref r, GetYModeProbs(ref cm.Fc.Value, mi, aboveMi, leftMi, 1));
                    break;
                case BlockSize.Block8X4:
                    bmiSpan = mi.Value.Bmi.AsSpan();
                    
                    bmiSpan[0].Mode = bmiSpan[1].Mode =
                        ReadIntraMode(ref r, GetYModeProbs(ref cm.Fc.Value, mi, aboveMi, leftMi, 0));
                    bmiSpan[2].Mode = bmiSpan[3].Mode = mi.Value.Mode =
                        ReadIntraMode(ref r, GetYModeProbs(ref cm.Fc.Value, mi, aboveMi, leftMi, 2));
                    break;
                default:
                    mi.Value.Mode = ReadIntraMode(ref r, GetYModeProbs(ref cm.Fc.Value, mi, aboveMi, leftMi, 0));
                    break;
            }

            mi.Value.UvMode = ReadIntraMode(ref r, cm.Fc.Value.KfUvModeProb[(int)mi.Value.Mode].AsSpan());
        }

        public static void ReadModeInfo(
            ref TileWorkerData twd,
            ref Vp9Common cm,
            int miRow,
            int miCol,
            int xMis,
            int yMis)
        {
            ref Reader r = ref twd.BitReader;
            ref MacroBlockD xd = ref twd.Xd;
            ref ModeInfo mi = ref xd.Mi[0].Value;
            ArrayPtr<MvRef> frameMvs = cm.CurFrameMvs.Slice((miRow * cm.MiCols) + miCol);

            if (cm.FrameIsIntraOnly())
            {
                ReadIntraFrameModeInfo(ref cm, ref xd, miRow, miCol, ref r, xMis, yMis);
            }
            else
            {
                ReadInterFrameModeInfo(ref cm, ref xd, miRow, miCol, ref r, xMis, yMis);

                for (int h = 0; h < yMis; ++h)
                {
                    for (int w = 0; w < xMis; ++w)
                    {
                        ref MvRef mv = ref frameMvs[w];
                        mi.RefFrame.AsSpan().CopyTo(mv.RefFrame.AsSpan());
                        mi.Mv.AsSpan().CopyTo(mv.Mv.AsSpan());
                    }

                    frameMvs = frameMvs.Slice(cm.MiCols);
                }
            }
        }
    }
}
