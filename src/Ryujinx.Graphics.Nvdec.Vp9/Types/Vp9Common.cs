using Ryujinx.Common.Memory;
using Ryujinx.Graphics.Nvdec.Vp9.Common;
using Ryujinx.Graphics.Nvdec.Vp9.Dsp;
using Ryujinx.Graphics.Video;
using System;

namespace Ryujinx.Graphics.Nvdec.Vp9.Types
{
    internal struct Vp9Common
    {
        public MacroBlockD Mb;

        public ArrayPtr<TileWorkerData> TileWorkerData;
        public int TotalTiles;

        public InternalErrorInfo Error;

        public VpxColorSpace ColorSpace;
        public VpxColorRange ColorRange;

        public int Width;
        public int Height;

        public int RenderWidth;
        public int RenderHeight;

        public int LastWidth;
        public int LastHeight;

        public int SubsamplingX;
        public int SubsamplingY;

        public bool UseHighBitDepth;

        public ArrayPtr<MvRef> PrevFrameMvs;
        public ArrayPtr<MvRef> CurFrameMvs;

        public Ptr<Surface> FrameToShow;
        public Ptr<RefCntBuffer> PrevFrame;

        public Ptr<RefCntBuffer> CurFrame;

        public Array8<int> RefFrameMap; /* maps fb_idx to reference slot */

        // Prepare ref_frame_map for the next frame.
        // Only used in frame parallel decode.
        public Array8<int> NextRefFrameMap;

        public Array3<RefBuffer> FrameRefs;

        public int NewFbIdx;

        public int CurShowFrameFbIdx;

        public FrameType LastFrameType;
        public FrameType FrameType;

        public int ShowFrame;
        public int LastShowFrame;
        public int ShowExistingFrame;

        // Flag signaling that the frame is encoded using only Intra modes.
        public bool IntraOnly;
        public bool LastIntraOnly;

        public bool AllowHighPrecisionMv;

        public int ResetFrameContext;

        // MBs, MbRows/Cols is in 16-pixel units; MiRows/Cols is in
        // ModeInfo (8-pixel) units.
        public int MBs;
        public int MbRows, MiRows;
        public int MbCols, MiCols;
        public int MiStride;

        /* Profile settings */
        public TxMode TxMode;

        public int BaseQindex;
        public int YDcDeltaQ;
        public int UvDcDeltaQ;
        public int UvAcDeltaQ;
        public Array8<Array2<short>> YDequant;
        public Array8<Array2<short>> UvDequant;

        /* We allocate a ModeInfo struct for each macroblock, together with
           an extra row on top and column on the left to simplify prediction. */
        public int MiAllocSize;
        public ArrayPtr<ModeInfo> Mip; /* Base of allocated array */
        public ArrayPtr<ModeInfo> Mi; /* Corresponds to upper left visible macroblock */

        // prev_mip and prev_mi will only be allocated in VP9 encoder.
        public Ptr<ModeInfo> PrevMip; /* MODE_INFO array 'mip' from last decoded frame */
        public Ptr<ModeInfo> PrevMi; /* 'mi' from last frame (points into prev_mip) */

        public ArrayPtr<Ptr<ModeInfo>> MiGridBase;
        public ArrayPtr<Ptr<ModeInfo>> MiGridVisible;

        // Whether to use previous frame's motion vectors for prediction.
        public bool UsePrevFrameMvs;

        // Persistent mb segment id map used in prediction.
        public int SegMapIdx;
        public int PrevSegMapIdx;

        public Array2<ArrayPtr<byte>> SegMapArray;
        public ArrayPtr<byte> LastFrameSegMap;
        public ArrayPtr<byte> CurrentFrameSegMap;

        public byte InterpFilter;

        public LoopFilterInfoN LfInfo;

        public int RefreshFrameContext; /* Two state 0 = NO, 1 = YES */

        public Array4<sbyte> RefFrameSignBias; /* Two state 0, 1 */

        public LoopFilter Lf;
        public Segmentation Seg;

        // Context probabilities for reference frame prediction
        public sbyte CompFixedRef;
        public Array2<sbyte> CompVarRef;
        public ReferenceMode ReferenceMode;

        public Ptr<Vp9EntropyProbs> Fc;
        public ArrayPtr<Vp9EntropyProbs> FrameContexts; // FRAME_CONTEXTS
        public uint FrameContextIdx; /* Context to use/update */
        public Ptr<Vp9BackwardUpdates> Counts;

        public uint CurrentVideoFrame;
        public BitstreamProfile Profile;

        public BitDepth BitDepth;
        public BitDepth DequantBitDepth; // bit_depth of current dequantizer

        public int ErrorResilientMode;
        public int FrameParallelDecodingMode;

        public int Log2TileCols, Log2TileRows;

        public int ByteAlignment;
        public int SkipLoopFilter;

        public Ptr<BufferPool> BufferPool;

        public ArrayPtr<sbyte> AboveSegContext;
        public ArrayPtr<sbyte> AboveContext;

        public readonly bool FrameIsIntraOnly()
        {
            return FrameType == FrameType.KeyFrame || IntraOnly;
        }

        public bool CompoundReferenceAllowed()
        {
            Span<sbyte> refFrameSignBiasSpan = RefFrameSignBias.AsSpan();
            
            for (int i = 1; i < Constants.RefsPerFrame; ++i)
            {
                if (refFrameSignBiasSpan[i + 1] != refFrameSignBiasSpan[1])
                {
                    return true;
                }
            }

            return false;
        }

        public readonly ref Surface GetFrameNewBuffer()
        {
            return ref BufferPool.Value.FrameBufs[NewFbIdx].Buf;
        }

        public readonly int GetFreeFb()
        {
            Span<RefCntBuffer> frameBuffs = BufferPool.Value.FrameBufs.AsSpan();

            int i;

            for (i = 0; i < Constants.FrameBuffers; ++i)
            {
                if (frameBuffs[i].RefCount == 0)
                {
                    break;
                }
            }

            if (i != Constants.FrameBuffers)
            {
                frameBuffs[i].RefCount = 1;
            }
            else
            {
                // Reset i to be INVALID_IDX to indicate no free buffer found.
                i = RefBuffer.InvalidIdx;
            }

            return i;
        }

        public void SwapCurrentAndLastSegMap()
        {
            // Swap indices.
            (SegMapIdx, PrevSegMapIdx) = (PrevSegMapIdx, SegMapIdx);

            CurrentFrameSegMap = SegMapArray[SegMapIdx];
            LastFrameSegMap = SegMapArray[PrevSegMapIdx];
        }

