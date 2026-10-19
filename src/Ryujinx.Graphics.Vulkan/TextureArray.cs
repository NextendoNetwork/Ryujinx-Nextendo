using Ryujinx.Graphics.GAL;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;

namespace Ryujinx.Graphics.Vulkan
{
    class TextureArray : ResourceArray, ITextureArray
    {
        private readonly VulkanRenderer _gd;

        private struct TextureRef
        {
            public TextureStorage Storage;
            public Auto<DisposableImageView> View;
            public Auto<DisposableSampler> Sampler;
        }

        private readonly TextureRef[] _textureRefs;
        private readonly TextureBuffer[] _bufferTextureRefs;

        private readonly DescriptorImageInfo[] _textures;
        private readonly BufferView[] _bufferTextures;
        private readonly int[] _bufferViewGenerations;

        // Indices of the buffer texture slots that are set, null if they must be found again.
        // Large arrays are mostly empty, so only these slots are looked at when the command buffer changes.
        private List<int> _bufferIndices;
        private BufferView _dummyBufferView;
        private int _dummyBufferViewGeneration;

        private HashSet<TextureStorage> _storages;

        private int _cachedCommandBufferIndex;
        private int _cachedSubmissionCount;
        private int _cachedBufferViewReleaseCount;

        private readonly bool _isBuffer;

        public TextureArray(VulkanRenderer gd, int size, bool isBuffer)
        {
            _gd = gd;

            if (isBuffer)
            {
                _bufferTextureRefs = new TextureBuffer[size];
                _bufferTextures = new BufferView[size];
                _bufferViewGenerations = new int[size];
            }
            else
            {
                _textureRefs = new TextureRef[size];
                _textures = new DescriptorImageInfo[size];
            }

            _storages = null;

            _cachedCommandBufferIndex = -1;
            _cachedSubmissionCount = 0;

            _isBuffer = isBuffer;
        }

        public void SetSamplers(int index, ISampler[] samplers)
        {
            // Buffer textures have no sampler.
            if (_isBuffer)
            {
                return;
            }

            bool changed = false;

            for (int i = 0; i < samplers.Length; i++)
            {
                Auto<DisposableSampler> sampler = (samplers[i] as SamplerHolder)?.GetSampler();

                if (_textureRefs[index + i].Sampler != sampler)
                {
                    _textureRefs[index + i].Sampler = sampler;
                    changed = true;
                }
            }

            // Games may update the whole array without changing anything. Rewriting every descriptor
            // is very costly for large arrays, so only do it when something actually changed.
            if (changed)
            {
                SetDirty();
            }
        }

        public void SetTextures(int index, ITexture[] textures)
        {
            bool changed = false;

            for (int i = 0; i < textures.Length; i++)
            {
                ITexture texture = textures[i];

                // A texture of the wrong kind (e.g. from a garbage pool descriptor) is bound as null,
                // as only the references matching the array kind are allocated.
                if (_isBuffer)
                {
                    TextureBuffer bufferTexture = texture as TextureBuffer;

                    // The same buffer texture must still be written again if its view changed since it was last written.
                    if (_bufferTextureRefs[index + i] != bufferTexture ||
                        (bufferTexture != null && bufferTexture.ViewGeneration != _bufferViewGenerations[index + i]))
                    {
                        if (_bufferTextureRefs[index + i] != bufferTexture)
                        {
                            _bufferIndices = null;
                        }

                        _bufferTextureRefs[index + i] = bufferTexture;
                        changed = true;
                    }
                }
                else
                {
                    TextureView view = texture as TextureView;
                    TextureStorage storage = view?.Storage;
                    Auto<DisposableImageView> imageView = view?.GetImageView();

                    if (_textureRefs[index + i].Storage != storage || _textureRefs[index + i].View != imageView)
                    {
                        _textureRefs[index + i].Storage = storage;
                        _textureRefs[index + i].View = imageView;
                        changed = true;
                    }
                }
            }

            // Games may update the whole array without changing anything. Rewriting every descriptor
            // is very costly for large arrays, so only do it when something actually changed.
            if (changed)
            {
                SetDirty();
            }
        }

