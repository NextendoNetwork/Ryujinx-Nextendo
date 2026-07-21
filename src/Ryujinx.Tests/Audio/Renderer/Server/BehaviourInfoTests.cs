using NUnit.Framework;
using Ryujinx.Audio.Renderer.Server;

namespace Ryujinx.Tests.Audio.Renderer.Server
{
    public class BehaviourInfoTests
    {
        [Test]
        public void TestCheckFeature()
        {
            int latestRevision = BehaviourInfo.BaseRevisionMagic + BehaviourInfo.LastRevision;
            int previousRevision = BehaviourInfo.BaseRevisionMagic + (BehaviourInfo.LastRevision - 1);
            int invalidRevision = BehaviourInfo.BaseRevisionMagic + (BehaviourInfo.LastRevision + 1);

            Assert.IsTrue(BehaviourInfo.CheckFeatureSupported(latestRevision, latestRevision));
            Assert.IsFalse(BehaviourInfo.CheckFeatureSupported(previousRevision, latestRevision));
            Assert.IsTrue(BehaviourInfo.CheckFeatureSupported(latestRevision, previousRevision));
            // In case we get an invalid revision, this is supposed to auto default to REV1 internally.. idk what the hell Nintendo was thinking here..
            Assert.IsTrue(BehaviourInfo.CheckFeatureSupported(invalidRevision, latestRevision));
        }

        [Test]
        public void TestsMemoryPoolForceMappingEnabled()
        {
            BehaviourInfo behaviourInfo = new();

            behaviourInfo.SetUserRevision(BehaviourInfo.BaseRevisionMagic + BehaviourInfo.Revision1);

            Assert.IsFalse(behaviourInfo.IsMemoryPoolForceMappingEnabled());

            behaviourInfo.UpdateFlags(0x1);

            Assert.IsTrue(behaviourInfo.IsMemoryPoolForceMappingEnabled());
        }

        [Test]
        public void TestRevision1()
        {
            BehaviourInfo behaviourInfo = new();

            behaviourInfo.SetUserRevision(BehaviourInfo.BaseRevisionMagic + BehaviourInfo.Revision1);

            Assert.IsFalse(behaviourInfo.IsAdpcmLoopContextBugFixed());
            Assert.IsFalse(behaviourInfo.IsSplitterSupported());
            Assert.IsFalse(behaviourInfo.IsLongSizePreDelaySupported());
            Assert.IsFalse(behaviourInfo.IsAudioUsbDeviceOutputSupported());
            Assert.IsFalse(behaviourInfo.IsFlushVoiceWaveBuffersSupported());
            Assert.IsFalse(behaviourInfo.IsSplitterBugFixed());
            Assert.IsFalse(behaviourInfo.IsElapsedFrameCountSupported());
            Assert.IsFalse(behaviourInfo.IsDecodingBehaviourFlagSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterEffectStateClearBugFixed());
            Assert.IsFalse(behaviourInfo.IsMixInParameterDirtyOnlyUpdateSupported());
            Assert.IsFalse(behaviourInfo.IsWaveBufferVersion2Supported());
            Assert.IsFalse(behaviourInfo.IsEffectInfoVersion2Supported());
            Assert.IsFalse(behaviourInfo.UseMultiTapBiquadFilterProcessing());
            Assert.IsFalse(behaviourInfo.IsNewEffectChannelMappingSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterParameterForSplitterEnabled());
            Assert.IsFalse(behaviourInfo.IsSplitterPrevVolumeResetSupported());

            Assert.AreEqual(0.70f, behaviourInfo.GetAudioRendererProcessingTimeLimit());
            Assert.AreEqual(1, behaviourInfo.GetCommandProcessingTimeEstimatorVersion());
            Assert.AreEqual(1, behaviourInfo.GetPerformanceMetricsDataFormat());
        }