        private static int CalcMiSize(int len)
        {
            // Len is in mi units.
            return len + Constants.MiBlockSize;
        }

        public void SetMbMi(int width, int height)
        {
            int alignedWidth = BitUtils.AlignPowerOfTwo(width, Constants.MiSizeLog2);
            int alignedHeight = BitUtils.AlignPowerOfTwo(height, Constants.MiSizeLog2);

            MiCols = alignedWidth >> Constants.MiSizeLog2;
            MiRows = alignedHeight >> Constants.MiSizeLog2;
            MiStride = CalcMiSize(MiCols);

            MbCols = (MiCols + 1) >> 1;
            MbRows = (MiRows + 1) >> 1;
            MBs = MbRows * MbCols;
        }

        public void AllocTileWorkerData(MemoryAllocator allocator, int tileCols, int tileRows, int maxThreads)
        {
            TileWorkerData =
                allocator.Allocate<TileWorkerData>((tileCols * tileRows) + (maxThreads > 1 ? maxThreads : 0));
        }

        public readonly void FreeTileWorkerData(MemoryAllocator allocator)
        {
            allocator.Free(TileWorkerData);
        }

        private void AllocSegMap(MemoryAllocator allocator, int segMapSize)
        {
            Span<ArrayPtr<byte>> segMapArraySpan = SegMapArray.AsSpan();
            
            for (int i = 0; i < Constants.NumPingPongBuffers; ++i)
            {
                segMapArraySpan[i] = allocator.Allocate<byte>(segMapSize);
            }

            // Init the index.
            SegMapIdx = 0;
            PrevSegMapIdx = 1;

            CurrentFrameSegMap = segMapArraySpan[SegMapIdx];
            LastFrameSegMap = segMapArraySpan[PrevSegMapIdx];
        }

        private void FreeSegMap(MemoryAllocator allocator)
        {
            Span<ArrayPtr<byte>> segMapArraySpan = SegMapArray.AsSpan();
            
            for (int i = 0; i < Constants.NumPingPongBuffers; ++i)
            {
                allocator.Free(segMapArraySpan[i]);
                segMapArraySpan[i] = ArrayPtr<byte>.Null;
            }

            CurrentFrameSegMap = ArrayPtr<byte>.Null;
            LastFrameSegMap = ArrayPtr<byte>.Null;
        }

        private void DecAllocMi(MemoryAllocator allocator, int miSize)
        {
            Mip = allocator.Allocate<ModeInfo>(miSize);
            MiGridBase = allocator.Allocate<Ptr<ModeInfo>>(miSize);
        }

        private void DecFreeMi(MemoryAllocator allocator)
        {
            allocator.Free(Mip);
            Mip = ArrayPtr<ModeInfo>.Null;
            allocator.Free(MiGridBase);
            MiGridBase = ArrayPtr<Ptr<ModeInfo>>.Null;
        }

        public void FreeContextBuffers(MemoryAllocator allocator)
        {
            DecFreeMi(allocator);
            FreeSegMap(allocator);
            allocator.Free(AboveContext);
            AboveContext = ArrayPtr<sbyte>.Null;
            allocator.Free(AboveSegContext);
            AboveSegContext = ArrayPtr<sbyte>.Null;
            allocator.Free(Lf.Lfm);
            Lf.Lfm = ArrayPtr<LoopFilterMask>.Null;
            allocator.Free(CurFrameMvs);
            CurFrameMvs = ArrayPtr<MvRef>.Null;

            if (UsePrevFrameMvs)
            {
                allocator.Free(PrevFrameMvs);
                PrevFrameMvs = ArrayPtr<MvRef>.Null;
            }
        }

        private void AllocLoopFilter(MemoryAllocator allocator)
        {
            // Each lfm holds bit masks for all the 8x8 blocks in a 64x64 region. The
            // stride and rows are rounded up / truncated to a multiple of 8.
            Lf.LfmStride = (MiCols + (Constants.MiBlockSize - 1)) >> 3;
            Lf.Lfm = allocator.Allocate<LoopFilterMask>(((MiRows + (Constants.MiBlockSize - 1)) >> 3) * Lf.LfmStride);
        }

        public bool AllocContextBuffers(MemoryAllocator allocator, int width, int height)
        {
            SetMbMi(width, height);
            int newMiSize = MiStride * CalcMiSize(MiRows);
            if (newMiSize != 0)
            {
                DecAllocMi(allocator, newMiSize);
            }

            if (MiRows * MiCols != 0)
            {
                // Create the segmentation map structure and set to 0.
                AllocSegMap(allocator, MiRows * MiCols);
            }

            if (MiCols != 0)
            {
                AboveContext = allocator.Allocate<sbyte>(2 * TileInfo.MiColsAlignedToSb(MiCols) * Constants.MaxMbPlane);
                AboveSegContext = allocator.Allocate<sbyte>(TileInfo.MiColsAlignedToSb(MiCols));
            }

            AllocLoopFilter(allocator);

            CurFrameMvs = allocator.Allocate<MvRef>(MiRows * MiCols);
            // Using the same size as the current frame is fine here,
            // as this is never true when we have a resolution change.
            if (UsePrevFrameMvs)
            {
                PrevFrameMvs = allocator.Allocate<MvRef>(MiRows * MiCols);
            }

            return false;
        }

        private unsafe void DecSetupMi()
        {
            Mi = Mip.Slice(MiStride + 1);
            MiGridVisible = MiGridBase.Slice(MiStride + 1);
            MemoryUtil.Fill(MiGridBase.ToPointer(), Ptr<ModeInfo>.Null, MiStride * (MiRows + 1));
        }

        public unsafe void InitContextBuffers()
        {
            DecSetupMi();
            if (!LastFrameSegMap.IsNull)
            {
                MemoryUtil.Fill(LastFrameSegMap.ToPointer(), (byte)0, MiRows * MiCols);
            }
        }

        private readonly void SetPartitionProbs(ref MacroBlockD xd)
        {
            xd.PartitionProbs = FrameIsIntraOnly()
                ? new ArrayPtr<Array3<byte>>(ref Fc.Value.KfPartitionProb[0], 16)
                : new ArrayPtr<Array3<byte>>(ref Fc.Value.PartitionProb[0], 16);
        }

