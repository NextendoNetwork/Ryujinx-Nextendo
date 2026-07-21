using Ryujinx.Audio.Renderer.Parameter.Sink;
using Ryujinx.Audio.Renderer.Server.MemoryPool;
using System;
using System.Diagnostics;

namespace Ryujinx.Audio.Renderer.Dsp.Command
{
    public class CircularBufferSinkCommand : ICommand
    {
        public bool Enabled { get; set; }

        public int NodeId { get; private set; }

        public CommandType CommandType => CommandType.CircularBufferSink;

        public uint EstimatedProcessingTime { get; set; }

        public ushort[] Input { get; }
        public uint InputCount { get; private set; }

        public ulong CircularBuffer { get; private set; }
        public ulong CircularBufferSize { get; private set; }
        public ulong CurrentOffset { get; private set; }

        public CircularBufferSinkCommand()
        {
            Input = new ushort[Constants.ChannelCountMax];
        }
        
        public CircularBufferSinkCommand Initialize(uint bufferOffset, ref CircularBufferParameter parameter, ref AddressInfo circularBufferAddressInfo, uint currentOffset, int nodeId)
        {
            Enabled = true;
            NodeId = nodeId;

            InputCount = parameter.InputCount;
            
            Span<byte> inputSpan = parameter.Input.AsSpan();

            for (int i = 0; i < InputCount; i++)
            {
                Input[i] = (ushort)(bufferOffset + inputSpan[i]);
            }

            CircularBuffer = circularBufferAddressInfo.GetReference(true);
            CircularBufferSize = parameter.BufferSize;
            CurrentOffset = currentOffset;

            Debug.Assert(CircularBuffer != 0);

            return this;
        }

        public void Process(CommandList context)
        {
            const int TargetChannelCount = 2;

            ulong currentOffset = CurrentOffset;

            if (CircularBufferSize > 0)
            {
                for (int i = 0; i < InputCount; i++)
                {
                    unsafe
                    {
                        float* inputBuffer = (float*)context.GetBufferPointer(Input[i]);

                        ulong targetOffset = CircularBuffer + currentOffset;

                        for (int y = 0; y < context.SampleCount; y++)
                        {
                            context.MemoryManager.Write(targetOffset + (ulong)y * TargetChannelCount, PcmHelper.Saturate(inputBuffer[y]));
                        }

                        currentOffset += context.SampleCount * TargetChannelCount;

                        if (currentOffset >= CircularBufferSize)
                        {
                            currentOffset = 0;
                        }
                    }
                }
            }
        }
    }
}
