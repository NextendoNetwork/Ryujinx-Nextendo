using Ryujinx.Common.Memory;
using System;

namespace Ryujinx.Graphics.Nvdec.Vp9.Types
{
    internal struct LoopFilter
    {
        public int FilterLevel;
        public int LastFiltLevel;

        public int SharpnessLevel;
        public int LastSharpnessLevel;

        public bool ModeRefDeltaEnabled;
        public bool ModeRefDeltaUpdate;

        // 0 = Intra, Last, GF, ARF
        public Array4<sbyte> RefDeltas;
        public Array4<sbyte> LastRefDeltas;

        // 0 = ZERO_MV, MV
        public Array2<sbyte> ModeDeltas;
        public Array2<sbyte> LastModeDeltas;

        public ArrayPtr<LoopFilterMask> Lfm;
        public int LfmStride;

        public void SetDefaultLfDeltas()
        {
            ModeRefDeltaEnabled = true;
            ModeRefDeltaUpdate = true;
            
            Span<sbyte> refDeltasSpan = RefDeltas.AsSpan();
            Span<sbyte> modeDeltasSpan = ModeDeltas.AsSpan();

            refDeltasSpan[Constants.IntraFrame] = 1;
            refDeltasSpan[Constants.LastFrame] = 0;
            refDeltasSpan[Constants.GoldenFrame] = -1;
            refDeltasSpan[Constants.AltRefFrame] = -1;
            modeDeltasSpan[0] = 0;
            modeDeltasSpan[1] = 0;
        }
    }
}