        [Test]
        public void TestRevision2()
        {
            BehaviourInfo behaviourInfo = new();

            behaviourInfo.SetUserRevision(BehaviourInfo.BaseRevisionMagic + BehaviourInfo.Revision2);

            Assert.IsTrue(behaviourInfo.IsAdpcmLoopContextBugFixed());
            Assert.IsTrue(behaviourInfo.IsSplitterSupported());
            Assert.IsFalse(behaviourInfo.IsLongSizePreDelaySupported());
            Assert.IsFalse(behaviourInfo.IsAudioUsbDeviceOutputSupported());
            Assert.IsFalse(behaviourInfo.IsFlushVoiceWaveBuffersSupported());
            Assert.IsFalse(behaviourInfo.IsSplitterBugFixed());
            Assert.IsFalse(behaviourInfo.IsElapsedFrameCountSupported());
            Assert.IsFalse(behaviourInfo.IsDecodingBehaviourFlagSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterEffectStateClearBugFixed());
            Assert.IsFalse(behaviourInfo.IsMixInParameterDirtyOnlyUpdateSupported());
            Assert.IsFalse(behaviourInfo.IsWaveBufferVersion2Supported());
            Assert.IsFalse(behaviourInfo.IsEffectInfoVersion2Supported());
            Assert.IsFalse(behaviourInfo.UseMultiTapBiquadFilterProcessing());
            Assert.IsFalse(behaviourInfo.IsNewEffectChannelMappingSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterParameterForSplitterEnabled());
            Assert.IsFalse(behaviourInfo.IsSplitterPrevVolumeResetSupported());

            Assert.AreEqual(0.70f, behaviourInfo.GetAudioRendererProcessingTimeLimit());
            Assert.AreEqual(1, behaviourInfo.GetCommandProcessingTimeEstimatorVersion());
            Assert.AreEqual(1, behaviourInfo.GetPerformanceMetricsDataFormat());
        }

        [Test]
        public void TestRevision3()
        {
            BehaviourInfo behaviourInfo = new();

            behaviourInfo.SetUserRevision(BehaviourInfo.BaseRevisionMagic + BehaviourInfo.Revision3);

            Assert.IsTrue(behaviourInfo.IsAdpcmLoopContextBugFixed());
            Assert.IsTrue(behaviourInfo.IsSplitterSupported());
            Assert.IsTrue(behaviourInfo.IsLongSizePreDelaySupported());
            Assert.IsFalse(behaviourInfo.IsAudioUsbDeviceOutputSupported());
            Assert.IsFalse(behaviourInfo.IsFlushVoiceWaveBuffersSupported());
            Assert.IsFalse(behaviourInfo.IsSplitterBugFixed());
            Assert.IsFalse(behaviourInfo.IsElapsedFrameCountSupported());
            Assert.IsFalse(behaviourInfo.IsDecodingBehaviourFlagSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterEffectStateClearBugFixed());
            Assert.IsFalse(behaviourInfo.IsMixInParameterDirtyOnlyUpdateSupported());
            Assert.IsFalse(behaviourInfo.IsWaveBufferVersion2Supported());
            Assert.IsFalse(behaviourInfo.IsEffectInfoVersion2Supported());
            Assert.IsFalse(behaviourInfo.UseMultiTapBiquadFilterProcessing());
            Assert.IsFalse(behaviourInfo.IsNewEffectChannelMappingSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterParameterForSplitterEnabled());
            Assert.IsFalse(behaviourInfo.IsSplitterPrevVolumeResetSupported());

            Assert.AreEqual(0.70f, behaviourInfo.GetAudioRendererProcessingTimeLimit());
            Assert.AreEqual(1, behaviourInfo.GetCommandProcessingTimeEstimatorVersion());
            Assert.AreEqual(1, behaviourInfo.GetPerformanceMetricsDataFormat());
        }

