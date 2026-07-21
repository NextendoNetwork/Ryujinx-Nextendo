using Ryujinx.Audio.Renderer.Dsp.State;
using Ryujinx.Audio.Renderer.Parameter;
using Ryujinx.Audio.Renderer.Parameter.Effect;
using Ryujinx.Audio.Renderer.Server.Effect;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ryujinx.Audio.Renderer.Dsp.Command
{
    public class LimiterCommandVersion2 : ICommand
    {
        public bool Enabled { get; set; }

        public int NodeId { get; private set; }

        public CommandType CommandType => CommandType.LimiterVersion2;

        public uint EstimatedProcessingTime { get; set; }

        public LimiterParameter Parameter => _parameter;
        public Memory<LimiterState> State { get; private set; }
        public Memory<EffectResultState> ResultState { get; private set; }
        public ulong WorkBuffer { get; private set; }
        public ushort[] OutputBufferIndices { get; }
        public ushort[] InputBufferIndices { get; }
        public bool IsEffectEnabled { get; private set; }

        private LimiterParameter _parameter;

        public LimiterCommandVersion2()
        {
            InputBufferIndices = new ushort[Constants.VoiceChannelCountMax];
            OutputBufferIndices = new ushort[Constants.VoiceChannelCountMax];
        }
        
        public LimiterCommandVersion2 Initialize(
            uint bufferOffset,
            LimiterParameter parameter,
            Memory<LimiterState> state,
            Memory<EffectResultState> resultState,
            bool isEnabled,
            ulong workBuffer,
            int nodeId)
        {
            Enabled = true;
            NodeId = nodeId;
            _parameter = parameter;
            State = state;
            ResultState = resultState;
            WorkBuffer = workBuffer;

            IsEffectEnabled = isEnabled;
            
            Span<byte> inputSpan = _parameter.Input.AsSpan();
            Span<byte> outputSpan = _parameter.Output.AsSpan();

            for (int i = 0; i < _parameter.ChannelCount; i++)
            {
                InputBufferIndices[i] = (ushort)(bufferOffset + inputSpan[i]);
                OutputBufferIndices[i] = (ushort)(bufferOffset + outputSpan[i]);
            }
            
            return this;
        }

        public void Process(CommandList context)
        {
            ref LimiterState state = ref State.Span[0];

            if (IsEffectEnabled)
            {
                if (_parameter.Status == UsageState.Invalid)
                {
                    state = new LimiterState(ref _parameter, WorkBuffer);
                }
                else if (_parameter.Status == UsageState.New)
                {
                    LimiterState.UpdateParameter(ref _parameter);
                }
            }

            ProcessLimiter(context, ref state);
        }

        private unsafe void ProcessLimiter(CommandList context, ref LimiterState state)
        {
            Debug.Assert(_parameter.IsChannelCountValid());

            if (IsEffectEnabled && _parameter.IsChannelCountValid())
            {
                if (!ResultState.IsEmpty && _parameter.StatisticsReset)
                {
                    ref LimiterStatistics statistics = ref MemoryMarshal.Cast<byte, LimiterStatistics>(ResultState.Span[0].SpecificData)[0];

                    statistics.Reset();
                }

                Span<nint> inputBuffers = stackalloc nint[_parameter.ChannelCount];
                Span<nint> outputBuffers = stackalloc nint[_parameter.ChannelCount];

                for (int i = 0; i < _parameter.ChannelCount; i++)
                {
                    inputBuffers[i] = context.GetBufferPointer(InputBufferIndices[i]);
                    outputBuffers[i] = context.GetBufferPointer(OutputBufferIndices[i]);
                }

                for (int channelIndex = 0; channelIndex < _parameter.ChannelCount; channelIndex++)
                {
                    for (int sampleIndex = 0; sampleIndex < context.SampleCount; sampleIndex++)
                    {
                        float rawInputSample = *((float*)inputBuffers[channelIndex] + sampleIndex);

                        float inputSample = (rawInputSample / short.MaxValue) * _parameter.InputGain;

                        float sampleInputMax = Math.Abs(inputSample);

                        float inputCoefficient = _parameter.ReleaseCoefficient;

                        if (sampleInputMax > state.DetectorAverage[channelIndex].Read())
                        {
                            inputCoefficient = _parameter.AttackCoefficient;
                        }

                        float detectorValue = state.DetectorAverage[channelIndex].Update(sampleInputMax, inputCoefficient);
                        float attenuation = 1.0f;

                        if (detectorValue > _parameter.Threshold)
                        {
                            attenuation = _parameter.Threshold / detectorValue;
                        }

                        float outputCoefficient = _parameter.ReleaseCoefficient;

                        if (state.CompressionGainAverage[channelIndex].Read() > attenuation)
                        {
                            outputCoefficient = _parameter.AttackCoefficient;
                        }

                        float compressionGain = state.CompressionGainAverage[channelIndex].Update(attenuation, outputCoefficient);

                        ref float delayedSample = ref state.DelayedSampleBuffer[channelIndex * _parameter.DelayBufferSampleCountMax + state.DelayedSampleBufferPosition[channelIndex]];

                        float outputSample = delayedSample * compressionGain * _parameter.OutputGain;

                        *((float*)outputBuffers[channelIndex] + sampleIndex) = outputSample * short.MaxValue;

                        delayedSample = inputSample;

                        state.DelayedSampleBufferPosition[channelIndex]++;

                        while (state.DelayedSampleBufferPosition[channelIndex] >= _parameter.DelayBufferSampleCountMin)
                        {
                            state.DelayedSampleBufferPosition[channelIndex] -= _parameter.DelayBufferSampleCountMin;
                        }

                        if (!ResultState.IsEmpty)
                        {
                            ref LimiterStatistics statistics = ref MemoryMarshal.Cast<byte, LimiterStatistics>(ResultState.Span[0].SpecificData)[0];

                            Span<float> inputMaxSpan = statistics.InputMax.AsSpan();
                            Span<float> compressionGainMinSpan = statistics.CompressionGainMin.AsSpan();
                            
                            inputMaxSpan[channelIndex] = Math.Max(inputMaxSpan[channelIndex], sampleInputMax);
                            compressionGainMinSpan[channelIndex] = Math.Min(compressionGainMinSpan[channelIndex], compressionGain);
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < _parameter.ChannelCount; i++)
                {
                    if (InputBufferIndices[i] != OutputBufferIndices[i])
                    {
                        context.CopyBuffer(OutputBufferIndices[i], InputBufferIndices[i]);
                    }
                }
            }
        }
    }
}