        internal void InitMacroBlockD(ref MacroBlockD xd, ArrayPtr<int> dqcoeff)
        {
            Span<MacroBlockDPlane> planeSpan = xd.Plane.AsSpan();
            Span<ArrayPtr<sbyte>> aboveContextSpan = xd.AboveContext.AsSpan();
            
            for (int i = 0; i < Constants.MaxMbPlane; ++i)
            {
                planeSpan[i].DqCoeff = dqcoeff;
                aboveContextSpan[i] = AboveContext.Slice(i * 2 * TileInfo.MiColsAlignedToSb(MiCols));

                if (i == 0)
                {
                    MemoryUtil.Copy(ref planeSpan[i].SegDequant, ref YDequant);
                }
                else
                {
                    MemoryUtil.Copy(ref planeSpan[i].SegDequant, ref UvDequant);
                }

                xd.Fc = new Ptr<Vp9EntropyProbs>(ref Fc.Value);
            }

            xd.AboveSegContext = AboveSegContext;
            xd.MiStride = MiStride;
            xd.ErrorInfo = new Ptr<InternalErrorInfo>(ref Error);

            SetPartitionProbs(ref xd);
        }

        public void SetupSegmentationDequant()
        {
            // Build y/uv dequant values based on segmentation.
            if (Seg.Enabled)
            {
                Span<Array2<short>> yDequantSpan1 = YDequant.AsSpan();
                Span<Array2<short>> uvDequantSpan1 = UvDequant.AsSpan();
                
                for (int i = 0; i < Constants.MaxSegments; ++i)
                {
                    Span<short> yDequantSpan2 = yDequantSpan1[i].AsSpan();
                    Span<short> uvDequantSpan2 = uvDequantSpan1[i].AsSpan();
                    
                    int qindex = Seg.GetQIndex(i, BaseQindex);
                    yDequantSpan2[0] = QuantCommon.DcQuant(qindex, YDcDeltaQ, BitDepth);
                    yDequantSpan2[1] = QuantCommon.AcQuant(qindex, 0, BitDepth);
                    uvDequantSpan2[0] = QuantCommon.DcQuant(qindex, UvDcDeltaQ, BitDepth);
                    uvDequantSpan2[1] = QuantCommon.AcQuant(qindex, UvAcDeltaQ, BitDepth);
                }
            }
            else
            {
                Span<short> yDequantSpan = YDequant[0].AsSpan();
                Span<short> uvDequantSpan = UvDequant[0].AsSpan();
                
                int qindex = BaseQindex;
                // When segmentation is disabled, only the first value is used.  The
                // remaining are don't cares.
                yDequantSpan[0] = QuantCommon.DcQuant(qindex, YDcDeltaQ, BitDepth);
                yDequantSpan[1] = QuantCommon.AcQuant(qindex, 0, BitDepth);
                uvDequantSpan[0] = QuantCommon.DcQuant(qindex, UvDcDeltaQ, BitDepth);
                uvDequantSpan[1] = QuantCommon.AcQuant(qindex, UvAcDeltaQ, BitDepth);
            }
        }

        public void SetupScaleFactors()
        {
            Span<RefBuffer> frameRefsSpan = FrameRefs.AsSpan();
            
            for (int i = 0; i < Constants.RefsPerFrame; ++i)
            {
                ref RefBuffer refBuf = ref frameRefsSpan[i];
                refBuf.Sf.SetupScaleFactorsForFrame(refBuf.Buf.Width, refBuf.Buf.Height, Width, Height);
            }
        }

        public void ReadFrameReferenceModeProbs(ref Reader r)
        {
            ref Vp9EntropyProbs fc = ref Fc.Value;

            if (ReferenceMode == ReferenceMode.Select)
            {
                Span<byte> compInterProbSpan = fc.CompInterProb.AsSpan();
                
                for (int i = 0; i < Constants.CompInterContexts; ++i)
                {
                    r.DiffUpdateProb(ref compInterProbSpan[i]);
                }
            }

            if (ReferenceMode != ReferenceMode.Compound)
            {
                Span<Array2<byte>> singleRefProbSpan1 = fc.SingleRefProb.AsSpan();
                
                for (int i = 0; i < Constants.RefContexts; ++i)
                {
                    Span<byte> singleRefProbSpan2 = singleRefProbSpan1[i].AsSpan();
                    
                    r.DiffUpdateProb(ref singleRefProbSpan2[0]);
                    r.DiffUpdateProb(ref singleRefProbSpan2[1]);
                }
            }

            if (ReferenceMode != ReferenceMode.Single)
            {
                Span<byte> compRefProbSpan = fc.CompRefProb.AsSpan();
                
                for (int i = 0; i < Constants.RefContexts; ++i)
                {
                    r.DiffUpdateProb(ref compRefProbSpan[i]);
                }
            }
        }

        public ReferenceMode ReadFrameReferenceMode(ref Reader r)
        {
            if (CompoundReferenceAllowed())
            {
                return r.ReadBit() != 0
                    ? r.ReadBit() != 0 ? ReferenceMode.Select : ReferenceMode.Compound
                    : ReferenceMode.Single;
            }

            return ReferenceMode.Single;
        }

        public void SetupCompoundReferenceMode()
        {
            Span<sbyte> refFrameSignBiasSpan = RefFrameSignBias.AsSpan();
            Span<sbyte> compVarRefSpan = CompVarRef.AsSpan();
            
            if (refFrameSignBiasSpan[Constants.LastFrame] == refFrameSignBiasSpan[Constants.GoldenFrame])
            {
                CompFixedRef = Constants.AltRefFrame;
                compVarRefSpan[0] = Constants.LastFrame;
                compVarRefSpan[1] = Constants.GoldenFrame;
            }
            else if (refFrameSignBiasSpan[Constants.LastFrame] == refFrameSignBiasSpan[Constants.AltRefFrame])
            {
                CompFixedRef = Constants.GoldenFrame;
                compVarRefSpan[0] = Constants.LastFrame;
                compVarRefSpan[1] = Constants.AltRefFrame;
            }
            else
            {
                CompFixedRef = Constants.LastFrame;
                compVarRefSpan[0] = Constants.GoldenFrame;
                compVarRefSpan[1] = Constants.AltRefFrame;
            }
        }

