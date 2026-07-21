using Ryujinx.Audio.Renderer.Common;
using Ryujinx.Audio.Renderer.Utils;
using System;
using System.Diagnostics;

namespace Ryujinx.Audio.Renderer.Server.Voice
{
    /// <summary>
    /// Voice context.
    /// </summary>
    public class VoiceContext
    {
        /// <summary>
        /// Storage of the sorted indices to <see cref="VoiceInfo"/>.
        /// </summary>
        private Memory<int> _sortedVoices;

        /// <summary>
        /// Storage for <see cref="VoiceInfo"/>.
        /// </summary>
        private Memory<VoiceInfo> _voices;

        /// <summary>
        /// Storage for <see cref="VoiceChannelResource"/>.
        /// </summary>
        private Memory<VoiceChannelResource> _voiceChannelResources;

        /// <summary>
        /// Storage for <see cref="VoiceState"/> that are used during audio renderer server updates.
        /// </summary>
        private Memory<VoiceState> _voiceStatesCpu;

        /// <summary>
        /// Storage for <see cref="VoiceState"/> for the <see cref="Dsp.AudioProcessor"/>.
        /// </summary>
        private Memory<VoiceState> _voiceStatesDsp;

        /// <summary>
        /// The total voice count.
        /// </summary>
        private uint _voiceCount;

        public void Initialize(Memory<int> sortedVoices, Memory<VoiceInfo> voices, Memory<VoiceChannelResource> voiceChannelResources, Memory<VoiceState> voiceStatesCpu, Memory<VoiceState> voiceStatesDsp, uint voiceCount)
        {
            _sortedVoices = sortedVoices;
            _voices = voices;
            _voiceChannelResources = voiceChannelResources;
            _voiceStatesCpu = voiceStatesCpu;
            _voiceStatesDsp = voiceStatesDsp;
            _voiceCount = voiceCount;
        }

        /// <summary>
        /// Get the total voice count.
        /// </summary>
        /// <returns>The total voice count.</returns>
        public uint GetCount()
        {
            return _voiceCount;
        }

        /// <summary>
        /// Get a reference to a <see cref="VoiceChannelResource"/> at the given <paramref name="id"/>.
        /// </summary>
        /// <param name="id">The index to use.</param>
        /// <returns>A reference to a <see cref="VoiceChannelResource"/> at the given <paramref name="id"/>.</returns>
        public ref VoiceChannelResource GetChannelResource(int id)
        {
            return ref SpanIOHelper.GetFromMemory(_voiceChannelResources, id, _voiceCount);
        }

        /// <summary>
        /// Get a <see cref="Memory{VoiceState}"/> at the given <paramref name="id"/>.
        /// </summary>
        /// <param name="id">The index to use.</param>
        /// <returns>A <see cref="Memory{VoiceState}"/> at the given <paramref name="id"/>.</returns>
        /// <remarks>The returned <see cref="Memory{VoiceState}"/> should only be used when updating the server state.</remarks>
        public Memory<VoiceState> GetUpdateStateForCpu(int id)
        {
            return SpanIOHelper.GetMemory(_voiceStatesCpu, id, _voiceCount);
        }

        /// <summary>
        /// Get a <see cref="Memory{VoiceState}"/> at the given <paramref name="id"/>.
        /// </summary>
        /// <param name="id">The index to use.</param>
        /// <returns>A <see cref="Memory{VoiceState}"/> at the given <paramref name="id"/>.</returns>
        /// <remarks>The returned <see cref="Memory{VoiceState}"/> should only be used in the context of processing on the <see cref="Dsp.AudioProcessor"/>.</remarks>
        public Memory<VoiceState> GetUpdateStateForDsp(int id)
        {
            return SpanIOHelper.GetMemory(_voiceStatesDsp, id, _voiceCount);
        }

        /// <summary>
        /// Get a reference to a <see cref="VoiceInfo"/> at the given <paramref name="id"/>.
        /// </summary>
        /// <param name="id">The index to use.</param>
        /// <returns>A reference to a <see cref="VoiceInfo"/> at the given <paramref name="id"/>.</returns>
        public ref VoiceInfo GetState(int id)
        {
            return ref SpanIOHelper.GetFromMemory(_voices, id, _voiceCount);
        }

        public ref VoiceInfo GetSortedState(int id)
        {
            Debug.Assert(id >= 0 && id < _voiceCount);

            return ref GetState(_sortedVoices.Span[id]);
        }

        /// <summary>
        /// Update internal state during command generation.
        /// </summary>
        public void UpdateForCommandGeneration()
        {
            _voiceStatesDsp.CopyTo(_voiceStatesCpu);
        }

        /// <summary>
        /// Sort the internal voices by priority and sorting order (if the priorities match).
        /// </summary>
        public void Sort()
        {
            for (int i = 0; i < _voiceCount; i++)
            {
                _sortedVoices.Span[i] = i;
            }

            Span<int> sortedVoicesTemp = _sortedVoices[..(int)_voiceCount].Span;

            sortedVoicesTemp.Sort((a, b) =>
            {
                ref VoiceInfo aInfo = ref GetState(a);
                ref VoiceInfo bInfo = ref GetState(b);

                int result = aInfo.Priority.CompareTo(bInfo.Priority);

                if (result == 0)
                {
                    return aInfo.SortingOrder.CompareTo(bInfo.SortingOrder);
                }

                return result;
            });

            // sortedVoicesTemp.CopyTo(_sortedVoices.Span);
        }
    }
}
