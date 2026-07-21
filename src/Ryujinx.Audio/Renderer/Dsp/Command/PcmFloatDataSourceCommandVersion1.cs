using Ryujinx.Audio.Common;
using Ryujinx.Audio.Renderer.Common;
using Ryujinx.Audio.Renderer.Server.Voice;
using System;
using Ryujinx.Audio.Renderer.Parameter;
using WaveBuffer = Ryujinx.Audio.Renderer.Common.WaveBuffer;

namespace Ryujinx.Audio.Renderer.Dsp.Command
{
    public class PcmFloatDataSourceCommandVersion1 : ICommand
    {
        public bool Enabled { get; set; }

        public int NodeId { get; private set; }

        public CommandType CommandType => CommandType.PcmFloatDataSourceVersion1;

        public uint EstimatedProcessingTime { get; set; }

        public ushort OutputBufferIndex { get; private set; }
        public uint SampleRate { get; private set; }
        public uint ChannelIndex { get; private set; }

        public uint ChannelCount { get; private set; }

        public float Pitch { get; private set; }

        public WaveBuffer[] WaveBuffers { get; }

        public Memory<VoiceState> State { get; private set; }
        public DecodingBehaviour DecodingBehaviour { get; private set; }

        public PcmFloatDataSourceCommandVersion1()
        {
            WaveBuffers = new WaveBuffer[Constants.VoiceWaveBufferCount];
        }

        public PcmFloatDataSourceCommandVersion1 Initialize(ref VoiceInfo serverInfo, Memory<VoiceState> state, ushort outputBufferIndex, ushort channelIndex, int nodeId)
        {
            Enabled = true;
            NodeId = nodeId;

            OutputBufferIndex = (ushort)(channelIndex + outputBufferIndex);
            SampleRate = serverInfo.SampleRate;
            ChannelIndex = channelIndex;
            ChannelCount = serverInfo.ChannelsCount;
            Pitch = serverInfo.Pitch;
            
            Span<Server.Voice.WaveBuffer> waveBufferSpan = serverInfo.WaveBuffers.AsSpan();

            for (int i = 0; i < WaveBuffers.Length; i++)
            {
                ref Server.Voice.WaveBuffer voiceWaveBuffer = ref waveBufferSpan[i];

                WaveBuffers[i] = voiceWaveBuffer.ToCommon(1);
            }

            State = state;
            DecodingBehaviour = serverInfo.DecodingBehaviour;

            return this;
        }

        public void Process(CommandList context)
        {
            Span<float> outputBuffer = context.GetBuffer(OutputBufferIndex);

            DataSourceHelper.WaveBufferInformation info = new()
            {
                SourceSampleRate = SampleRate,
                SampleFormat = SampleFormat.PcmFloat,
                Pitch = Pitch,
                DecodingBehaviour = DecodingBehaviour,
                ExtraParameter = 0,
                ExtraParameterSize = 0,
                ChannelIndex = (int)ChannelIndex,
                ChannelCount = (int)ChannelCount,
            };

            DataSourceHelper.ProcessWaveBuffers(context.MemoryManager, outputBuffer, ref info, WaveBuffers, ref State.Span[0], context.SampleRate, (int)context.SampleCount);
        }
    }
}
