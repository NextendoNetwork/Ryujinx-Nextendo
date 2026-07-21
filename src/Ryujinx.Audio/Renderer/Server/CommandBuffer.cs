using Ryujinx.Audio.Renderer.Common;
using Ryujinx.Audio.Renderer.Dsp.Command;
using Ryujinx.Audio.Renderer.Dsp.State;
using Ryujinx.Audio.Renderer.Parameter;
using Ryujinx.Audio.Renderer.Parameter.Effect;
using Ryujinx.Audio.Renderer.Server.Performance;
using Ryujinx.Audio.Renderer.Server.Sink;
using Ryujinx.Audio.Renderer.Server.Splitter;
using Ryujinx.Audio.Renderer.Server.Upsampler;
using Ryujinx.Audio.Renderer.Server.Voice;
using Ryujinx.Common;
using System;
using CpuAddress = System.UInt64;

namespace Ryujinx.Audio.Renderer.Server
{
    /// <summary>
    /// An API to generate commands and aggregate them into a <see cref="CommandList"/>.
    /// </summary>
    public class CommandBuffer
    {
        /// <summary>
        /// The command processing time estimator in use.
        /// </summary>
        private readonly ICommandProcessingTimeEstimator _commandProcessingTimeEstimator;

        /// <summary>
        /// The estimated total processing time.
        /// </summary>
        public uint EstimatedProcessingTime { get; set; }

        /// <summary>
        /// The command list that is populated by the <see cref="CommandBuffer"/>.
        /// </summary>
        public CommandList CommandList { get; }

        private readonly static ObjectPool<PcmInt16DataSourceCommandVersion1> _pcmInt16DataSourceCommandVersion1Pool = new(() => new PcmInt16DataSourceCommandVersion1());
        private readonly static ObjectPool<PcmFloatDataSourceCommandVersion1> _pcmFloatDataSourceCommandVersion1Pool = new(() => new PcmFloatDataSourceCommandVersion1());
        private readonly static ObjectPool<AdpcmDataSourceCommandVersion1> _adpcmDataSourceCommandVersion1Pool = new(() => new AdpcmDataSourceCommandVersion1());
        private readonly static ObjectPool<DataSourceVersion2Command> _dataSourceVersion2CommandPool = new(() => new DataSourceVersion2Command());
        private readonly static ObjectPool<VolumeCommand> _volumeCommandPool = new(() => new VolumeCommand());
        private readonly static ObjectPool<VolumeRampCommand> _volumeRampCommandPool = new(() => new VolumeRampCommand());
        private readonly static ObjectPool<BiquadFilterCommand> _biquadFilterCommandPool = new(() => new BiquadFilterCommand());
        private readonly static ObjectPool<MixCommand> _mixCommandPool = new(() => new MixCommand());
        private readonly static ObjectPool<MixRampCommand> _mixRampCommandPool = new(() => new MixRampCommand());
        private readonly static ObjectPool<MixRampGroupedCommand> _mixRampGroupedCommandPool = new(() => new MixRampGroupedCommand());
        private readonly static ObjectPool<DepopPrepareCommand> _depopPrepareCommandPool = new(() => new DepopPrepareCommand());
        private readonly static ObjectPool<DepopForMixBuffersCommand> _depopForMixBuffersCommandPool = new(() => new DepopForMixBuffersCommand());
        private readonly static ObjectPool<DelayCommand> _delayCommandPool = new(() => new DelayCommand());
        private readonly static ObjectPool<UpsampleCommand> _upsampleCommandPool = new(() => new UpsampleCommand());
        private readonly static ObjectPool<DownMixSurroundToStereoCommand> _downMixSurroundToStereoCommandPool = new(() => new DownMixSurroundToStereoCommand());
        private readonly static ObjectPool<AuxiliaryBufferCommand> _auxiliaryBufferCommandPool = new(() => new AuxiliaryBufferCommand());
        private readonly static ObjectPool<DeviceSinkCommand> _deviceSinkCommandPool = new(() => new DeviceSinkCommand());
        private readonly static ObjectPool<CircularBufferSinkCommand> _circularBufferSinkCommandPool = new(() => new CircularBufferSinkCommand());
        private readonly static ObjectPool<ReverbCommand> _reverbCommandPool = new(() => new ReverbCommand());
        private readonly static ObjectPool<Reverb3dCommand> _reverb3dCommandPool = new(() => new Reverb3dCommand());
        private readonly static ObjectPool<PerformanceCommand> _performanceCommandPool = new(() => new PerformanceCommand());
        private readonly static ObjectPool<ClearMixBufferCommand> _clearMixBufferCommandPool = new(() => new ClearMixBufferCommand());
        private readonly static ObjectPool<CopyMixBufferCommand> _copyMixBufferCommandPool = new(() => new CopyMixBufferCommand());
        private readonly static ObjectPool<LimiterCommandVersion1> _limiterCommandVersion1Pool = new(() => new LimiterCommandVersion1());
        private readonly static ObjectPool<LimiterCommandVersion2> _limiterCommandVersion2Pool = new(() => new LimiterCommandVersion2());
        private readonly static ObjectPool<MultiTapBiquadFilterCommand> _multiTapBiquadFilterCommandPool = new(() => new MultiTapBiquadFilterCommand());
        private readonly static ObjectPool<CaptureBufferCommand> _captureBufferCommandPool = new(() => new CaptureBufferCommand());
        private readonly static ObjectPool<CompressorCommand> _compressorCommandPool = new(() => new CompressorCommand());
        private readonly static ObjectPool<BiquadFilterAndMixCommand> _biquadFilterAndMixCommandPool = new(() => new BiquadFilterAndMixCommand());
        private readonly static ObjectPool<MultiTapBiquadFilterAndMixCommand> _multiTapBiquadFilterAndMixCommandPool = new(() => new MultiTapBiquadFilterAndMixCommand());
        private readonly static ObjectPool<FillBufferCommand> _fillBufferCommandPool = new(() => new FillBufferCommand());