        public readonly void InitMvProbs()
        {
            Span<byte> jointsSpan = Fc.Value.Joints.AsSpan();
            Span<byte> signSpan = Fc.Value.Sign.AsSpan();
            Span<Array10<byte>> classesSpan = Fc.Value.Classes.AsSpan();
            Span<byte> classes0Span = classesSpan[0].AsSpan();
            Span<byte> classes1Span = classesSpan[1].AsSpan();
            Span<Array1<byte>> class0Span = Fc.Value.Class0.AsSpan();
            Span<Array10<byte>> bitsSpan = Fc.Value.Bits.AsSpan();
            Span<byte> bits0Span = bitsSpan[0].AsSpan();
            Span<byte> bits1Span = bitsSpan[1].AsSpan();
            Span<Array2<Array3<byte>>> class0FpSpan = Fc.Value.Class0Fp.AsSpan();
            Span<Array3<byte>> class0Fp0Span = class0FpSpan[0].AsSpan();
            Span<Array3<byte>> class0Fp1Span = class0FpSpan[1].AsSpan();
            Span<byte> class0Fp00Span = class0Fp0Span[0].AsSpan();
            Span<byte> class0Fp01Span = class0Fp0Span[1].AsSpan();
            Span<byte> class0Fp10Span = class0Fp1Span[0].AsSpan();
            Span<byte> class0Fp11Span = class0Fp1Span[1].AsSpan();
            Span<Array3<byte>> fpSpan = Fc.Value.Fp.AsSpan();
            Span<byte> fp0Span = fpSpan[0].AsSpan();
            Span<byte> fp1Span = fpSpan[1].AsSpan();
            Span<byte> class0HpSpan = Fc.Value.Class0Hp.AsSpan();
            Span<byte> hpSpan = Fc.Value.Hp.AsSpan();
            
            jointsSpan[0] = 32;
            jointsSpan[1] = 64;
            jointsSpan[2] = 96;

            signSpan[0] = 128;
            classes0Span[0] = 224;
            classes0Span[1] = 144;
            classes0Span[2] = 192;
            classes0Span[3] = 168;
            classes0Span[4] = 192;
            classes0Span[5] = 176;
            classes0Span[6] = 192;
            classes0Span[7] = 198;
            classes0Span[8] = 198;
            classes0Span[9] = 245;
            class0Span[0][0] = 216;
            bits0Span[0] = 136;
            bits0Span[1] = 140;
            bits0Span[2] = 148;
            bits0Span[3] = 160;
            bits0Span[4] = 176;
            bits0Span[5] = 192;
            bits0Span[6] = 224;
            bits0Span[7] = 234;
            bits0Span[8] = 234;
            bits0Span[9] = 240;
            class0Fp00Span[0] = 128;
            class0Fp00Span[1] = 128;
            class0Fp00Span[2] = 64;
            class0Fp01Span[0] = 96;
            class0Fp01Span[1] = 112;
            class0Fp01Span[2] = 64;
            fp0Span[0] = 64;
            fp0Span[1] = 96;
            fp0Span[2] = 64;
            class0HpSpan[0] = 160;
            hpSpan[0] = 128;

            signSpan[1] = 128;
            classes1Span[0] = 216;
            classes1Span[1] = 128;
            classes1Span[2] = 176;
            classes1Span[3] = 160;
            classes1Span[4] = 176;
            classes1Span[5] = 176;
            classes1Span[6] = 192;
            classes1Span[7] = 198;
            classes1Span[8] = 198;
            classes1Span[9] = 208;
            class0Span[1][0] = 208;
            bits1Span[0] = 136;
            bits1Span[1] = 140;
            bits1Span[2] = 148;
            bits1Span[3] = 160;
            bits1Span[4] = 176;
            bits1Span[5] = 192;
            bits1Span[6] = 224;
            bits1Span[7] = 234;
            bits1Span[8] = 234;
            bits1Span[9] = 240;
            class0Fp10Span[0] = 128;
            class0Fp10Span[1] = 128;
            class0Fp10Span[2] = 64;
            class0Fp11Span[0] = 96;
            class0Fp11Span[1] = 112;
            class0Fp11Span[2] = 64;
            fp1Span[0] = 64;
            fp1Span[1] = 96;
            fp1Span[2] = 64;
            class0HpSpan[1] = 160;
            hpSpan[1] = 128;
        }

