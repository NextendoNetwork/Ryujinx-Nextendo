using Ryujinx.Audio.Renderer.Common;
using System;
using System.Runtime.CompilerServices;

namespace Ryujinx.Audio.Renderer.Dsp.Command
{
    public class MixRampCommand : ICommand
    {
        public bool Enabled { get; set; }

        public int NodeId { get; private set; }

        public CommandType CommandType => CommandType.MixRamp;

        public uint EstimatedProcessingTime { get; set; }

        public ushort InputBufferIndex { get; private set; }
        public ushort OutputBufferIndex { get; private set; }

        public float Volume0 { get; private set; }
        public float Volume1 { get; private set; }

        public Memory<VoiceState> State { get; private set; }

        public int LastSampleIndex { get; private set; }

        public MixRampCommand()
        {
            
        }

        public MixRampCommand Initialize(float volume0, float volume1, uint inputBufferIndex, uint outputBufferIndex, int lastSampleIndex, Memory<VoiceState> state, int nodeId)
        {
            Enabled = true;
            NodeId = nodeId;

            InputBufferIndex = (ushort)inputBufferIndex;
            OutputBufferIndex = (ushort)outputBufferIndex;

            Volume0 = volume0;
            Volume1 = volume1;

            State = state;
            LastSampleIndex = lastSampleIndex;

            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float ProcessMixRamp(Span<float> outputBuffer, ReadOnlySpan<float> inputBuffer, int sampleCount)
        {
            float ramp = (Volume1 - Volume0) / sampleCount;
            float volume = Volume0;
            float state = 0;

            for (int i = 0; i < sampleCount; i++)
            {
                state = FloatingPointHelper.MultiplyRoundUp(inputBuffer[i], volume);

                outputBuffer[i] += state;
                volume += ramp;
            }

            return state;
        }

        public void Process(CommandList context)
        {
            ReadOnlySpan<float> inputBuffer = context.GetBuffer(InputBufferIndex);
            Span<float> outputBuffer = context.GetBuffer(OutputBufferIndex);

            State.Span[0].LastSamples[LastSampleIndex] = ProcessMixRamp(outputBuffer, inputBuffer, (int)context.SampleCount);
        }
    }
}