        public static void ReleaseCommand(ICommand command)
        {
            switch (command.CommandType)
            {
                case CommandType.PcmInt16DataSourceVersion1:
                    _pcmInt16DataSourceCommandVersion1Pool.Release((PcmInt16DataSourceCommandVersion1)command);
                    break;
                case CommandType.PcmInt16DataSourceVersion2:
                    _dataSourceVersion2CommandPool.Release((DataSourceVersion2Command)command);
                    break;
                case CommandType.PcmFloatDataSourceVersion1:
                    _pcmFloatDataSourceCommandVersion1Pool.Release((PcmFloatDataSourceCommandVersion1)command);
                    break;
                case CommandType.PcmFloatDataSourceVersion2:
                    _dataSourceVersion2CommandPool.Release((DataSourceVersion2Command)command);
                    break;
                case CommandType.AdpcmDataSourceVersion1:
                    _adpcmDataSourceCommandVersion1Pool.Release((AdpcmDataSourceCommandVersion1)command);
                    break;
                case CommandType.AdpcmDataSourceVersion2:
                    _dataSourceVersion2CommandPool.Release((DataSourceVersion2Command)command);
                    break;
                case CommandType.Volume:
                    _volumeCommandPool.Release((VolumeCommand)command);
                    break;
                case CommandType.VolumeRamp:
                    _volumeRampCommandPool.Release((VolumeRampCommand)command);
                    break;
                case CommandType.BiquadFilter:
                    _biquadFilterCommandPool.Release((BiquadFilterCommand)command);
                    break;
                case CommandType.BiquadFilterFloatCoeff:
                    throw new NotImplementedException();
                case CommandType.Mix:
                    _mixCommandPool.Release((MixCommand)command);
                    break;
                case CommandType.MixRamp:
                    _mixRampCommandPool.Release((MixRampCommand)command);
                    break;
                case CommandType.MixRampGrouped:
                    _mixRampGroupedCommandPool.Release((MixRampGroupedCommand)command);
                    break;
                case CommandType.DepopPrepare:
                    _depopPrepareCommandPool.Release((DepopPrepareCommand)command);
                    break;
                case CommandType.DepopForMixBuffers:
                    _depopForMixBuffersCommandPool.Release((DepopForMixBuffersCommand)command);
                    break;
                case CommandType.Delay:
                    _delayCommandPool.Release((DelayCommand)command);
                    break;
                case CommandType.Upsample:
                    _upsampleCommandPool.Release((UpsampleCommand)command);
                    break;
                case CommandType.DownMixSurroundToStereo:
                    _downMixSurroundToStereoCommandPool.Release((DownMixSurroundToStereoCommand)command);
                    break;
                case CommandType.AuxiliaryBuffer:
                    _auxiliaryBufferCommandPool.Release((AuxiliaryBufferCommand)command);
                    break;
                case CommandType.DeviceSink:
                    _deviceSinkCommandPool.Release((DeviceSinkCommand)command);
                    break;
                case CommandType.CircularBufferSink:
                    _circularBufferSinkCommandPool.Release((CircularBufferSinkCommand)command);
                    break;
                case CommandType.Reverb:
                    _reverbCommandPool.Release((ReverbCommand)command);
                    break;
                case CommandType.Reverb3d:
                    _reverb3dCommandPool.Release((Reverb3dCommand)command);
                    break;
                case CommandType.Performance:
                    _performanceCommandPool.Release((PerformanceCommand)command);
                    break;
                case CommandType.ClearMixBuffer:
                    _clearMixBufferCommandPool.Release((ClearMixBufferCommand)command);
                    break;
                case CommandType.CopyMixBuffer:
                    _copyMixBufferCommandPool.Release((CopyMixBufferCommand)command);
                    break;
                case CommandType.LimiterVersion1:
                    _limiterCommandVersion1Pool.Release((LimiterCommandVersion1)command);
                    break;
                case CommandType.LimiterVersion2:
                    _limiterCommandVersion2Pool.Release((LimiterCommandVersion2)command);
                    break;
                case CommandType.MultiTapBiquadFilter:
                    _multiTapBiquadFilterCommandPool.Release((MultiTapBiquadFilterCommand)command);
                    break;
                case CommandType.MultiTapBiquadFilterFloatCoeff:
                    throw new NotImplementedException();
                case CommandType.CaptureBuffer:
                    _captureBufferCommandPool.Release((CaptureBufferCommand)command);
                    break;
                case CommandType.Compressor:
                    _compressorCommandPool.Release((CompressorCommand)command);
                    break;
                case CommandType.BiquadFilterAndMix:
                    _biquadFilterAndMixCommandPool.Release((BiquadFilterAndMixCommand)command);
                    break;
                case CommandType.BiquadFilterAndMixFloatCoeff:
                    throw new NotImplementedException();
                case CommandType.MultiTapBiquadFilterAndMix:
                    _multiTapBiquadFilterAndMixCommandPool.Release((MultiTapBiquadFilterAndMixCommand)command);
                    break;
                case CommandType.MultiTapBiquadFilterAndMixFloatCoef:
                    throw new NotImplementedException();
                case CommandType.AuxiliaryBufferGrouped:
                    throw new NotImplementedException();
                case CommandType.FillMixBuffer:
                    throw new NotImplementedException();
                case CommandType.BiquadFilterCrossFade:
                    throw new NotImplementedException();
                case CommandType.MultiTapBiquadFilterCrossFade:
                    throw new NotImplementedException();
                case CommandType.FillBuffer:
                    _fillBufferCommandPool.Release((FillBufferCommand)command);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
        
        /// <summary>
        /// Create a new <see cref="CommandBuffer"/>.
        /// </summary>
        /// <param name="commandList">The command list that will store the generated commands.</param>
        /// <param name="commandProcessingTimeEstimator">The command processing time estimator to use.</param>
        public CommandBuffer(CommandList commandList, ICommandProcessingTimeEstimator commandProcessingTimeEstimator)
        {
            CommandList = commandList;
            EstimatedProcessingTime = 0;
            _commandProcessingTimeEstimator = commandProcessingTimeEstimator;
        }

        /// <summary>
        /// Add a new generated command to the <see cref="CommandList"/>.
        /// </summary>
        /// <param name="command">The command to add.</param>
        private void AddCommand(ICommand command)
        {
            EstimatedProcessingTime += command.EstimatedProcessingTime;

            CommandList.AddCommand(command);
        }

        /// <summary>
        /// Generate a new <see cref="ClearMixBufferCommand"/>.
        /// </summary>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateClearMixBuffer(int nodeId)
        {
            ClearMixBufferCommand command = _clearMixBufferCommandPool.Allocate().Initialize(nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Generate a new <see cref="DepopPrepareCommand"/>.
        /// </summary>
        /// <param name="state">The voice state associated.</param>
        /// <param name="depopBuffer">The depop buffer.</param>
        /// <param name="bufferCount">The buffer count.</param>
        /// <param name="bufferOffset">The target buffer offset.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        /// <param name="wasPlaying">Set to true if the voice was playing previously.</param>
        public void GenerateDepopPrepare(Memory<VoiceState> state, Memory<float> depopBuffer, uint bufferCount, uint bufferOffset, int nodeId, bool wasPlaying)
        {
            DepopPrepareCommand command = _depopPrepareCommandPool.Allocate().Initialize(state, depopBuffer, bufferCount, bufferOffset, nodeId, wasPlaying);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Generate a new <see cref="PerformanceCommand"/>.
        /// </summary>
        /// <param name="performanceEntryAddresses">The <see cref="PerformanceEntryAddresses"/>.</param>
        /// <param name="type">The performance operation to perform.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GeneratePerformance(ref PerformanceEntryAddresses performanceEntryAddresses, PerformanceCommand.Type type, int nodeId)
        {
            PerformanceCommand command = _performanceCommandPool.Allocate().Initialize(ref performanceEntryAddresses, type, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Create a new <see cref="VolumeRampCommand"/>.
        /// </summary>
        /// <param name="previousVolume">The previous volume.</param>
        /// <param name="volume">The new volume.</param>
        /// <param name="bufferIndex">The index of the mix buffer to use.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateVolumeRamp(float previousVolume, float volume, uint bufferIndex, int nodeId)
        {
            VolumeRampCommand command = _volumeRampCommandPool.Allocate().Initialize(previousVolume, volume, bufferIndex, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Create a new <see cref="DataSourceVersion2Command"/>.
        /// </summary>
        /// <param name="voiceInfo">The <see cref="VoiceInfo"/> to generate the command from.</param>
        /// <param name="state">The <see cref="VoiceState"/> to generate the command from.</param>
        /// <param name="outputBufferIndex">The output buffer index to use.</param>
        /// <param name="channelIndex">The target channel index.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateDataSourceVersion2(ref VoiceInfo voiceInfo, Memory<VoiceState> state, ushort outputBufferIndex, ushort channelIndex, int nodeId)
        {
            DataSourceVersion2Command command = _dataSourceVersion2CommandPool.Allocate().Initialize(ref voiceInfo, state, outputBufferIndex, channelIndex, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Create a new <see cref="PcmInt16DataSourceCommandVersion1"/>.
        /// </summary>
        /// <param name="voiceInfo">The <see cref="VoiceInfo"/> to generate the command from.</param>
        /// <param name="state">The <see cref="VoiceState"/> to generate the command from.</param>
        /// <param name="outputBufferIndex">The output buffer index to use.</param>
        /// <param name="channelIndex">The target channel index.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GeneratePcmInt16DataSourceVersion1(ref VoiceInfo voiceInfo, Memory<VoiceState> state, ushort outputBufferIndex, ushort channelIndex, int nodeId)
        {
            PcmInt16DataSourceCommandVersion1 command = _pcmInt16DataSourceCommandVersion1Pool.Allocate().Initialize(ref voiceInfo, state, outputBufferIndex, channelIndex, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Create a new <see cref="PcmFloatDataSourceCommandVersion1"/>.
        /// </summary>
        /// <param name="voiceInfo">The <see cref="VoiceInfo"/> to generate the command from.</param>
        /// <param name="state">The <see cref="VoiceState"/> to generate the command from.</param>
        /// <param name="outputBufferIndex">The output buffer index to use.</param>
        /// <param name="channelIndex">The target channel index.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GeneratePcmFloatDataSourceVersion1(ref VoiceInfo voiceInfo, Memory<VoiceState> state, ushort outputBufferIndex, ushort channelIndex, int nodeId)
        {
            PcmFloatDataSourceCommandVersion1 command = _pcmFloatDataSourceCommandVersion1Pool.Allocate().Initialize(ref voiceInfo, state, outputBufferIndex, channelIndex, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Create a new <see cref="AdpcmDataSourceCommandVersion1"/>.
        /// </summary>
        /// <param name="voiceInfo">The <see cref="VoiceInfo"/> to generate the command from.</param>
        /// <param name="state">The <see cref="VoiceState"/> to generate the command from.</param>
        /// <param name="outputBufferIndex">The output buffer index to use.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateAdpcmDataSourceVersion1(ref VoiceInfo voiceInfo, Memory<VoiceState> state, ushort outputBufferIndex, int nodeId)
        {
            AdpcmDataSourceCommandVersion1 command = _adpcmDataSourceCommandVersion1Pool.Allocate().Initialize(ref voiceInfo, state, outputBufferIndex, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Create a new <see cref="BiquadFilterCommand"/>.
        /// </summary>
        /// <param name="baseIndex">The base index of the input and output buffer.</param>
        /// <param name="filter">The biquad filter parameter.</param>
        /// <param name="biquadFilterStateMemory">The biquad state.</param>
        /// <param name="inputBufferOffset">The input buffer offset.</param>
        /// <param name="outputBufferOffset">The output buffer offset.</param>
        /// <param name="needInitialization">Set to true if the biquad filter state needs to be initialized.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateBiquadFilter(int baseIndex, ref BiquadFilterParameter2 filter, Memory<BiquadFilterState> biquadFilterStateMemory, int inputBufferOffset, int outputBufferOffset, bool needInitialization, int nodeId)
        {
            BiquadFilterCommand command = _biquadFilterCommandPool.Allocate().Initialize(baseIndex, ref filter, biquadFilterStateMemory, inputBufferOffset, outputBufferOffset, needInitialization, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Create a new <see cref="MultiTapBiquadFilterCommand"/>.
        /// </summary>
        /// <param name="baseIndex">The base index of the input and output buffer.</param>
        /// <param name="filters">The biquad filter parameters.</param>
        /// <param name="biquadFilterStatesMemory">The biquad states.</param>
        /// <param name="inputBufferOffset">The input buffer offset.</param>
        /// <param name="outputBufferOffset">The output buffer offset.</param>
        /// <param name="isInitialized">Set to true if the biquad filter state is initialized.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateMultiTapBiquadFilter(int baseIndex, ReadOnlySpan<BiquadFilterParameter2> filters, Memory<BiquadFilterState> biquadFilterStatesMemory, int inputBufferOffset, int outputBufferOffset, ReadOnlySpan<bool> isInitialized, int nodeId)
        {
            MultiTapBiquadFilterCommand command = _multiTapBiquadFilterCommandPool.Allocate().Initialize(baseIndex, filters, biquadFilterStatesMemory, inputBufferOffset, outputBufferOffset, isInitialized, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Generate a new <see cref="MixRampGroupedCommand"/>.
        /// </summary>
        /// <param name="mixBufferCount">The mix buffer count.</param>
        /// <param name="inputBufferIndex">The base input index.</param>
        /// <param name="outputBufferIndex">The base output index.</param>
        /// <param name="previousVolume">The previous volume.</param>
        /// <param name="volume">The new volume.</param>
        /// <param name="state">The <see cref="VoiceState"/> to generate the command from.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateMixRampGrouped(uint mixBufferCount, uint inputBufferIndex, uint outputBufferIndex, ReadOnlySpan<float> previousVolume, ReadOnlySpan<float> volume, Memory<VoiceState> state, int nodeId)
        {
            MixRampGroupedCommand command = _mixRampGroupedCommandPool.Allocate().Initialize(mixBufferCount, inputBufferIndex, outputBufferIndex, previousVolume, volume, state, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Generate a new <see cref="MixRampCommand"/>.
        /// </summary>
        /// <param name="previousVolume">The previous volume.</param>
        /// <param name="volume">The new volume.</param>
        /// <param name="inputBufferIndex">The input buffer index.</param>
        /// <param name="outputBufferIndex">The output buffer index.</param>
        /// <param name="lastSampleIndex">The index in the <see cref="VoiceState.LastSamples"/> array to store the ramped sample.</param>
        /// <param name="state">The <see cref="VoiceState"/> to generate the command from.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateMixRamp(float previousVolume, float volume, uint inputBufferIndex, uint outputBufferIndex, int lastSampleIndex, Memory<VoiceState> state, int nodeId)
        {
            MixRampCommand command = _mixRampCommandPool.Allocate().Initialize(previousVolume, volume, inputBufferIndex, outputBufferIndex, lastSampleIndex, state, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Generate a new <see cref="BiquadFilterAndMixCommand"/>.
        /// </summary>
        /// <param name="previousVolume">The previous volume.</param>
        /// <param name="volume">The new volume.</param>
        /// <param name="inputBufferIndex">The input buffer index.</param>
        /// <param name="outputBufferIndex">The output buffer index.</param>
        /// <param name="lastSampleIndex">The index in the <see cref="VoiceState.LastSamples"/> array to store the ramped sample.</param>
        /// <param name="state">The <see cref="VoiceState"/> to generate the command from.</param>
        /// <param name="filter">The biquad filter parameter.</param>
        /// <param name="biquadFilterState">The biquad state.</param>
        /// <param name="previousBiquadFilterState">The previous biquad state.</param>
        /// <param name="needInitialization">Set to true if the biquad filter state needs to be initialized.</param>
        /// <param name="hasVolumeRamp">Set to true if the mix has volume ramp, and <paramref name="previousVolume"/> should be taken into account.</param>
        /// <param name="isFirstMixBuffer">Set to true if the buffer is the first mix buffer.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateBiquadFilterAndMix(
            float previousVolume,
            float volume,
            uint inputBufferIndex,
            uint outputBufferIndex,
            int lastSampleIndex,
            Memory<VoiceState> state,
            ref BiquadFilterParameter2 filter,
            Memory<BiquadFilterState> biquadFilterState,
            Memory<BiquadFilterState> previousBiquadFilterState,
            bool needInitialization,
            bool hasVolumeRamp,
            bool isFirstMixBuffer,
            int nodeId)
        {
            BiquadFilterAndMixCommand command = _biquadFilterAndMixCommandPool.Allocate().Initialize(
                previousVolume,
                volume,
                inputBufferIndex,
                outputBufferIndex,
                lastSampleIndex,
                state,
                ref filter,
                biquadFilterState,
                previousBiquadFilterState,
                needInitialization,
                hasVolumeRamp,
                isFirstMixBuffer,
                nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Generate a new <see cref="MultiTapBiquadFilterAndMixCommand"/>.
        /// </summary>
        /// <param name="previousVolume">The previous volume.</param>
        /// <param name="volume">The new volume.</param>
        /// <param name="inputBufferIndex">The input buffer index.</param>
        /// <param name="outputBufferIndex">The output buffer index.</param>
        /// <param name="lastSampleIndex">The index in the <see cref="VoiceState.LastSamples"/> array to store the ramped sample.</param>
        /// <param name="state">The <see cref="VoiceState"/> to generate the command from.</param>
        /// <param name="filter0">First biquad filter parameter.</param>
        /// <param name="filter1">Second biquad filter parameter.</param>
        /// <param name="biquadFilterState0">First biquad state.</param>
        /// <param name="biquadFilterState1">Second biquad state.</param>
        /// <param name="previousBiquadFilterState0">First previous biquad state.</param>
        /// <param name="previousBiquadFilterState1">Second previous biquad state.</param>
        /// <param name="needInitialization0">Set to true if the first biquad filter state needs to be initialized.</param>
        /// <param name="needInitialization1">Set to true if the second biquad filter state needs to be initialized.</param>
        /// <param name="hasVolumeRamp">Set to true if the mix has volume ramp, and <paramref name="previousVolume"/> should be taken into account.</param>
        /// <param name="isFirstMixBuffer">Set to true if the buffer is the first mix buffer.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateMultiTapBiquadFilterAndMix(
            float previousVolume,
            float volume,
            uint inputBufferIndex,
            uint outputBufferIndex,
            int lastSampleIndex,
            Memory<VoiceState> state,
            ref BiquadFilterParameter2 filter0,
            ref BiquadFilterParameter2 filter1,
            Memory<BiquadFilterState> biquadFilterState0,
            Memory<BiquadFilterState> biquadFilterState1,
            Memory<BiquadFilterState> previousBiquadFilterState0,
            Memory<BiquadFilterState> previousBiquadFilterState1,
            bool needInitialization0,
            bool needInitialization1,
            bool hasVolumeRamp,
            bool isFirstMixBuffer,
            int nodeId)
        {
            MultiTapBiquadFilterAndMixCommand command = _multiTapBiquadFilterAndMixCommandPool.Allocate().Initialize(
                previousVolume,
                volume,
                inputBufferIndex,
                outputBufferIndex,
                lastSampleIndex,
                state,
                ref filter0,
                ref filter1,
                biquadFilterState0,
                biquadFilterState1,
                previousBiquadFilterState0,
                previousBiquadFilterState1,
                needInitialization0,
                needInitialization1,
                hasVolumeRamp,
                isFirstMixBuffer,
                nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Generate a new <see cref="DepopForMixBuffersCommand"/>.
        /// </summary>
        /// <param name="depopBuffer">The depop buffer.</param>
        /// <param name="bufferOffset">The target buffer offset.</param>
        /// <param name="bufferCount">The buffer count.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        /// <param name="sampleRate">The target sample rate in use.</param>
        public void GenerateDepopForMixBuffers(Memory<float> depopBuffer, uint bufferOffset, uint bufferCount, int nodeId, uint sampleRate)
        {
            DepopForMixBuffersCommand command = _depopForMixBuffersCommandPool.Allocate().Initialize(depopBuffer, bufferOffset, bufferCount, nodeId, sampleRate);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Generate a new <see cref="CopyMixBufferCommand"/>.
        /// </summary>
        /// <param name="inputBufferIndex">The input buffer index.</param>
        /// <param name="outputBufferIndex">The output buffer index.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateCopyMixBuffer(uint inputBufferIndex, uint outputBufferIndex, int nodeId)
        {
            CopyMixBufferCommand command = _copyMixBufferCommandPool.Allocate().Initialize(inputBufferIndex, outputBufferIndex, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Generate a new <see cref="MixCommand"/>.
        /// </summary>
        /// <param name="inputBufferIndex">The input buffer index.</param>
        /// <param name="outputBufferIndex">The output buffer index.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        /// <param name="volume">The mix volume.</param>
        public void GenerateMix(uint inputBufferIndex, uint outputBufferIndex, int nodeId, float volume)
        {
            MixCommand command = _mixCommandPool.Allocate().Initialize(inputBufferIndex, outputBufferIndex, nodeId, volume);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Generate a new <see cref="ReverbCommand"/>.
        /// </summary>
        /// <param name="bufferOffset">The target buffer offset.</param>
        /// <param name="parameter">The reverb parameter.</param>
        /// <param name="state">The reverb state.</param>
        /// <param name="isEnabled">Set to true if the effect should be active.</param>
        /// <param name="workBuffer">The work buffer to use for processing.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        /// <param name="isLongSizePreDelaySupported">If set to true, the long size pre-delay is supported.</param>
        /// <param name="newEffectChannelMappingSupported">If set to true, the new effect channel mapping for 5.1 is supported.</param>
        public void GenerateReverbEffect(uint bufferOffset, ReverbParameter parameter, Memory<ReverbState> state, bool isEnabled, CpuAddress workBuffer, int nodeId, bool isLongSizePreDelaySupported, bool newEffectChannelMappingSupported)
        {
            if (parameter.IsChannelCountValid())
            {
                ReverbCommand command = _reverbCommandPool.Allocate().Initialize(bufferOffset, parameter, state, isEnabled, workBuffer, nodeId, isLongSizePreDelaySupported, newEffectChannelMappingSupported);

                command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

                AddCommand(command);
            }
        }

        /// <summary>
        /// Generate a new <see cref="Reverb3dCommand"/>.
        /// </summary>
        /// <param name="bufferOffset">The target buffer offset.</param>
        /// <param name="parameter">The reverb 3d parameter.</param>
        /// <param name="state">The reverb 3d state.</param>
        /// <param name="isEnabled">Set to true if the effect should be active.</param>
        /// <param name="workBuffer">The work buffer to use for processing.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        /// <param name="newEffectChannelMappingSupported">If set to true, the new effect channel mapping for 5.1 is supported.</param>
        public void GenerateReverb3dEffect(uint bufferOffset, Reverb3dParameter parameter, Memory<Reverb3dState> state, bool isEnabled, CpuAddress workBuffer, int nodeId, bool newEffectChannelMappingSupported)
        {
            if (parameter.IsChannelCountValid())
            {
                Reverb3dCommand command = _reverb3dCommandPool.Allocate().Initialize(bufferOffset, parameter, state, isEnabled, workBuffer, nodeId, newEffectChannelMappingSupported);

                command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

                AddCommand(command);
            }
        }

        /// <summary>
        /// Generate a new <see cref="DelayCommand"/>.
        /// </summary>
        /// <param name="bufferOffset">The target buffer offset.</param>
        /// <param name="parameter">The delay parameter.</param>
        /// <param name="state">The delay state.</param>
        /// <param name="isEnabled">Set to true if the effect should be active.</param>
        /// <param name="workBuffer">The work buffer to use for processing.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        /// <param name="newEffectChannelMappingSupported">If set to true, the new effect channel mapping for 5.1 is supported.</param>
        public void GenerateDelayEffect(uint bufferOffset, DelayParameter parameter, Memory<DelayState> state, bool isEnabled, CpuAddress workBuffer, int nodeId, bool newEffectChannelMappingSupported)
        {
            if (parameter.IsChannelCountValid())
            {
                DelayCommand command = _delayCommandPool.Allocate().Initialize(bufferOffset, parameter, state, isEnabled, workBuffer, nodeId, newEffectChannelMappingSupported);

                command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

                AddCommand(command);
            }
        }

        /// <summary>
        /// Generate a new <see cref="LimiterCommandVersion1"/>.
        /// </summary>
        /// <param name="bufferOffset">The target buffer offset.</param>
        /// <param name="parameter">The limiter parameter.</param>
        /// <param name="state">The limiter state.</param>
        /// <param name="isEnabled">Set to true if the effect should be active.</param>
        /// <param name="workBuffer">The work buffer to use for processing.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateLimiterEffectVersion1(uint bufferOffset, LimiterParameter parameter, Memory<LimiterState> state, bool isEnabled, ulong workBuffer, int nodeId)
        {
            if (parameter.IsChannelCountValid())
            {
                LimiterCommandVersion1 command = _limiterCommandVersion1Pool.Allocate().Initialize(bufferOffset, parameter, state, isEnabled, workBuffer, nodeId);

                command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

                AddCommand(command);
            }
        }

        /// <summary>
        /// Generate a new <see cref="LimiterCommandVersion2"/>.
        /// </summary>
        /// <param name="bufferOffset">The target buffer offset.</param>
        /// <param name="parameter">The limiter parameter.</param>
        /// <param name="state">The limiter state.</param>
        /// <param name="effectResultState">The DSP effect result state.</param>
        /// <param name="isEnabled">Set to true if the effect should be active.</param>
        /// <param name="workBuffer">The work buffer to use for processing.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateLimiterEffectVersion2(uint bufferOffset, LimiterParameter parameter, Memory<LimiterState> state, Memory<EffectResultState> effectResultState, bool isEnabled, ulong workBuffer, int nodeId)
        {
            if (parameter.IsChannelCountValid())
            {
                LimiterCommandVersion2 command = _limiterCommandVersion2Pool.Allocate().Initialize(bufferOffset, parameter, state, effectResultState, isEnabled, workBuffer, nodeId);

                command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

                AddCommand(command);
            }
        }

        /// <summary>
        /// Generate a new <see cref="AuxiliaryBufferCommand"/>.
        /// </summary>
        /// <param name="bufferOffset">The target buffer offset.</param>
        /// <param name="inputBufferOffset">The input buffer offset.</param>
        /// <param name="outputBufferOffset">The output buffer offset.</param>
        /// <param name="state">The aux state.</param>
        /// <param name="isEnabled">Set to true if the effect should be active.</param>
        /// <param name="countMax">The limit of the circular buffer.</param>
        /// <param name="outputBuffer">The guest address of the output buffer.</param>
        /// <param name="inputBuffer">The guest address of the input buffer.</param>
        /// <param name="updateCount">The count to add on the offset after write/read operations.</param>
        /// <param name="writeOffset">The write offset.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateAuxEffect(uint bufferOffset, byte inputBufferOffset, byte outputBufferOffset, ref AuxiliaryBufferAddresses state, bool isEnabled, uint countMax, CpuAddress outputBuffer, CpuAddress inputBuffer, uint updateCount, uint writeOffset, int nodeId)
        {
            if (state.SendBufferInfoBase != 0 && state.ReturnBufferInfoBase != 0)
            {
                AuxiliaryBufferCommand command = _auxiliaryBufferCommandPool.Allocate().Initialize(bufferOffset, inputBufferOffset, outputBufferOffset, ref state, isEnabled, countMax, outputBuffer, inputBuffer, updateCount, writeOffset, nodeId);

                command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

                AddCommand(command);
            }
        }

        /// <summary>
        /// Generate a new <see cref="CaptureBufferCommand"/>.
        /// </summary>
        /// <param name="bufferOffset">The target buffer offset.</param>
        /// <param name="inputBufferOffset">The input buffer offset.</param>
        /// <param name="sendBufferInfo">The capture state.</param>
        /// <param name="isEnabled">Set to true if the effect should be active.</param>
        /// <param name="countMax">The limit of the circular buffer.</param>
        /// <param name="outputBuffer">The guest address of the output buffer.</param>
        /// <param name="updateCount">The count to add on the offset after write operations.</param>
        /// <param name="writeOffset">The write offset.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateCaptureEffect(uint bufferOffset, byte inputBufferOffset, ulong sendBufferInfo, bool isEnabled, uint countMax, CpuAddress outputBuffer, uint updateCount, uint writeOffset, int nodeId)
        {
            if (sendBufferInfo != 0)
            {
                CaptureBufferCommand command = _captureBufferCommandPool.Allocate().Initialize(bufferOffset, inputBufferOffset, sendBufferInfo, isEnabled, countMax, outputBuffer, updateCount, writeOffset, nodeId);

                command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

                AddCommand(command);
            }
        }

        /// <summary>
        /// Generate a new <see cref="CompressorCommand"/>.
        /// </summary>
        /// <param name="bufferOffset">The target buffer offset.</param>
        /// <param name="parameter">The compressor parameter.</param>
        /// <param name="state">The compressor state.</param>
        /// <param name="effectResultState">The DSP effect result state.</param>
        /// <param name="isEnabled">Set to true if the effect should be active.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateCompressorEffect(uint bufferOffset, CompressorParameter parameter, Memory<CompressorState> state, Memory<EffectResultState> effectResultState, bool isEnabled, int nodeId)
        {
            if (parameter.IsChannelCountValid())
            {
                CompressorCommand command = _compressorCommandPool.Allocate().Initialize(bufferOffset, parameter, state, effectResultState, isEnabled, nodeId);

                command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

                AddCommand(command);
            }
        }

        /// <summary>
        /// Generate a new <see cref="VolumeCommand"/>.
        /// </summary>
        /// <param name="volume">The target volume to apply.</param>
        /// <param name="bufferOffset">The offset of the mix buffer.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateVolume(float volume, uint bufferOffset, int nodeId)
        {
            VolumeCommand command = _volumeCommandPool.Allocate().Initialize(volume, bufferOffset, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Create a new <see cref="CircularBufferSinkCommand"/>.
        /// </summary>
        /// <param name="bufferOffset">The offset of the mix buffer.</param>
        /// <param name="sink">The <see cref="BaseSink"/> of the circular buffer.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateCircularBuffer(uint bufferOffset, CircularBufferSink sink, int nodeId)
        {
            CircularBufferSinkCommand command = _circularBufferSinkCommandPool.Allocate().Initialize(bufferOffset, ref sink.Parameter, ref sink.CircularBufferAddressInfo, sink.CurrentWriteOffset, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Create a new <see cref="DownMixSurroundToStereoCommand"/>.
        /// </summary>
        /// <param name="bufferOffset">The offset of the mix buffer.</param>
        /// <param name="inputBufferOffset">The input buffer offset.</param>
        /// <param name="outputBufferOffset">The output buffer offset.</param>
        /// <param name="downMixParameter">The downmixer parameters to use.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateDownMixSurroundToStereo(uint bufferOffset, Span<byte> inputBufferOffset, Span<byte> outputBufferOffset, float[] downMixParameter, int nodeId)
        {
            DownMixSurroundToStereoCommand command = _downMixSurroundToStereoCommandPool.Allocate().Initialize(bufferOffset, inputBufferOffset, outputBufferOffset, downMixParameter, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Create a new <see cref="UpsampleCommand"/>.
        /// </summary>
        /// <param name="bufferOffset">The offset of the mix buffer.</param>
        /// <param name="upsampler">The <see cref="UpsamplerInfo"/> associated.</param>
        /// <param name="inputCount">The total input count.</param>
        /// <param name="inputBufferOffset">The input buffer mix offset.</param>
        /// <param name="bufferCountPerSample">The buffer count per sample.</param>
        /// <param name="sampleCount">The source sample count.</param>
        /// <param name="sampleRate">The source sample rate.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateUpsample(uint bufferOffset, UpsamplerInfo upsampler, uint inputCount, Span<byte> inputBufferOffset, uint bufferCountPerSample, uint sampleCount, uint sampleRate, int nodeId)
        {
            UpsampleCommand command = _upsampleCommandPool.Allocate().Initialize(bufferOffset, upsampler, inputCount, inputBufferOffset, bufferCountPerSample, sampleCount, sampleRate, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        /// <summary>
        /// Create a new <see cref="DeviceSinkCommand"/>.
        /// </summary>
        /// <param name="bufferOffset">The offset of the mix buffer.</param>
        /// <param name="sink">The <see cref="BaseSink"/> of the device sink.</param>
        /// <param name="sessionId">The current audio renderer session id.</param>
        /// <param name="buffer">The mix buffer in use.</param>
        /// <param name="nodeId">The node id associated to this command.</param>
        public void GenerateDeviceSink(uint bufferOffset, DeviceSink sink, int sessionId, Memory<float> buffer, int nodeId)
        {
            DeviceSinkCommand command = _deviceSinkCommandPool.Allocate().Initialize(bufferOffset, sink, sessionId, buffer, nodeId);

            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);

            AddCommand(command);
        }

        public void GenerateFillBuffer(SplitterDestination destination, float value, int length, int nodeId)
        {
            FillBufferCommand command = _fillBufferCommandPool.Allocate().Initialize(destination, length, value, nodeId);
            
            command.EstimatedProcessingTime = _commandProcessingTimeEstimator.Estimate(command);
            
            AddCommand(command);
        }
    }
}