        [Test]
        public void TestRevision4()
        {
            BehaviourInfo behaviourInfo = new();

            behaviourInfo.SetUserRevision(BehaviourInfo.BaseRevisionMagic + BehaviourInfo.Revision4);

            Assert.IsTrue(behaviourInfo.IsAdpcmLoopContextBugFixed());
            Assert.IsTrue(behaviourInfo.IsSplitterSupported());
            Assert.IsTrue(behaviourInfo.IsLongSizePreDelaySupported());
            Assert.IsTrue(behaviourInfo.IsAudioUsbDeviceOutputSupported());
            Assert.IsFalse(behaviourInfo.IsFlushVoiceWaveBuffersSupported());
            Assert.IsFalse(behaviourInfo.IsSplitterBugFixed());
            Assert.IsFalse(behaviourInfo.IsElapsedFrameCountSupported());
            Assert.IsFalse(behaviourInfo.IsDecodingBehaviourFlagSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterEffectStateClearBugFixed());
            Assert.IsFalse(behaviourInfo.IsMixInParameterDirtyOnlyUpdateSupported());
            Assert.IsFalse(behaviourInfo.IsWaveBufferVersion2Supported());
            Assert.IsFalse(behaviourInfo.IsEffectInfoVersion2Supported());
            Assert.IsFalse(behaviourInfo.UseMultiTapBiquadFilterProcessing());
            Assert.IsFalse(behaviourInfo.IsNewEffectChannelMappingSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterParameterForSplitterEnabled());
            Assert.IsFalse(behaviourInfo.IsSplitterPrevVolumeResetSupported());

            Assert.AreEqual(0.75f, behaviourInfo.GetAudioRendererProcessingTimeLimit());
            Assert.AreEqual(1, behaviourInfo.GetCommandProcessingTimeEstimatorVersion());
            Assert.AreEqual(1, behaviourInfo.GetPerformanceMetricsDataFormat());
        }

        [Test]
        public void TestRevision5()
        {
            BehaviourInfo behaviourInfo = new();

            behaviourInfo.SetUserRevision(BehaviourInfo.BaseRevisionMagic + BehaviourInfo.Revision5);

            Assert.IsTrue(behaviourInfo.IsAdpcmLoopContextBugFixed());
            Assert.IsTrue(behaviourInfo.IsSplitterSupported());
            Assert.IsTrue(behaviourInfo.IsLongSizePreDelaySupported());
            Assert.IsTrue(behaviourInfo.IsAudioUsbDeviceOutputSupported());
            Assert.IsTrue(behaviourInfo.IsFlushVoiceWaveBuffersSupported());
            Assert.IsTrue(behaviourInfo.IsSplitterBugFixed());
            Assert.IsTrue(behaviourInfo.IsElapsedFrameCountSupported());
            Assert.IsTrue(behaviourInfo.IsDecodingBehaviourFlagSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterEffectStateClearBugFixed());
            Assert.IsFalse(behaviourInfo.IsMixInParameterDirtyOnlyUpdateSupported());
            Assert.IsFalse(behaviourInfo.IsWaveBufferVersion2Supported());
            Assert.IsFalse(behaviourInfo.IsEffectInfoVersion2Supported());
            Assert.IsFalse(behaviourInfo.UseMultiTapBiquadFilterProcessing());
            Assert.IsFalse(behaviourInfo.IsNewEffectChannelMappingSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterParameterForSplitterEnabled());
            Assert.IsFalse(behaviourInfo.IsSplitterPrevVolumeResetSupported());

            Assert.AreEqual(0.80f, behaviourInfo.GetAudioRendererProcessingTimeLimit());
            Assert.AreEqual(2, behaviourInfo.GetCommandProcessingTimeEstimatorVersion());
            Assert.AreEqual(2, behaviourInfo.GetPerformanceMetricsDataFormat());
        }