        public void AdaptMvProbs(bool allowHp)
        {
            ref Vp9EntropyProbs fc = ref Fc.Value;
            ref Vp9EntropyProbs preFc = ref FrameContexts[(int)FrameContextIdx];
            ref Vp9BackwardUpdates counts = ref Counts.Value;

            Prob.VpxTreeMergeProbs(
                EntropyMv.JointTree,
                preFc.Joints.AsSpan(),
                counts.Joints.AsSpan(),
                fc.Joints.AsSpan());

            Span<byte> fSignSpan = fc.Sign.AsSpan();
            Span<byte> pSignSpan = preFc.Sign.AsSpan();
            Span<Array2<uint>> cSignSpan = counts.Sign.AsSpan();
            Span<Array10<byte>> fClassesSpan = fc.Classes.AsSpan();
            Span<Array10<byte>> pClassesSpan = preFc.Classes.AsSpan();
            Span<Array11<uint>> cClassesSpan = counts.Classes.AsSpan();
            Span<Array1<byte>> fClass0Span = fc.Class0.AsSpan();
            Span<Array1<byte>> pClass0Span = preFc.Class0.AsSpan();
            Span<Array2<uint>> cClass0Span = counts.Class0.AsSpan();
            Span<Array10<byte>> fBitsSpan1 = fc.Bits.AsSpan();
            Span<Array10<byte>> pBitsSpan1 = preFc.Bits.AsSpan();
            Span<Array10<Array2<uint>>> cBitsSpan1 = counts.Bits.AsSpan();
            Span<Array2<Array3<byte>>> fClass0FpSpan1 = fc.Class0Fp.AsSpan();
            Span<Array2<Array3<byte>>> pClass0FpSpan1 = preFc.Class0Fp.AsSpan();
            Span<Array2<Array4<uint>>> cClass0FpSpan1 = counts.Class0Fp.AsSpan();
            Span<Array3<byte>> fFpSpan = fc.Fp.AsSpan();
            Span<Array3<byte>> pFpSpan = preFc.Fp.AsSpan();
            Span<Array4<uint>> cFpSpan = counts.Fp.AsSpan();
            Span<byte> fClass0HpSpan = fc.Class0Hp.AsSpan();
            Span<byte> pClass0HpSpan = preFc.Class0Hp.AsSpan();
            Span<Array2<uint>> cClass0HpSpan = counts.Class0Hp.AsSpan();
            Span<byte> fHpSpan = fc.Hp.AsSpan();
            Span<byte> pHpSpan = preFc.Hp.AsSpan();
            Span<Array2<uint>> cHpSpan = counts.Hp.AsSpan();

            for (int i = 0; i < 2; ++i)
            {
                fSignSpan[i] = Prob.ModeMvMergeProbs(pSignSpan[i], cSignSpan[i].AsSpan());
                Prob.VpxTreeMergeProbs(
                    EntropyMv.ClassTree,
                    pClassesSpan[i].AsSpan(),
                    cClassesSpan[i].AsSpan(),
                    fClassesSpan[i].AsSpan());
                Prob.VpxTreeMergeProbs(
                    EntropyMv.Class0Tree,
                    pClass0Span[i].AsSpan(),
                    cClass0Span[i].AsSpan(),
                    fClass0Span[i].AsSpan());
                
                Span<byte> fBitsSpan2 = fBitsSpan1[i].AsSpan();
                Span<byte> pBitsSpan2 = pBitsSpan1[i].AsSpan();
                Span<Array2<uint>> cBitsSpan2 = cBitsSpan1[i].AsSpan();

                for (int j = 0; j < EntropyMv.OffsetBits; ++j)
                {
                    fBitsSpan2[j] = Prob.ModeMvMergeProbs(pBitsSpan2[j], cBitsSpan2[j].AsSpan());
                }

                Span<Array3<byte>> fClass0FpSpan2 = fClass0FpSpan1[i].AsSpan();
                Span<Array3<byte>> pClass0FpSpan2 = pClass0FpSpan1[i].AsSpan();
                Span<Array4<uint>> cClass0FpSpan2 = cClass0FpSpan1[i].AsSpan();

                for (int j = 0; j < EntropyMv.Class0Size; ++j)
                {
                    Prob.VpxTreeMergeProbs(
                        EntropyMv.FpTree,
                        pClass0FpSpan2[j].AsSpan(),
                        cClass0FpSpan2[j].AsSpan(),
                        fClass0FpSpan2[j].AsSpan());
                }

                Prob.VpxTreeMergeProbs(EntropyMv.FpTree, pFpSpan[i].AsSpan(), cFpSpan[i].AsSpan(),
                    fFpSpan[i].AsSpan());

                if (allowHp)
                {
                    fClass0HpSpan[i] = Prob.ModeMvMergeProbs(pClass0HpSpan[i], cClass0HpSpan[i].AsSpan());
                    fHpSpan[i] = Prob.ModeMvMergeProbs(pHpSpan[i], cHpSpan[i].AsSpan());
                }
            }
        }

        public void ResizeContextBuffers(MemoryAllocator allocator, int width, int height)
        {
            if (Width != width || Height != height)
            {
                int newMiRows = BitUtils.AlignPowerOfTwo(height, Constants.MiSizeLog2) >> Constants.MiSizeLog2;
                int newMiCols = BitUtils.AlignPowerOfTwo(width, Constants.MiSizeLog2) >> Constants.MiSizeLog2;

                // Allocations in AllocContextBuffers() depend on individual
                // dimensions as well as the overall size.
                if (newMiCols > MiCols || newMiRows > MiRows)
                {
                    if (AllocContextBuffers(allocator, width, height))
                    {
                        // The Mi* values have been cleared and any existing context
                        // buffers have been freed. Clear Width and Height to be
                        // consistent and to force a realloc next time.
                        Width = 0;
                        Height = 0;
                        Error.InternalError(CodecErr.MemError, "Failed to allocate context buffers");
                    }
                }
                else
                {
                    SetMbMi(width, height);
                }

                InitContextBuffers();
                Width = width;
                Height = height;
            }

            if (CurFrameMvs.IsNull ||
                MiRows > CurFrame.Value.MiRows ||
                MiCols > CurFrame.Value.MiCols)
            {
                ResizeMvBuffer(allocator);
            }
        }

        public void CheckMemError<T>(ref ArrayPtr<T> lval, ArrayPtr<T> expr)
            where T : unmanaged
        {
            lval = expr;
            if (lval.IsNull)
            {
                Error.InternalError(CodecErr.MemError, "Failed to allocate");
            }
        }

        private void ResizeMvBuffer(MemoryAllocator allocator)
        {
            allocator.Free(CurFrameMvs);
            CurFrame.Value.MiRows = MiRows;
            CurFrame.Value.MiCols = MiCols;
            CheckMemError(ref CurFrameMvs, allocator.Allocate<MvRef>(MiRows * MiCols));
        }

        public void CheckMemError<T>(ref Ptr<T> lval, Ptr<T> expr) where T : unmanaged
        {
            lval = expr;
            if (lval.IsNull)
            {
                Error.InternalError(CodecErr.MemError, "Failed to allocate");
            }
        }

        public void SetupTileInfo(ref ReadBitBuffer rb)
        {
            TileInfo.GetTileNBits(MiCols, out int minLog2TileCols, out int maxLog2TileCols);

            // columns
            int maxOnes = maxLog2TileCols - minLog2TileCols;
            Log2TileCols = minLog2TileCols;
            while (maxOnes-- != 0 && rb.ReadBit() != 0)
            {
                Log2TileCols++;
            }

            if (Log2TileCols > 6)
            {
                Error.InternalError(CodecErr.CorruptFrame, "Invalid number of tile columns");
            }

            // rows
            Log2TileRows = rb.ReadBit();
            if (Log2TileRows != 0)
            {
                Log2TileRows += rb.ReadBit();
            }
        }

