using Ryujinx.Common.Memory;
using Ryujinx.Graphics.Nvdec.Vp9.Common;
using Ryujinx.Graphics.Nvdec.Vp9.Types;
using Ryujinx.Graphics.Video;
using System;

namespace Ryujinx.Graphics.Nvdec.Vp9
{
    public sealed class Decoder : IVp9Decoder
    {
        public bool IsHardwareAccelerated => false;

        private readonly MemoryAllocator _allocator = new();
        private byte[] _lastSegmentMap = [];
        private int _lastSegmentMapMiRows;
        private int _lastSegmentMapMiCols;

        public ISurface CreateSurface(int width, int height)
        {
            return new Surface(width, height);
        }

        private static ReadOnlySpan<byte> LiteralToFilter =>
        [
            Constants.EightTapSmooth, Constants.EightTap, Constants.EightTapSharp, Constants.Bilinear
        ];

        public unsafe bool Decode(
            ref Vp9PictureInfo pictureInfo,
            ISurface output,
            ReadOnlySpan<byte> bitstream,
            ReadOnlySpan<Vp9MvRef> mvsIn,
            Span<Vp9MvRef> mvsOut)
        {
            Vp9Common cm = new()
            {
                FrameType = pictureInfo.IsKeyFrame ? FrameType.KeyFrame : FrameType.InterFrame,
                IntraOnly = pictureInfo.IntraOnly,

                Width = output.Width,
                Height = output.Height,
                SubsamplingX = 1,
                SubsamplingY = 1,

                UsePrevFrameMvs = pictureInfo.UsePrevInFindMvRefs,

                RefFrameSignBias = pictureInfo.RefFrameSignBias,

                BaseQindex = pictureInfo.BaseQIndex,
                YDcDeltaQ = pictureInfo.YDcDeltaQ,
                UvAcDeltaQ = pictureInfo.UvAcDeltaQ,
                UvDcDeltaQ = pictureInfo.UvDcDeltaQ,

                TxMode = (TxMode)pictureInfo.TransformMode,

                AllowHighPrecisionMv = pictureInfo.AllowHighPrecisionMv,

                InterpFilter = (byte)pictureInfo.InterpFilter,

                ReferenceMode = (ReferenceMode)pictureInfo.ReferenceMode,

                CompFixedRef = pictureInfo.CompFixedRef,
                CompVarRef = pictureInfo.CompVarRef,

                BitDepth = BitDepth.Bits8,

                Log2TileCols = pictureInfo.Log2TileCols,
                Log2TileRows = pictureInfo.Log2TileRows,

                Fc = new Ptr<Vp9EntropyProbs>(ref pictureInfo.Entropy),
                Counts = new Ptr<Vp9BackwardUpdates>(ref pictureInfo.BackwardUpdateCounts)
            };

            cm.Mb.Lossless = pictureInfo.Lossless;
            cm.Mb.Bd = 8;

            if (cm.InterpFilter != Constants.Switchable)
            {
                cm.InterpFilter = LiteralToFilter[cm.InterpFilter];
            }

            cm.Seg.Enabled = pictureInfo.SegmentEnabled;
            cm.Seg.UpdateMap = pictureInfo.SegmentMapUpdate;
            cm.Seg.TemporalUpdate = pictureInfo.SegmentMapTemporalUpdate;
            cm.Seg.AbsDelta = (byte)pictureInfo.SegmentAbsDelta;
            cm.Seg.FeatureMask = pictureInfo.SegmentFeatureEnable;
            cm.Seg.FeatureData = pictureInfo.SegmentFeatureData;

            cm.Lf.FilterLevel = pictureInfo.LoopFilterLevel;
            cm.Lf.SharpnessLevel = pictureInfo.LoopFilterSharpnessLevel;
            cm.Lf.ModeRefDeltaEnabled = pictureInfo.ModeRefDeltaEnabled;
            cm.Lf.RefDeltas = pictureInfo.RefDeltas;
            cm.Lf.ModeDeltas = pictureInfo.ModeDeltas;

            Span<RefBuffer> frameRefsSpan = cm.FrameRefs.AsSpan();
            frameRefsSpan[0].Buf = (Surface)pictureInfo.LastReference;
            frameRefsSpan[1].Buf = (Surface)pictureInfo.GoldenReference;
            frameRefsSpan[2].Buf = (Surface)pictureInfo.AltReference;
            cm.Mb.CurBuf = (Surface)output;

            cm.Mb.SetupBlockPlanes(1, 1);

            int tileCols = 1 << pictureInfo.Log2TileCols;
            int tileRows = 1 << pictureInfo.Log2TileRows;

            // Video usually have only 4 columns, so more threads won't make a difference for those.
            // Try to not take all CPU cores for video decoding.
            int maxThreads = Math.Min(4, Environment.ProcessorCount / 2);

            cm.AllocTileWorkerData(_allocator, tileCols, tileRows, maxThreads);
            cm.AllocContextBuffers(_allocator, output.Width, output.Height);

            cm.InitContextBuffers();
            LoadSegmentMap(ref cm, ref pictureInfo);
            cm.SetupSegmentationDequant();
            cm.SetupScaleFactors();

            cm.SetMvs(mvsIn);

            if (cm.Lf.FilterLevel != 0 && cm.SkipLoopFilter == 0)
            {
                LoopFilter.LoopFilterFrameInit(ref cm, cm.Lf.FilterLevel);
            }

            fixed (byte* dataPtr = bitstream)
            {
                try
                {
                    if (maxThreads > 1 && tileRows == 1 && tileCols > 1)
                    {
                        DecodeFrame.DecodeTilesMt(ref cm, new ArrayPtr<byte>(dataPtr, bitstream.Length), maxThreads);

                        LoopFilter.LoopFilterFrameMt(
                            ref cm.Mb.CurBuf,
                            ref cm,
                            ref cm.Mb,
                            cm.Lf.FilterLevel,
                            false,
                            false,
                            maxThreads);
                    }
                    else
                    {
                        DecodeFrame.DecodeTiles(ref cm, new ArrayPtr<byte>(dataPtr, bitstream.Length));

                        LoopFilter.LoopFilterFrame(
                            ref cm.Mb.CurBuf,
                            ref cm,
                            ref cm.Mb,
                            cm.Lf.FilterLevel,
                            false,
                            false);
                    }
                }
                catch (InternalErrorException)
                {
                    return false;
                }
            }

            SaveSegmentMap(ref cm);
            cm.GetMvs(mvsOut);

            cm.FreeTileWorkerData(_allocator);
            cm.FreeContextBuffers(_allocator);

            return true;
        }

