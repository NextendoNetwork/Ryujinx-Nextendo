using Ryujinx.Audio.Renderer.Server.Upsampler;
using System;

namespace Ryujinx.Audio.Renderer.Dsp.Command
{
    public class UpsampleCommand : ICommand
    {
        public bool Enabled { get; set; }

        public int NodeId { get; private set; }

        public CommandType CommandType => CommandType.Upsample;

        public uint EstimatedProcessingTime { get; set; }

        public uint BufferCount { get; private set; }
        public uint InputBufferIndex { get; private set; }
        public uint InputSampleCount { get; private set; }
        public uint InputSampleRate { get; private set; }

        public UpsamplerInfo UpsamplerInfo { get; private set; }

        public Memory<float> OutBuffer { get; private set; }

        public UpsampleCommand()
        {
            
        }

        public UpsampleCommand Initialize(uint bufferOffset, UpsamplerInfo info, uint inputCount, Span<byte> inputBufferOffset, uint bufferCount, uint sampleCount, uint sampleRate, int nodeId)
        {
            Enabled = true;
            NodeId = nodeId;

            InputBufferIndex = 0;
            OutBuffer = info.OutputBuffer;
            BufferCount = bufferCount;
            InputSampleCount = sampleCount;
            InputSampleRate = sampleRate;
            info.SourceSampleCount = inputCount;
            info.InputBufferIndices = new ushort[inputCount];

            for (int i = 0; i < inputCount; i++)
            {
                info.InputBufferIndices[i] = (ushort)(bufferOffset + inputBufferOffset[i]);
            }

            if (info.BufferStates?.Length != (int)inputCount)
            {
                // Keep state if possible.
                info.BufferStates = new UpsamplerBufferState[(int)inputCount];
            }

            UpsamplerInfo = info;

            return this;
        }

        private Span<float> GetBuffer(int index, int sampleCount)
        {
            return UpsamplerInfo.OutputBuffer.Span.Slice(index * sampleCount, sampleCount);
        }

        public void Process(CommandList context)
        {
            uint bufferCount = Math.Min(BufferCount, UpsamplerInfo.SourceSampleCount);

            for (int i = 0; i < bufferCount; i++)
            {
                Span<float> inputBuffer = context.GetBuffer(UpsamplerInfo.InputBufferIndices[i]);
                Span<float> outputBuffer = GetBuffer(UpsamplerInfo.InputBufferIndices[i], (int)UpsamplerInfo.SampleCount);

                UpsamplerHelper.Upsample(outputBuffer, inputBuffer, (int)UpsamplerInfo.SampleCount, (int)InputSampleCount, ref UpsamplerInfo.BufferStates[i]);
            }
        }
    }
}
