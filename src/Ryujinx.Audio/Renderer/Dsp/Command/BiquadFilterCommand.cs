using Ryujinx.Audio.Renderer.Dsp.State;
using Ryujinx.Audio.Renderer.Parameter;
using System;

namespace Ryujinx.Audio.Renderer.Dsp.Command
{
    public class BiquadFilterCommand : ICommand
    {
        public bool Enabled { get; set; }

        public int NodeId { get; private set; }

        public CommandType CommandType => CommandType.BiquadFilter;

        public uint EstimatedProcessingTime { get; set; }

        public Memory<BiquadFilterState> BiquadFilterState { get; private set; }
        public int InputBufferIndex { get; private set; }
        public int OutputBufferIndex { get; private set; }
        public bool NeedInitialization { get; private set; }

        private BiquadFilterParameter2 _parameter;

        public BiquadFilterCommand()
        {
            
        }

        public BiquadFilterCommand Initialize(
            int baseIndex,
            ref BiquadFilterParameter2 filter,
            Memory<BiquadFilterState> biquadFilterStateMemory,
            int inputBufferOffset,
            int outputBufferOffset,
            bool needInitialization,
            int nodeId)
        {
            _parameter = filter;
            BiquadFilterState = biquadFilterStateMemory;
            InputBufferIndex = baseIndex + inputBufferOffset;
            OutputBufferIndex = baseIndex + outputBufferOffset;
            NeedInitialization = needInitialization;

            Enabled = true;
            NodeId = nodeId;

            return this;
        }

        public void Process(CommandList context)
        {
            ref BiquadFilterState state = ref BiquadFilterState.Span[0];

            ReadOnlySpan<float> inputBuffer = context.GetBuffer(InputBufferIndex);
            Span<float> outputBuffer = context.GetBuffer(OutputBufferIndex);

            if (NeedInitialization)
            {
                state = new BiquadFilterState();
            }

            BiquadFilterHelper.ProcessBiquadFilter(ref _parameter, ref state, outputBuffer, inputBuffer, context.SampleCount);
        }
    }
}
