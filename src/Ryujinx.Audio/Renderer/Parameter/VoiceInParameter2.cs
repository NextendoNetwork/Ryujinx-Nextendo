using Ryujinx.Audio.Common;
using Ryujinx.Audio.Renderer.Common;
using Ryujinx.Common.Memory;
using System.Runtime.InteropServices;

namespace Ryujinx.Audio.Renderer.Parameter
{
    /// <summary>
    /// Input information for a voice.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 0x188, Pack = 1)]
    public struct VoiceInParameter2
    {
        /// <summary>
        /// Id of the voice.
        /// </summary>
        public int Id;

        /// <summary>
        /// Node id of the voice.
        /// </summary>
        public int NodeId;

        /// <summary>
        /// Set to true if the voice is new.
        /// </summary>
        [MarshalAs(UnmanagedType.I1)]
        public bool IsNew;

        /// <summary>
        /// Set to true if the voice is used.
        /// </summary>
        [MarshalAs(UnmanagedType.I1)]
        public bool InUse;

        /// <summary>
        /// The voice <see cref="PlayState"/> wanted by the user.
        /// </summary>
        public PlayState PlayState;

        /// <summary>
        /// The <see cref="SampleFormat"/> of the voice.
        /// </summary>
        public SampleFormat SampleFormat;

        /// <summary>
        /// The sample rate of the voice.
        /// </summary>
        public uint SampleRate;

        /// <summary>
        /// The priority of the voice.
        /// </summary>
        public uint Priority;

        /// <summary>
        /// Target sorting position of the voice. (Used to sort voices with the same <see cref="Priority"/>)
        /// </summary>
        public uint SortingOrder;

        /// <summary>
        /// The total channel count used.
        /// </summary>
        public uint ChannelCount;

        /// <summary>
        /// The pitch used on the voice.
        /// </summary>
        public float Pitch;

        /// <summary>
        /// The output volume of the voice.
        /// </summary>
        public float Volume;

        /// <summary>
        /// Biquad filters to apply to the output of the voice.
        /// </summary>
        public Array2<BiquadFilterParameter2> BiquadFilters;

        /// <summary>
        /// Total count of <see cref="WaveBufferInternal"/> of the voice.
        /// </summary>
        public uint WaveBuffersCount;

        /// <summary>
        /// Current playing <see cref="WaveBufferInternal"/> of the voice.
        /// </summary>
        public uint WaveBuffersIndex;

        /// <summary>
        /// Reserved/unused.
        /// </summary>
        private readonly uint 
            _reserved1;

        /// <summary>
        /// User state address required by the data source.
        /// </summary>
        /// <remarks>Only used for <see cref="SampleFormat.Adpcm"/> as the address of the GC-ADPCM coefficients.</remarks>
        public ulong DataSourceStateAddress;

        /// <summary>
        /// User state size required by the data source.
        /// </summary>
        /// <remarks>Only used for <see cref="SampleFormat.Adpcm"/> as the size of the GC-ADPCM coefficients.</remarks>
        public ulong DataSourceStateSize;

        /// <summary>
        /// The target mix id of the voice.
        /// </summary>
        public int MixId;

        /// <summary>
        /// The target splitter id of the voice.
        /// </summary>
        public uint SplitterId;

        /// <summary>
        /// The wavebuffer parameters of this voice.
        /// </summary>
        public Array4<WaveBufferInternal> WaveBuffers;

        /// <summary>
        /// The channel resource ids associated to the voice.
        /// </summary>
        public Array6<int> ChannelResourceIds;

        /// <summary>
        /// Reset the voice drop flag during voice server update.
        /// </summary>
        [MarshalAs(UnmanagedType.I1)]
        public bool ResetVoiceDropFlag;

        /// <summary>
        /// Flush the amount of wavebuffer specified. This will result in the wavebuffer being skipped and marked played.
        /// </summary>
        /// <remarks>This was added on REV5.</remarks>
        public byte FlushWaveBufferCount;

        /// <summary>
        /// Reserved/unused.
        /// </summary>
        private readonly ushort _reserved2;

        /// <summary>
        /// Change the behaviour of the voice.
        /// </summary>
        /// <remarks>This was added on REV5.</remarks>
        public DecodingBehaviour DecodingBehaviourFlags;

        /// <summary>
        /// Change the Sample Rate Conversion (SRC) quality of the voice.
        /// </summary>
        /// <remarks>This was added on REV8.</remarks>
        public SampleRateConversionQuality SrcQuality;

        /// <summary>
        /// This was previously used for opus codec support on the Audio Renderer and was removed on REV3.
        /// </summary>
        public uint ExternalContext;

        /// <summary>
        /// This was previously used for opus codec support on the Audio Renderer and was removed on REV3.
        /// </summary>
        public uint ExternalContextSize;

        /// <summary>
        /// Reserved/unused.
        /// </summary>
        private unsafe fixed uint _reserved3[2];
    }
}