        private void SetDirty()
        {
            _cachedCommandBufferIndex = -1;
            _storages = null;
            SetDirty(_gd, isImage: false);
        }

        public void QueueWriteToReadBarriers(CommandBufferScoped cbs, PipelineStageFlags stageFlags)
        {
            // Buffer textures have no texture storage.
            if (_isBuffer)
            {
                return;
            }

            HashSet<TextureStorage> storages = _storages;

            if (storages == null)
            {
                storages = [];

                for (int index = 0; index < _textureRefs.Length; index++)
                {
                    if (_textureRefs[index].Storage != null)
                    {
                        storages.Add(_textureRefs[index].Storage);
                    }
                }

                _storages = storages;
            }

            foreach (TextureStorage storage in storages)
            {
                storage.QueueWriteToReadBarrier(cbs, AccessFlags.ShaderReadBit, stageFlags);
            }
        }

        public ReadOnlySpan<DescriptorImageInfo> GetImageInfos(VulkanRenderer gd, CommandBufferScoped cbs, TextureView dummyTexture, SamplerHolder dummySampler)
        {
            int submissionCount = gd.CommandBufferPool.GetSubmissionCount(cbs.CommandBufferIndex);

            Span<DescriptorImageInfo> textures = _textures;

            if (cbs.CommandBufferIndex == _cachedCommandBufferIndex && submissionCount == _cachedSubmissionCount)
            {
                return textures;
            }

            _cachedCommandBufferIndex = cbs.CommandBufferIndex;
            _cachedSubmissionCount = submissionCount;

            for (int i = 0; i < textures.Length; i++)
            {
                ref DescriptorImageInfo texture = ref textures[i];
                ref TextureRef refs = ref _textureRefs[i];

                if (i > 0 && _textureRefs[i - 1].View == refs.View && _textureRefs[i - 1].Sampler == refs.Sampler)
                {
                    texture = textures[i - 1];

                    continue;
                }

                texture.ImageLayout = ImageLayout.General;
                texture.ImageView = refs.View?.Get(cbs).Value ?? default;
                texture.Sampler = refs.Sampler?.Get(cbs).Value ?? default;

                if (texture.ImageView.Handle == 0)
                {
                    texture.ImageView = dummyTexture.GetImageView().Get(cbs).Value;
                }

                if (texture.Sampler.Handle == 0)
                {
                    texture.Sampler = dummySampler.GetSampler().Get(cbs).Value;
                }
            }

            return textures;
        }

        public ReadOnlySpan<BufferView> GetBufferViews(CommandBufferScoped cbs, TextureBuffer dummyBufferTexture)
        {
            Span<BufferView> bufferTextures = _bufferTextures;

            // Empty slots get a valid view: MoltenVK crashes on null buffer views in arrays.
            BufferView dummyView = dummyBufferTexture.GetBufferView(cbs, false);
            _dummyBufferView = dummyView;
            _dummyBufferViewGeneration = dummyBufferTexture.ViewGeneration;

            for (int i = 0; i < bufferTextures.Length; i++)
            {
                TextureBuffer bufferTexture = _bufferTextureRefs[i];

                if (bufferTexture == null)
                {
                    bufferTextures[i] = dummyView;
                }
                else
                {
                    bufferTextures[i] = bufferTexture.GetBufferView(cbs, false);
                    _bufferViewGenerations[i] = bufferTexture.ViewGeneration;
                }
            }

            return bufferTextures;
        }

        private List<int> GetBufferIndices()
        {
            if (_bufferIndices == null)
            {
                _bufferIndices = [];

                for (int i = 0; i < _bufferTextureRefs.Length; i++)
                {
                    if (_bufferTextureRefs[i] != null)
                    {
                        _bufferIndices.Add(i);
                    }
                }
            }

            return _bufferIndices;
        }