        public void ReadBitdepthColorspaceSampling(ref ReadBitBuffer rb)
        {
            if (Profile >= BitstreamProfile.Profile2)
            {
                BitDepth = rb.ReadBit() != 0 ? BitDepth.Bits12 : BitDepth.Bits10;
                UseHighBitDepth = true;
            }
            else
            {
                BitDepth = BitDepth.Bits8;
                UseHighBitDepth = false;
            }

            ColorSpace = (VpxColorSpace)rb.ReadLiteral(3);
            if (ColorSpace != VpxColorSpace.Srgb)
            {
                ColorRange = (VpxColorRange)rb.ReadBit();
                if (Profile is BitstreamProfile.Profile1 or BitstreamProfile.Profile3)
                {
                    SubsamplingX = rb.ReadBit();
                    SubsamplingY = rb.ReadBit();
                    if (SubsamplingX == 1 && SubsamplingY == 1)
                    {
                        Error.InternalError(CodecErr.UnsupBitstream,
                            "4:2:0 color not supported in profile 1 or 3");
                    }

                    if (rb.ReadBit() != 0)
                    {
                        Error.InternalError(CodecErr.UnsupBitstream, "Reserved bit set");
                    }
                }
                else
                {
                    SubsamplingY = SubsamplingX = 1;
                }
            }
            else
            {
                ColorRange = VpxColorRange.Full;
                if (Profile is BitstreamProfile.Profile1 or BitstreamProfile.Profile3)
                {
                    // Note if colorspace is SRGB then 4:4:4 chroma sampling is assumed.
                    // 4:2:2 or 4:4:0 chroma sampling is not allowed.
                    SubsamplingY = SubsamplingX = 0;
                    if (rb.ReadBit() != 0)
                    {
                        Error.InternalError(CodecErr.UnsupBitstream, "Reserved bit set");
                    }
                }
                else
                {
                    Error.InternalError(CodecErr.UnsupBitstream, "4:4:4 color not supported in profile 0 or 2");
                }
            }
        }

        public void AdaptModeProbs()
        {
            ref Vp9EntropyProbs fc = ref Fc.Value;
            ref Vp9EntropyProbs preFc = ref FrameContexts[(int)FrameContextIdx];
            ref Vp9BackwardUpdates counts = ref Counts.Value;

            Span<byte> fIntraInterProbSpan = fc.IntraInterProb.AsSpan();
            Span<byte> pIntraInterProbSpan = preFc.IntraInterProb.AsSpan();
            Span<Array2<uint>> cIntraInterSpan = counts.IntraInter.AsSpan();

            for (int i = 0; i < Constants.IntraInterContexts; i++)
            {
                fIntraInterProbSpan[i] = Prob.ModeMvMergeProbs(pIntraInterProbSpan[i], cIntraInterSpan[i].AsSpan());
            }
            
            Span<byte> fCompInterProbSpan = fc.CompInterProb.AsSpan();
            Span<byte> pCompInterProbSpan = preFc.CompInterProb.AsSpan();
            Span<Array2<uint>> cCompInterSpan = counts.CompInter.AsSpan();

            for (int i = 0; i < Constants.CompInterContexts; i++)
            {
                fCompInterProbSpan[i] = Prob.ModeMvMergeProbs(pCompInterProbSpan[i], cCompInterSpan[i].AsSpan());
            }
            
            Span<byte> fCompRefProbSpan = fc.CompRefProb.AsSpan();
            Span<byte> pCompRefProbSpan = preFc.CompRefProb.AsSpan();
            Span<Array2<uint>> cCompRefSpan = counts.CompRef.AsSpan();

            for (int i = 0; i < Constants.RefContexts; i++)
            {
                fCompRefProbSpan[i] = Prob.ModeMvMergeProbs(pCompRefProbSpan[i], cCompRefSpan[i].AsSpan());
            }
            
            Span<Array2<byte>> fSingleRefProbSpan1 = fc.SingleRefProb.AsSpan();
            Span<Array2<byte>> pSingleRefProbSpan1 = preFc.SingleRefProb.AsSpan();
            Span<Array2<Array2<uint>>> cSingleRefSpan1 = counts.SingleRef.AsSpan();

            for (int i = 0; i < Constants.RefContexts; i++)
            {
                Span<byte> fSingleRefProbSpan2 = fSingleRefProbSpan1[i].AsSpan();
                Span<byte> pSingleRefProbSpan2 = pSingleRefProbSpan1[i].AsSpan();
                Span<Array2<uint>> cSingleRefSpan2 = cSingleRefSpan1[i].AsSpan();
                
                for (int j = 0; j < 2; j++)
                {
                    fSingleRefProbSpan2[j] =
                        Prob.ModeMvMergeProbs(pSingleRefProbSpan2[j], cSingleRefSpan2[j].AsSpan());
                }
            }
            
            Span<Array3<byte>> fInterModeProbSpan = fc.InterModeProb.AsSpan();
            Span<Array3<byte>> pInterModeProbSpan = preFc.InterModeProb.AsSpan();
            Span<Array4<uint>> cInterModeSpan = counts.InterMode.AsSpan();

            for (int i = 0; i < Constants.InterModeContexts; i++)
            {
                Prob.VpxTreeMergeProbs(
                    EntropyMode.InterModeTree,
                    pInterModeProbSpan[i].AsSpan(),
                    cInterModeSpan[i].AsSpan(),
                    fInterModeProbSpan[i].AsSpan());
            }
            
            Span<Array9<byte>> fYModeProbSpan = fc.YModeProb.AsSpan();
            Span<Array9<byte>> pYModeProbSpan = preFc.YModeProb.AsSpan();
            Span<Array10<uint>> cYModeSpan = counts.YMode.AsSpan();

            for (int i = 0; i < EntropyMode.BlockSizeGroups; i++)
            {
                Prob.VpxTreeMergeProbs(
                    EntropyMode.IntraModeTree,
                    pYModeProbSpan[i].AsSpan(),
                    cYModeSpan[i].AsSpan(),
                    fYModeProbSpan[i].AsSpan());
            }
            
            Span<Array9<byte>> fUvModeProbSpan = fc.UvModeProb.AsSpan();
            Span<Array9<byte>> pUvModeProbSpan = preFc.UvModeProb.AsSpan();
            Span<Array10<uint>> cUvModeSpan = counts.UvMode.AsSpan();

            for (int i = 0; i < Constants.IntraModes; ++i)
            {
                Prob.VpxTreeMergeProbs(
                    EntropyMode.IntraModeTree,
                    pUvModeProbSpan[i].AsSpan(),
                    cUvModeSpan[i].AsSpan(),
                    fUvModeProbSpan[i].AsSpan());
            }
            
            Span<Array3<byte>> fPartitionProbSpan = fc.PartitionProb.AsSpan();
            Span<Array3<byte>> pPartitionProbSpan = preFc.PartitionProb.AsSpan();
            Span<Array4<uint>> cPartitionSpan = counts.Partition.AsSpan();

            for (int i = 0; i < Constants.PartitionContexts; i++)
            {
                Prob.VpxTreeMergeProbs(
                    EntropyMode.PartitionTree,
                    pPartitionProbSpan[i].AsSpan(),
                    cPartitionSpan[i].AsSpan(),
                    fPartitionProbSpan[i].AsSpan());
            }

            if (InterpFilter == Constants.Switchable)
            {
                Span<Array2<byte>> fSwitchableInterpProbSpan = fc.SwitchableInterpProb.AsSpan();
                Span<Array2<byte>> pSwitchableInterpProbSpan = preFc.SwitchableInterpProb.AsSpan();
                Span<Array3<uint>> cSwitchableInterpSpan = counts.SwitchableInterp.AsSpan();
                
                for (int i = 0; i < Constants.SwitchableFilterContexts; i++)
                {
                    Prob.VpxTreeMergeProbs(
                        EntropyMode.SwitchableInterpTree,
                        pSwitchableInterpProbSpan[i].AsSpan(),
                        cSwitchableInterpSpan[i].AsSpan(),
                        fSwitchableInterpProbSpan[i].AsSpan());
                }
            }

            if (TxMode == TxMode.TxModeSelect)
            {
                Array1<Array2<uint>> branchCt8X8P = new();
                Array2<Array2<uint>> branchCt16X16P = new();
                Array3<Array2<uint>> branchCt32X32P = new();
                
                Span<Array2<uint>> branchCt8X8PSpan = branchCt8X8P.AsSpan();
                Span<Array2<uint>> branchCt16X16PSpan = branchCt16X16P.AsSpan();
                Span<Array2<uint>> branchCt32X32PSpan = branchCt32X32P.AsSpan();
                
                Span<Array2<uint>> tx8x8Span = counts.Tx8x8.AsSpan();
                Span<Array2<uint>> tx16x16Span = counts.Tx8x8.AsSpan();
                Span<Array2<uint>> tx32x32Span = counts.Tx8x8.AsSpan();

                //There is no need for a Span2, as there is only ever 1 iteration
                Span<Array1<byte>> fTx8x8ProbSpan = fc.Tx8x8Prob.AsSpan();
                Span<Array1<byte>> pTx8x8ProbSpan = preFc.Tx8x8Prob.AsSpan();
                
                Span<Array2<byte>> fTx16x16ProbSpan1 = fc.Tx16x16Prob.AsSpan();
                Span<Array2<byte>> pTx16x16ProbSpan1 = preFc.Tx16x16Prob.AsSpan();
                
                Span<Array3<byte>> fTx32x32ProbSpan1 = fc.Tx32x32Prob.AsSpan();
                Span<Array3<byte>> pTx32x32ProbSpan1 = preFc.Tx32x32Prob.AsSpan();

                for (int i = 0; i < EntropyMode.TxSizeContexts; ++i)
                {
                    EntropyMode.TxCountsToBranchCounts8X8(tx8x8Span[i].AsSpan(), branchCt8X8P.AsSpan());
                    for (int j = 0; j < (int)TxSize.TxSizes - 3; ++j)
                    {
                        fTx8x8ProbSpan[i][j] = Prob.ModeMvMergeProbs(pTx8x8ProbSpan[i][j], branchCt8X8PSpan[j].AsSpan());
                    }
                    
                    Span<byte> fTx16x16ProbSpan2 = fTx16x16ProbSpan1[i].AsSpan();
                    Span<byte> pTx16x16ProbSpan2 = pTx16x16ProbSpan1[i].AsSpan();

                    EntropyMode.TxCountsToBranchCounts16X16(tx16x16Span[i].AsSpan(), branchCt16X16P.AsSpan());
                    for (int j = 0; j < (int)TxSize.TxSizes - 2; ++j)
                    {
                        fTx16x16ProbSpan2[j] =
                            Prob.ModeMvMergeProbs(pTx16x16ProbSpan2[j], branchCt16X16PSpan[j].AsSpan());
                    }
                    
                    Span<byte> fTx32x32ProbSpan2 = fTx32x32ProbSpan1[i].AsSpan();
                    Span<byte> pTx32x32ProbSpan2 = pTx32x32ProbSpan1[i].AsSpan();

                    EntropyMode.TxCountsToBranchCounts32X32(tx32x32Span[i].AsSpan(), branchCt32X32P.AsSpan());
                    for (int j = 0; j < (int)TxSize.TxSizes - 1; ++j)
                    {
                        fTx32x32ProbSpan2[j] =
                            Prob.ModeMvMergeProbs(pTx32x32ProbSpan2[j], branchCt32X32PSpan[j].AsSpan());
                    }
                }
            }
            
            Span<byte> fSkipProbSpan = fc.SkipProb.AsSpan();
            Span<byte> pSkipProbSpan = preFc.SkipProb.AsSpan();
            Span<Array2<uint>> cSkipSpan = counts.Skip.AsSpan();

            for (int i = 0; i < Constants.SkipContexts; ++i)
            {
                fSkipProbSpan[i] = Prob.ModeMvMergeProbs(pSkipProbSpan[i], cSkipSpan[i].AsSpan());
            }
        }