        [Test]
        public void TestRevision6()
        {
            BehaviourInfo behaviourInfo = new();

            behaviourInfo.SetUserRevision(BehaviourInfo.BaseRevisionMagic + BehaviourInfo.Revision6);

            Assert.IsTrue(behaviourInfo.IsAdpcmLoopContextBugFixed());
            Assert.IsTrue(behaviourInfo.IsSplitterSupported());
            Assert.IsTrue(behaviourInfo.IsLongSizePreDelaySupported());
            Assert.IsTrue(behaviourInfo.IsAudioUsbDeviceOutputSupported());
            Assert.IsTrue(behaviourInfo.IsFlushVoiceWaveBuffersSupported());
            Assert.IsTrue(behaviourInfo.IsSplitterBugFixed());
            Assert.IsTrue(behaviourInfo.IsElapsedFrameCountSupported());
            Assert.IsTrue(behaviourInfo.IsDecodingBehaviourFlagSupported());
            Assert.IsTrue(behaviourInfo.IsBiquadFilterEffectStateClearBugFixed());
            Assert.IsFalse(behaviourInfo.IsMixInParameterDirtyOnlyUpdateSupported());
            Assert.IsFalse(behaviourInfo.IsWaveBufferVersion2Supported());
            Assert.IsFalse(behaviourInfo.IsEffectInfoVersion2Supported());
            Assert.IsFalse(behaviourInfo.UseMultiTapBiquadFilterProcessing());
            Assert.IsFalse(behaviourInfo.IsNewEffectChannelMappingSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterParameterForSplitterEnabled());
            Assert.IsFalse(behaviourInfo.IsSplitterPrevVolumeResetSupported());

            Assert.AreEqual(0.80f, behaviourInfo.GetAudioRendererProcessingTimeLimit());
            Assert.AreEqual(2, behaviourInfo.GetCommandProcessingTimeEstimatorVersion());
            Assert.AreEqual(2, behaviourInfo.GetPerformanceMetricsDataFormat());
        }

        [Test]
        public void TestRevision7()
        {
            BehaviourInfo behaviourInfo = new();

            behaviourInfo.SetUserRevision(BehaviourInfo.BaseRevisionMagic + BehaviourInfo.Revision7);

            Assert.IsTrue(behaviourInfo.IsAdpcmLoopContextBugFixed());
            Assert.IsTrue(behaviourInfo.IsSplitterSupported());
            Assert.IsTrue(behaviourInfo.IsLongSizePreDelaySupported());
            Assert.IsTrue(behaviourInfo.IsAudioUsbDeviceOutputSupported());
            Assert.IsTrue(behaviourInfo.IsFlushVoiceWaveBuffersSupported());
            Assert.IsTrue(behaviourInfo.IsSplitterBugFixed());
            Assert.IsTrue(behaviourInfo.IsElapsedFrameCountSupported());
            Assert.IsTrue(behaviourInfo.IsDecodingBehaviourFlagSupported());
            Assert.IsTrue(behaviourInfo.IsBiquadFilterEffectStateClearBugFixed());
            Assert.IsTrue(behaviourInfo.IsMixInParameterDirtyOnlyUpdateSupported());
            Assert.IsFalse(behaviourInfo.IsWaveBufferVersion2Supported());
            Assert.IsFalse(behaviourInfo.IsEffectInfoVersion2Supported());
            Assert.IsFalse(behaviourInfo.UseMultiTapBiquadFilterProcessing());
            Assert.IsFalse(behaviourInfo.IsNewEffectChannelMappingSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterParameterForSplitterEnabled());
            Assert.IsFalse(behaviourInfo.IsSplitterPrevVolumeResetSupported());

            Assert.AreEqual(0.80f, behaviourInfo.GetAudioRendererProcessingTimeLimit());
            Assert.AreEqual(2, behaviourInfo.GetCommandProcessingTimeEstimatorVersion());
            Assert.AreEqual(2, behaviourInfo.GetPerformanceMetricsDataFormat());
        }