        /// <summary>
        /// Gets the current buffer views, returning true if any differs from the last ones written to the descriptor set.
        /// </summary>
        private bool UpdateBufferViews(CommandBufferScoped cbs, TextureBuffer dummyBufferTexture)
        {
            int submissionCount = _gd.CommandBufferPool.GetSubmissionCount(cbs.CommandBufferIndex);

            // Checking every view on every draw is too costly for large arrays. The views are already referenced
            // by this command buffer, and none can be stale if no buffer texture released its view since.
            bool sameCommandBuffer = cbs.CommandBufferIndex == _cachedCommandBufferIndex && submissionCount == _cachedSubmissionCount;

            if (sameCommandBuffer && _gd.BufferViewReleaseCount == _cachedBufferViewReleaseCount)
            {
                return false;
            }

            _cachedCommandBufferIndex = cbs.CommandBufferIndex;
            _cachedSubmissionCount = submissionCount;
            _cachedBufferViewReleaseCount = _gd.BufferViewReleaseCount;

            bool changed = false;

            // All empty slots share the dummy view, so it only needs to be looked at once.
            if (!sameCommandBuffer || dummyBufferTexture.ViewGeneration != _dummyBufferViewGeneration)
            {
                BufferView dummyView = dummyBufferTexture.GetBufferView(cbs, false);
                _dummyBufferViewGeneration = dummyBufferTexture.ViewGeneration;

                if (dummyView.Handle != _dummyBufferView.Handle)
                {
                    _dummyBufferView = dummyView;

                    for (int i = 0; i < _bufferTextures.Length; i++)
                    {
                        if (_bufferTextureRefs[i] == null)
                        {
                            _bufferTextures[i] = dummyView;
                        }
                    }

                    changed = true;
                }
            }

            foreach (int i in GetBufferIndices())
            {
                TextureBuffer bufferTexture = _bufferTextureRefs[i];

                // On the same command buffer, only the views released since need to be looked at again.
                if (sameCommandBuffer && bufferTexture.ViewGeneration == _bufferViewGenerations[i])
                {
                    continue;
                }

                BufferView view = bufferTexture.GetBufferView(cbs, false);
                _bufferViewGenerations[i] = bufferTexture.ViewGeneration;

                if (view.Handle != _bufferTextures[i].Handle)
                {
                    _bufferTextures[i] = view;
                    changed = true;
                }
            }

            return changed;
        }

        public DescriptorSet[] GetDescriptorSets(
            Device device,
            CommandBufferScoped cbs,
            DescriptorSetTemplateUpdater templateUpdater,
            ShaderCollection program,
            int setIndex,
            TextureView dummyTexture,
            SamplerHolder dummySampler,
            TextureBuffer dummyBufferTexture)
        {
            bool bufferViewsUpToDate = false;

            if (TryGetCachedDescriptorSets(cbs, program, setIndex, out DescriptorSet[] sets))
            {
                // We still need to ensure the current command buffer holds a reference to all used textures.

                if (!_isBuffer)
                {
                    GetImageInfos(_gd, cbs, dummyTexture, dummySampler);

                    return sets;
                }

                if (!UpdateBufferViews(cbs, dummyBufferTexture))
                {
                    return sets;
                }

                // A buffer texture got a new view (its storage was changed by another binding, which releases
                // the old view), so the cached set references a destroyed view and must be written again.
                // The views were just brought up to date, so they need not be gathered again.
                ReleaseDescriptorSet();
                TryGetCachedDescriptorSets(cbs, program, setIndex, out sets);
                bufferViewsUpToDate = true;
            }

            DescriptorSetTemplate template = program.Templates[setIndex];

            DescriptorSetTemplateWriter tu = templateUpdater.Begin(template);

            if (!_isBuffer)
            {
                tu.Push(GetImageInfos(_gd, cbs, dummyTexture, dummySampler));
            }
            else
            {
                tu.Push(bufferViewsUpToDate ? (ReadOnlySpan<BufferView>)_bufferTextures : GetBufferViews(cbs, dummyBufferTexture));
            }

            templateUpdater.Commit(_gd, device, sets[0]);

            return sets;
        }
    }
}