        public void AdaptCoefProbs()
        {
            byte t;
            uint countSat, updateFactor;

            if (FrameIsIntraOnly())
            {
                updateFactor = Entropy.CoefMaxUpdateFactorKey;
                countSat = Entropy.CoefCountSatKey;
            }
            else if (LastFrameType == FrameType.KeyFrame)
            {
                updateFactor = Entropy.CoefMaxUpdateFactorAfterKey; /* adapt quickly */
                countSat = Entropy.CoefCountSatAfterKey;
            }
            else
            {
                updateFactor = Entropy.CoefMaxUpdateFactor;
                countSat = Entropy.CoefCountSat;
            }

            for (t = (int)TxSize.Tx4X4; t <= (int)TxSize.Tx32X32; t++)
            {
                AdaptCoefProbs(t, countSat, updateFactor);
            }
        }

        public void SetMvs(ReadOnlySpan<Vp9MvRef> mvs)
        {
            if (mvs.Length > PrevFrameMvs.Length)
            {
                throw new ArgumentException(
                    $"Size mismatch, expected: {PrevFrameMvs.Length}, but got: {mvs.Length}.");
            }

            for (int i = 0; i < mvs.Length; i++)
            {
                ref MvRef mv = ref PrevFrameMvs[i];

                Span<Mv> mvSpan = mv.Mv.AsSpan();
                Span<Vp9Mv> mvsSpan = mvs[i].Mvs.AsSpan();

                mvSpan[0].Row = mvsSpan[0].Row;
                mvSpan[0].Col = mvsSpan[0].Col;
                mvSpan[1].Row = mvsSpan[1].Row;
                mvSpan[1].Col = mvsSpan[1].Col;

                Span<sbyte> refFrameSpan = mv.RefFrame.AsSpan();
                Span<int> refFramesSpan = mvs[i].RefFrames.AsSpan();
                
                refFrameSpan[0] = (sbyte)refFramesSpan[0];
                refFrameSpan[1] = (sbyte)refFramesSpan[1];
            }
        }