        [Test]
        public void TestRevision8()
        {
            BehaviourInfo behaviourInfo = new();

            behaviourInfo.SetUserRevision(BehaviourInfo.BaseRevisionMagic + BehaviourInfo.Revision8);

            Assert.IsTrue(behaviourInfo.IsAdpcmLoopContextBugFixed());
            Assert.IsTrue(behaviourInfo.IsSplitterSupported());
            Assert.IsTrue(behaviourInfo.IsLongSizePreDelaySupported());
            Assert.IsTrue(behaviourInfo.IsAudioUsbDeviceOutputSupported());
            Assert.IsTrue(behaviourInfo.IsFlushVoiceWaveBuffersSupported());
            Assert.IsTrue(behaviourInfo.IsSplitterBugFixed());
            Assert.IsTrue(behaviourInfo.IsElapsedFrameCountSupported());
            Assert.IsTrue(behaviourInfo.IsDecodingBehaviourFlagSupported());
            Assert.IsTrue(behaviourInfo.IsBiquadFilterEffectStateClearBugFixed());
            Assert.IsTrue(behaviourInfo.IsMixInParameterDirtyOnlyUpdateSupported());
            Assert.IsTrue(behaviourInfo.IsWaveBufferVersion2Supported());
            Assert.IsFalse(behaviourInfo.IsEffectInfoVersion2Supported());
            Assert.IsFalse(behaviourInfo.UseMultiTapBiquadFilterProcessing());
            Assert.IsFalse(behaviourInfo.IsNewEffectChannelMappingSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterParameterForSplitterEnabled());
            Assert.IsFalse(behaviourInfo.IsSplitterPrevVolumeResetSupported());

            Assert.AreEqual(0.80f, behaviourInfo.GetAudioRendererProcessingTimeLimit());
            Assert.AreEqual(3, behaviourInfo.GetCommandProcessingTimeEstimatorVersion());
            Assert.AreEqual(2, behaviourInfo.GetPerformanceMetricsDataFormat());
        }

        [Test]
        public void TestRevision9()
        {
            BehaviourInfo behaviourInfo = new();

            behaviourInfo.SetUserRevision(BehaviourInfo.BaseRevisionMagic + BehaviourInfo.Revision9);

            Assert.IsTrue(behaviourInfo.IsAdpcmLoopContextBugFixed());
            Assert.IsTrue(behaviourInfo.IsSplitterSupported());
            Assert.IsTrue(behaviourInfo.IsLongSizePreDelaySupported());
            Assert.IsTrue(behaviourInfo.IsAudioUsbDeviceOutputSupported());
            Assert.IsTrue(behaviourInfo.IsFlushVoiceWaveBuffersSupported());
            Assert.IsTrue(behaviourInfo.IsSplitterBugFixed());
            Assert.IsTrue(behaviourInfo.IsElapsedFrameCountSupported());
            Assert.IsTrue(behaviourInfo.IsDecodingBehaviourFlagSupported());
            Assert.IsTrue(behaviourInfo.IsBiquadFilterEffectStateClearBugFixed());
            Assert.IsTrue(behaviourInfo.IsMixInParameterDirtyOnlyUpdateSupported());
            Assert.IsTrue(behaviourInfo.IsWaveBufferVersion2Supported());
            Assert.IsTrue(behaviourInfo.IsEffectInfoVersion2Supported());
            Assert.IsFalse(behaviourInfo.UseMultiTapBiquadFilterProcessing());
            Assert.IsFalse(behaviourInfo.IsNewEffectChannelMappingSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterParameterForSplitterEnabled());
            Assert.IsFalse(behaviourInfo.IsSplitterPrevVolumeResetSupported());

            Assert.AreEqual(0.80f, behaviourInfo.GetAudioRendererProcessingTimeLimit());
            Assert.AreEqual(3, behaviourInfo.GetCommandProcessingTimeEstimatorVersion());
            Assert.AreEqual(2, behaviourInfo.GetPerformanceMetricsDataFormat());
        }