        private void LoadSegmentMap(ref Vp9Common cm, ref Vp9PictureInfo pictureInfo)
        {
            int mapLength = cm.MiRows * cm.MiCols;
            if (mapLength == 0 || cm.LastFrameSegMap.IsNull)
            {
                return;
            }

            bool reset =
                pictureInfo.IsKeyFrame ||
                pictureInfo.IntraOnly ||
                _lastSegmentMapMiRows != cm.MiRows ||
                _lastSegmentMapMiCols != cm.MiCols;

            if (_lastSegmentMap.Length != mapLength)
            {
                _lastSegmentMap = new byte[mapLength];
            }
            else if (reset)
            {
                Array.Clear(_lastSegmentMap);
            }

            _lastSegmentMapMiRows = cm.MiRows;
            _lastSegmentMapMiCols = cm.MiCols;
            _lastSegmentMap.CopyTo(cm.LastFrameSegMap.AsSpan());
        }

        private void SaveSegmentMap(ref Vp9Common cm)
        {
            int mapLength = cm.MiRows * cm.MiCols;
            if (!cm.Seg.Enabled || mapLength == 0 || cm.CurrentFrameSegMap.IsNull)
            {
                return;
            }

            if (_lastSegmentMap.Length != mapLength)
            {
                _lastSegmentMap = new byte[mapLength];
            }

            cm.CurrentFrameSegMap.AsSpan()[..mapLength].CopyTo(_lastSegmentMap);
            _lastSegmentMapMiRows = cm.MiRows;
            _lastSegmentMapMiCols = cm.MiCols;
        }

        public void Dispose()
        {
            _allocator.Dispose();
        }
    }
}
