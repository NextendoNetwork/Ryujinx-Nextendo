using Ryujinx.Audio.Renderer.Common;
using Ryujinx.Audio.Renderer.Dsp;
using Ryujinx.Audio.Renderer.Dsp.State;
using Ryujinx.Audio.Renderer.Parameter;
using Ryujinx.Audio.Renderer.Parameter.Effect;
using Ryujinx.Audio.Renderer.Server.MemoryPool;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ryujinx.Audio.Renderer.Server.Effect
{
    /// <summary>
    /// Server state for a biquad filter effect.
    /// </summary>
    public class BiquadFilterEffect : BaseEffect
    {
        /// <summary>
        /// The biquad filter parameter.
        /// </summary>
        public BiquadFilterEffectParameter2 Parameter;

        /// <summary>
        /// The biquad filter state.
        /// </summary>
        public Memory<BiquadFilterState> State { get; }

        /// <summary>
        /// The biquad filter effect version.
        /// </summary>
        public int BiquadFilterEffectVersion;

        /// <summary>
        /// Create a new <see cref="BiquadFilterEffect"/>.
        /// </summary>
        public BiquadFilterEffect(int version)
        {
            Parameter = new BiquadFilterEffectParameter2();
            State = new BiquadFilterState[Constants.ChannelCountMax];
            BiquadFilterEffectVersion = version;
        }

        public override EffectType TargetEffectType => EffectType.BiquadFilter;

        public override void Update(out BehaviourParameter.ErrorInfo updateErrorInfo, in EffectInParameterVersion1 parameter, PoolMapper mapper)
        {
            Update(out updateErrorInfo, in parameter, mapper);
        }

        public override void Update(out BehaviourParameter.ErrorInfo updateErrorInfo, in EffectInParameterVersion2 parameter, PoolMapper mapper)
        {
            Update(out updateErrorInfo, in parameter, mapper);
        }

        public void Update<T>(out BehaviourParameter.ErrorInfo updateErrorInfo, in T parameter, PoolMapper mapper) where T : unmanaged, IEffectInParameter
        {
            Debug.Assert(IsTypeValid(in parameter));

            UpdateParameterBase(in parameter);

            if (BiquadFilterEffectVersion == 2)
            {
                Parameter = MemoryMarshal.Cast<byte, BiquadFilterEffectParameter2>(parameter.SpecificData)[0];
            }
            else
            {
                BiquadFilterEffectParameter1 oldParameter =
                    MemoryMarshal.Cast<byte, BiquadFilterEffectParameter1>(parameter.SpecificData)[0];
                Parameter = BiquadFilterHelper.ToBiquadFilterEffectParameter2(oldParameter);
            }
            
            IsEnabled = parameter.IsEnabled;

            updateErrorInfo = new BehaviourParameter.ErrorInfo();
        }

        public override void UpdateForCommandGeneration()
        {
            UpdateUsageStateForCommandGeneration();

            Parameter.Status = UsageState.Enabled;
        }
    }
}