        [Test]
        public void TestRevision10()
        {
            BehaviourInfo behaviourInfo = new();

            behaviourInfo.SetUserRevision(BehaviourInfo.BaseRevisionMagic + BehaviourInfo.Revision10);

            Assert.IsTrue(behaviourInfo.IsAdpcmLoopContextBugFixed());
            Assert.IsTrue(behaviourInfo.IsSplitterSupported());
            Assert.IsTrue(behaviourInfo.IsLongSizePreDelaySupported());
            Assert.IsTrue(behaviourInfo.IsAudioUsbDeviceOutputSupported());
            Assert.IsTrue(behaviourInfo.IsFlushVoiceWaveBuffersSupported());
            Assert.IsTrue(behaviourInfo.IsSplitterBugFixed());
            Assert.IsTrue(behaviourInfo.IsElapsedFrameCountSupported());
            Assert.IsTrue(behaviourInfo.IsDecodingBehaviourFlagSupported());
            Assert.IsTrue(behaviourInfo.IsBiquadFilterEffectStateClearBugFixed());
            Assert.IsTrue(behaviourInfo.IsMixInParameterDirtyOnlyUpdateSupported());
            Assert.IsTrue(behaviourInfo.IsWaveBufferVersion2Supported());
            Assert.IsTrue(behaviourInfo.IsEffectInfoVersion2Supported());
            Assert.IsTrue(behaviourInfo.UseMultiTapBiquadFilterProcessing());
            Assert.IsFalse(behaviourInfo.IsNewEffectChannelMappingSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterParameterForSplitterEnabled());
            Assert.IsFalse(behaviourInfo.IsSplitterPrevVolumeResetSupported());

            Assert.AreEqual(0.80f, behaviourInfo.GetAudioRendererProcessingTimeLimit());
            Assert.AreEqual(4, behaviourInfo.GetCommandProcessingTimeEstimatorVersion());
            Assert.AreEqual(2, behaviourInfo.GetPerformanceMetricsDataFormat());
        }

        [Test]
        public void TestRevision11()
        {
            BehaviourInfo behaviourInfo = new();

            behaviourInfo.SetUserRevision(BehaviourInfo.BaseRevisionMagic + BehaviourInfo.Revision11);

            Assert.IsTrue(behaviourInfo.IsAdpcmLoopContextBugFixed());
            Assert.IsTrue(behaviourInfo.IsSplitterSupported());
            Assert.IsTrue(behaviourInfo.IsLongSizePreDelaySupported());
            Assert.IsTrue(behaviourInfo.IsAudioUsbDeviceOutputSupported());
            Assert.IsTrue(behaviourInfo.IsFlushVoiceWaveBuffersSupported());
            Assert.IsTrue(behaviourInfo.IsSplitterBugFixed());
            Assert.IsTrue(behaviourInfo.IsElapsedFrameCountSupported());
            Assert.IsTrue(behaviourInfo.IsDecodingBehaviourFlagSupported());
            Assert.IsTrue(behaviourInfo.IsBiquadFilterEffectStateClearBugFixed());
            Assert.IsTrue(behaviourInfo.IsMixInParameterDirtyOnlyUpdateSupported());
            Assert.IsTrue(behaviourInfo.IsWaveBufferVersion2Supported());
            Assert.IsTrue(behaviourInfo.IsEffectInfoVersion2Supported());
            Assert.IsTrue(behaviourInfo.UseMultiTapBiquadFilterProcessing());
            Assert.IsTrue(behaviourInfo.IsNewEffectChannelMappingSupported());
            Assert.IsFalse(behaviourInfo.IsBiquadFilterParameterForSplitterEnabled());
            Assert.IsFalse(behaviourInfo.IsSplitterPrevVolumeResetSupported());

            Assert.AreEqual(0.80f, behaviourInfo.GetAudioRendererProcessingTimeLimit());
            Assert.AreEqual(5, behaviourInfo.GetCommandProcessingTimeEstimatorVersion());
            Assert.AreEqual(2, behaviourInfo.GetPerformanceMetricsDataFormat());
        }

