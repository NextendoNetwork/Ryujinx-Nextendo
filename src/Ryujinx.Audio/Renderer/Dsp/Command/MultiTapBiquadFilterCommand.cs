using Ryujinx.Audio.Renderer.Dsp.State;
using Ryujinx.Audio.Renderer.Parameter;
using System;

namespace Ryujinx.Audio.Renderer.Dsp.Command
{
    public class MultiTapBiquadFilterCommand : ICommand
    {
        public bool Enabled { get; set; }

        public int NodeId { get; private set; }

        public CommandType CommandType => CommandType.MultiTapBiquadFilter;

        public uint EstimatedProcessingTime { get; set; }

        public BiquadFilterParameter2[] Parameters { get; private set; }
        public Memory<BiquadFilterState> BiquadFilterStates { get; private set; }
        public int InputBufferIndex { get; private set; }
        public int OutputBufferIndex { get; private set; }
        public bool[] IsInitialized { get; private set; }

        public MultiTapBiquadFilterCommand()
        {
            
        }

        public MultiTapBiquadFilterCommand Initialize(int baseIndex, ReadOnlySpan<BiquadFilterParameter2> filters, Memory<BiquadFilterState> biquadFilterStateMemory, int inputBufferOffset, int outputBufferOffset, ReadOnlySpan<bool> isInitialized, int nodeId)
        {
            Parameters = filters.ToArray();
            BiquadFilterStates = biquadFilterStateMemory;
            InputBufferIndex = baseIndex + inputBufferOffset;
            OutputBufferIndex = baseIndex + outputBufferOffset;
            IsInitialized = isInitialized.ToArray();

            Enabled = true;
            NodeId = nodeId;
            
            return this;
        }

        public void Process(CommandList context)
        {
            Span<BiquadFilterState> states = BiquadFilterStates.Span;

            ReadOnlySpan<float> inputBuffer = context.GetBuffer(InputBufferIndex);
            Span<float> outputBuffer = context.GetBuffer(OutputBufferIndex);

            for (int i = 0; i < Parameters.Length; i++)
            {
                if (!IsInitialized[i])
                {
                    states[i] = new BiquadFilterState();
                }
            }

            // NOTE: Nintendo only implement single and double biquad filters but no generic path when the command definition suggests it could be done.
            // As such we currently only implement a generic path for simplicity for double biquad.
            if (Parameters.Length == 1)
            {
                BiquadFilterHelper.ProcessBiquadFilter(ref Parameters[0], ref states[0], outputBuffer, inputBuffer, context.SampleCount);
            }
            else
            {
                BiquadFilterHelper.ProcessBiquadFilter(Parameters, states, outputBuffer, inputBuffer, context.SampleCount);
            }
        }
    }
}
