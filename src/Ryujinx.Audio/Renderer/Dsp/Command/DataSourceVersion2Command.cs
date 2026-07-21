using Ryujinx.Audio.Common;
using Ryujinx.Audio.Renderer.Common;
using Ryujinx.Audio.Renderer.Server.Voice;
using System;
using Ryujinx.Audio.Renderer.Parameter;
using WaveBuffer = Ryujinx.Audio.Renderer.Common.WaveBuffer;

namespace Ryujinx.Audio.Renderer.Dsp.Command
{
    public class DataSourceVersion2Command : ICommand
    {
        public bool Enabled { get; set; }

        public int NodeId { get; private set; }

        public CommandType CommandType { get; private set; }

        public uint EstimatedProcessingTime { get; set; }

        public ushort OutputBufferIndex { get; private set; }
        public uint SampleRate { get; private set; }

        public float Pitch { get; private set; }

        public WaveBuffer[] WaveBuffers { get; }

        public Memory<VoiceState> State { get; private set; }

        public ulong ExtraParameter { get; private set; }
        public ulong ExtraParameterSize { get; private set; }

        public uint ChannelIndex { get; private set; }

        public uint ChannelCount { get; private set; }

        public DecodingBehaviour DecodingBehaviour { get; private set; }

        public SampleFormat SampleFormat { get; private set; }

        public SampleRateConversionQuality SrcQuality { get; private set; }

        public DataSourceVersion2Command()
        {
            WaveBuffers = new WaveBuffer[Constants.VoiceWaveBufferCount];
        }

        public DataSourceVersion2Command Initialize(ref VoiceInfo serverInfo, Memory<VoiceState> state, ushort outputBufferIndex, ushort channelIndex, int nodeId)
        {
            Enabled = true;
            NodeId = nodeId;
            ChannelIndex = channelIndex;
            ChannelCount = serverInfo.ChannelsCount;
            SampleFormat = serverInfo.SampleFormat;
            SrcQuality = serverInfo.SrcQuality;
            CommandType = GetCommandTypeBySampleFormat(SampleFormat);

            OutputBufferIndex = (ushort)(channelIndex + outputBufferIndex);
            SampleRate = serverInfo.SampleRate;
            Pitch = serverInfo.Pitch;
            
            Span<Server.Voice.WaveBuffer> waveBufferSpan = serverInfo.WaveBuffers.AsSpan();

            for (int i = 0; i < WaveBuffers.Length; i++)
            {
                ref Server.Voice.WaveBuffer voiceWaveBuffer = ref waveBufferSpan[i];

                WaveBuffers[i] = voiceWaveBuffer.ToCommon(2);
            }

            if (SampleFormat == SampleFormat.Adpcm)
            {
                ExtraParameter = serverInfo.DataSourceStateAddressInfo.GetReference(true);
                ExtraParameterSize = serverInfo.DataSourceStateAddressInfo.Size;
            }

            State = state;
            DecodingBehaviour = serverInfo.DecodingBehaviour;

            return this;
        }

        private static CommandType GetCommandTypeBySampleFormat(SampleFormat sampleFormat)
        {
            return sampleFormat switch
            {
                SampleFormat.Adpcm => CommandType.AdpcmDataSourceVersion2,
                SampleFormat.PcmInt16 => CommandType.PcmInt16DataSourceVersion2,
                SampleFormat.PcmFloat => CommandType.PcmFloatDataSourceVersion2,
                _ => throw new NotImplementedException($"{sampleFormat}"),
            };
        }

        public void Process(CommandList context)
        {
            Span<float> outputBuffer = context.GetBuffer(OutputBufferIndex);

            DataSourceHelper.WaveBufferInformation info = new()
            {
                SourceSampleRate = SampleRate,
                SampleFormat = SampleFormat,
                Pitch = Pitch,
                DecodingBehaviour = DecodingBehaviour,
                ExtraParameter = ExtraParameter,
                ExtraParameterSize = ExtraParameterSize,
                ChannelIndex = (int)ChannelIndex,
                ChannelCount = (int)ChannelCount,
                SrcQuality = SrcQuality,
            };

            DataSourceHelper.ProcessWaveBuffers(context.MemoryManager, outputBuffer, ref info, WaveBuffers, ref State.Span[0], context.SampleRate, (int)context.SampleCount);
        }
    }
}
