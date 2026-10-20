using Silk.NET.Vulkan;
using System;
using System.Diagnostics;

namespace Ryujinx.Graphics.Vulkan
{
    class ResourceArray : IDisposable
    {
        private DescriptorSet[] _cachedDescriptorSets;

        private ShaderCollection _cachedDscProgram;
        private int _cachedDscSetIndex;
        private int _cachedDscIndex;

        // The last descriptor sets written, kept after the array changed so that the next sets can be
        // copied from them and only the changed entries written, instead of writing the whole array.
        private DescriptorSet[] _staleDescriptorSets;

        private ShaderCollection _staleDscProgram;
        private int _staleDscSetIndex;
        private int _staleDscIndex;

        private int _bindCount;

        protected void SetDirty(VulkanRenderer gd, bool isImage)
        {
            MakeDescriptorSetStale();

            if (_bindCount != 0)
            {
                if (isImage)
                {
                    gd.PipelineInternal.ForceImageDirty();
                }
                else
                {
                    gd.PipelineInternal.ForceTextureDirty();
                }
            }
        }

        public bool TryGetCachedDescriptorSets(CommandBufferScoped cbs, ShaderCollection program, int setIndex, out DescriptorSet[] sets)
        {
            if (_cachedDescriptorSets != null)
            {
                _cachedDscProgram.UpdateManualDescriptorSetCollectionOwnership(cbs, _cachedDscSetIndex, _cachedDscIndex);

                sets = _cachedDescriptorSets;

                return true;
            }

            DescriptorSetCollection dsc = program.GetNewManualDescriptorSetCollection(cbs, setIndex, out _cachedDscIndex).Get(cbs);

            sets = dsc.GetSets();

            _cachedDescriptorSets = sets;
            _cachedDscProgram = program;
            _cachedDscSetIndex = setIndex;

            return false;
        }

        public void IncrementBindCount()
        {
            _bindCount++;
        }

        public void DecrementBindCount()
        {
            int newBindCount = --_bindCount;
            Debug.Assert(newBindCount >= 0);
        }

        /// <summary>
        /// Keeps the current descriptor sets as stale ones, for the next sets to be copied from, and forgets them as current.
        /// </summary>
        protected void MakeDescriptorSetStale()
        {
            if (_cachedDescriptorSets != null)
            {
                ReleaseStaleDescriptorSet();

                _staleDescriptorSets = _cachedDescriptorSets;
                _staleDscProgram = _cachedDscProgram;
                _staleDscSetIndex = _cachedDscSetIndex;
                _staleDscIndex = _cachedDscIndex;

                _cachedDescriptorSets = null;
            }
        }

        /// <summary>
        /// Gets the stale descriptor set, if there is one with the same layout as the given program's.
        /// </summary>
        /// <param name="program">Program the new sets were created for</param>
        /// <param name="set">The stale descriptor set</param>
        /// <returns>True if there is a stale set to copy from, false otherwise</returns>
        protected bool TryGetStaleDescriptorSet(ShaderCollection program, out DescriptorSet set)
        {
            if (_staleDescriptorSets != null && program.HasSameLayout(_staleDscProgram))
            {
                set = _staleDescriptorSets[0];

                return true;
            }

            set = default;

            return false;
        }

        protected void ReleaseStaleDescriptorSet()
        {
            if (_staleDescriptorSets != null)
            {
                _staleDscProgram.ReleaseManualDescriptorSetCollection(_staleDscSetIndex, _staleDscIndex);
                _staleDescriptorSets = null;
            }
        }

        protected void ReleaseDescriptorSet()
        {
            if (_cachedDescriptorSets != null)
            {
                _cachedDscProgram.ReleaseManualDescriptorSetCollection(_cachedDscSetIndex, _cachedDscIndex);
                _cachedDescriptorSets = null;
            }
        }

        public void Dispose()
        {
            ReleaseStaleDescriptorSet();
            ReleaseDescriptorSet();
        }
    }
}