        public void GetMvs(Span<Vp9MvRef> mvs)
        {
            if (FrameIsIntraOnly())
            {
                mvs.Clear();
                return;
            }

            if (mvs.Length > CurFrameMvs.Length)
            {
                throw new ArgumentException(
                    $"Size mismatch, expected: {CurFrameMvs.Length}, but got: {mvs.Length}.");
            }

            for (int i = 0; i < mvs.Length; i++)
            {
                ref MvRef mv = ref CurFrameMvs[i];
                
                Span<Mv> mvSpan = mv.Mv.AsSpan();
                Span<Vp9Mv> mvsSpan = mvs[i].Mvs.AsSpan();

                mvsSpan[0].Row = mvSpan[0].Row;
                mvsSpan[0].Col = mvSpan[0].Col;
                mvsSpan[1].Row = mvSpan[1].Row;
                mvsSpan[1].Col = mvSpan[1].Col;
                
                Span<sbyte> refFrameSpan = mv.RefFrame.AsSpan();
                Span<int> refFramesSpan = mvs[i].RefFrames.AsSpan();

                refFramesSpan[0] = refFrameSpan[0];
                refFramesSpan[1] = refFrameSpan[1];
            }
        }

        private void AdaptCoefProbs(byte txSize, uint countSat, uint updateFactor)
        {
            ref Vp9EntropyProbs preFc = ref FrameContexts[(int)FrameContextIdx];
            Span<Array2<Array6<Array6<Array3<byte>>>>> probsSpan1 = Fc.Value.CoefProbs[txSize].AsSpan();
            Span<Array2<Array6<Array6<Array3<byte>>>>> preProbsSpan1 = preFc.CoefProbs[txSize].AsSpan();
            Span<Array2<Array6<Array6<Array4<uint>>>>> countsSpan1 = Counts.Value.Coef[txSize].AsSpan();
            Span<Array2<Array6<Array6<uint>>>> eobCountsSpan1 = Counts.Value.EobBranch[txSize].AsSpan();

            for (int i = 0; i < Constants.PlaneTypes; ++i)
            {
                Span<Array6<Array6<Array3<byte>>>> probsSpan2 = probsSpan1[i].AsSpan();
                Span<Array6<Array6<Array3<byte>>>> preProbsSpan2 = preProbsSpan1[i].AsSpan();
                Span<Array6<Array6<Array4<uint>>>> countsSpan2 = countsSpan1[i].AsSpan();
                Span<Array6<Array6<uint>>> eobCountsSpan2 = eobCountsSpan1[i].AsSpan();
                
                for (int j = 0; j < Entropy.RefTypes; ++j)
                {
                    Span<Array6<Array3<byte>>> probsSpan3 = probsSpan2[j].AsSpan();
                    Span<Array6<Array3<byte>>> preProbsSpan3 = preProbsSpan2[j].AsSpan();
                    Span<Array6<Array4<uint>>> countsSpan3 = countsSpan2[j].AsSpan();
                    Span<Array6<uint>> eobCountsSpan3 = eobCountsSpan2[j].AsSpan();
                    
                    for (int k = 0; k < Entropy.CoefBands; ++k)
                    {
                        Span<Array3<byte>> probsSpan4 = probsSpan3[k].AsSpan();
                        Span<Array3<byte>> preProbsSpan4 = preProbsSpan3[k].AsSpan();
                        Span<Array4<uint>> countsSpan4 = countsSpan3[k].AsSpan();
                        Span<uint> eobCountsSpan4 = eobCountsSpan3[k].AsSpan();
                        
                        for (int l = 0; l < Entropy.BAND_COEFF_CONTEXTS(k); ++l)
                        {
                            Span<byte> probsSpan5 = probsSpan4[l].AsSpan();
                            Span<byte> preProbsSpan5 = preProbsSpan4[l].AsSpan();
                            Span<uint> countsSpan5 = countsSpan4[l].AsSpan();
                            
                            int n0 = (int)countsSpan5[Entropy.ZeroToken];
                            int n1 = (int)countsSpan5[Entropy.OneToken];
                            int n2 = (int)countsSpan5[Entropy.TwoToken];
                            int neob = (int)countsSpan5[Entropy.EobModelToken];
                            Array3<Array2<uint>> branchCt = new();
                            Span<Array2<uint>> branchCtSpan = branchCt.AsSpan();
                            Span<uint> branchCt0Span = branchCtSpan[0].AsSpan();
                            Span<uint> branchCt1Span = branchCtSpan[1].AsSpan();
                            Span<uint> branchCt2Span = branchCtSpan[2].AsSpan();
                            branchCt0Span[0] = (uint)neob;
                            branchCt0Span[1] = (uint)(eobCountsSpan4[l] - neob);
                            branchCt1Span[0] = (uint)n0;
                            branchCt1Span[1] = (uint)(n1 + n2);
                            branchCt2Span[0] = (uint)n1;
                            branchCt2Span[1] = (uint)n2;
                            for (int m = 0; m < Entropy.UnconstrainedNodes; ++m)
                            {
                                probsSpan5[m] = Prob.MergeProbs(preProbsSpan5[m], branchCt[m].AsSpan(),
                                    countSat, updateFactor);
                            }
                        }
                    }
                }
            }
        }

        public readonly void DefaultCoefProbs()
        {
            Entropy.CopyProbs(ref Fc.Value.CoefProbs[(int)TxSize.Tx4X4], Entropy.DefaultCoefProbs4X4);
            Entropy.CopyProbs(ref Fc.Value.CoefProbs[(int)TxSize.Tx8X8], Entropy.DefaultCoefProbs8X8);
            Entropy.CopyProbs(ref Fc.Value.CoefProbs[(int)TxSize.Tx16X16], Entropy.DefaultCoefProbs16X16);
            Entropy.CopyProbs(ref Fc.Value.CoefProbs[(int)TxSize.Tx32X32], Entropy.DefaultCoefProbs32X32);
        }
    }
}