        [Test]
        public void TestRevision12()
        {
            BehaviourInfo behaviourInfo = new();

            behaviourInfo.SetUserRevision(BehaviourInfo.BaseRevisionMagic + BehaviourInfo.Revision12);

            Assert.IsTrue(behaviourInfo.IsAdpcmLoopContextBugFixed());
            Assert.IsTrue(behaviourInfo.IsSplitterSupported());
            Assert.IsTrue(behaviourInfo.IsLongSizePreDelaySupported());
            Assert.IsTrue(behaviourInfo.IsAudioUsbDeviceOutputSupported());
            Assert.IsTrue(behaviourInfo.IsFlushVoiceWaveBuffersSupported());
            Assert.IsTrue(behaviourInfo.IsSplitterBugFixed());
            Assert.IsTrue(behaviourInfo.IsElapsedFrameCountSupported());
            Assert.IsTrue(behaviourInfo.IsDecodingBehaviourFlagSupported());
            Assert.IsTrue(behaviourInfo.IsBiquadFilterEffectStateClearBugFixed());
            Assert.IsTrue(behaviourInfo.IsMixInParameterDirtyOnlyUpdateSupported());
            Assert.IsTrue(behaviourInfo.IsWaveBufferVersion2Supported());
            Assert.IsTrue(behaviourInfo.IsEffectInfoVersion2Supported());
            Assert.IsTrue(behaviourInfo.UseMultiTapBiquadFilterProcessing());
            Assert.IsTrue(behaviourInfo.IsNewEffectChannelMappingSupported());
            Assert.IsTrue(behaviourInfo.IsBiquadFilterParameterForSplitterEnabled());
            Assert.IsFalse(behaviourInfo.IsSplitterPrevVolumeResetSupported());

            Assert.AreEqual(0.80f, behaviourInfo.GetAudioRendererProcessingTimeLimit());
            Assert.AreEqual(5, behaviourInfo.GetCommandProcessingTimeEstimatorVersion());
            Assert.AreEqual(2, behaviourInfo.GetPerformanceMetricsDataFormat());
        }

        [Test]
        public void TestRevision13()
        {
            BehaviourInfo behaviourInfo = new();

            behaviourInfo.SetUserRevision(BehaviourInfo.BaseRevisionMagic + BehaviourInfo.Revision13);

            Assert.IsTrue(behaviourInfo.IsAdpcmLoopContextBugFixed());
            Assert.IsTrue(behaviourInfo.IsSplitterSupported());
            Assert.IsTrue(behaviourInfo.IsLongSizePreDelaySupported());
            Assert.IsTrue(behaviourInfo.IsAudioUsbDeviceOutputSupported());
            Assert.IsTrue(behaviourInfo.IsFlushVoiceWaveBuffersSupported());
            Assert.IsTrue(behaviourInfo.IsSplitterBugFixed());
            Assert.IsTrue(behaviourInfo.IsElapsedFrameCountSupported());
            Assert.IsTrue(behaviourInfo.IsDecodingBehaviourFlagSupported());
            Assert.IsTrue(behaviourInfo.IsBiquadFilterEffectStateClearBugFixed());
            Assert.IsTrue(behaviourInfo.IsMixInParameterDirtyOnlyUpdateSupported());
            Assert.IsTrue(behaviourInfo.IsWaveBufferVersion2Supported());
            Assert.IsTrue(behaviourInfo.IsEffectInfoVersion2Supported());
            Assert.IsTrue(behaviourInfo.UseMultiTapBiquadFilterProcessing());
            Assert.IsTrue(behaviourInfo.IsNewEffectChannelMappingSupported());
            Assert.IsTrue(behaviourInfo.IsBiquadFilterParameterForSplitterEnabled());
            Assert.IsTrue(behaviourInfo.IsSplitterPrevVolumeResetSupported());

            Assert.AreEqual(0.80f, behaviourInfo.GetAudioRendererProcessingTimeLimit());
            Assert.AreEqual(5, behaviourInfo.GetCommandProcessingTimeEstimatorVersion());
            Assert.AreEqual(2, behaviourInfo.GetPerformanceMetricsDataFormat());
        }
    }
}
