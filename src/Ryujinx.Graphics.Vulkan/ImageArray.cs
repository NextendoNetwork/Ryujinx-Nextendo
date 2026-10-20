using Ryujinx.Graphics.GAL;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;

namespace Ryujinx.Graphics.Vulkan
{
    class ImageArray : ResourceArray, IImageArray
    {
        private readonly VulkanRenderer _gd;

        private record struct TextureRef
        {
            public TextureStorage Storage;
            public TextureView View;
        }

        private readonly TextureRef[] _textureRefs;
        private readonly TextureBuffer[] _bufferTextureRefs;

        private readonly DescriptorImageInfo[] _textures;
        private readonly BufferView[] _bufferTextures;
        private readonly int[] _bufferViewGenerations;

        private HashSet<TextureStorage> _storages;

        private int _cachedCommandBufferIndex;
        private int _cachedSubmissionCount;
        private int _cachedBufferViewReleaseCount;

        private readonly bool _isBuffer;

        public ImageArray(VulkanRenderer gd, int size, bool isBuffer)
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

        public void SetImages(int index, ITexture[] images)
        {
            for (int i = 0; i < images.Length; i++)
            {
                ITexture image = images[i];

                // An image of the wrong kind (e.g. from a garbage pool descriptor) is bound as null,
                // as only the references matching the array kind are allocated.
                if (_isBuffer)
                {
                    _bufferTextureRefs[index + i] = image as TextureBuffer;
                }
                else if (image is TextureView view)
                {
                    _textureRefs[index + i].Storage = view.Storage;
                    _textureRefs[index + i].View = view;
                }
                else
                {
                    _textureRefs[index + i].Storage = null;
                    _textureRefs[index + i].View = default;
                }
            }

            SetDirty();
        }

        private void SetDirty()
        {
            _cachedCommandBufferIndex = -1;
            _storages = null;
            SetDirty(_gd, isImage: true);
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

        public ReadOnlySpan<DescriptorImageInfo> GetImageInfos(VulkanRenderer gd, CommandBufferScoped cbs, TextureView dummyTexture)
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

                if (i > 0 && _textureRefs[i - 1].View == refs.View)
                {
                    texture = textures[i - 1];

                    continue;
                }

                texture.ImageLayout = ImageLayout.General;
                texture.ImageView = refs.View?.GetIdentityImageView().Get(cbs).Value ?? default;

                if (texture.ImageView.Handle == 0)
                {
                    texture.ImageView = dummyTexture.GetImageView().Get(cbs).Value;
                }
            }

            return textures;
        }

        public ReadOnlySpan<BufferView> GetBufferViews(CommandBufferScoped cbs, TextureBuffer dummyBufferTexture)
        {
            Span<BufferView> bufferTextures = _bufferTextures;

            for (int i = 0; i < bufferTextures.Length; i++)
            {
                // Empty slots get a valid view: MoltenVK crashes on null buffer views in arrays.
                TextureBuffer bufferTexture = _bufferTextureRefs[i] ?? dummyBufferTexture;

                bufferTextures[i] = bufferTexture.GetBufferView(cbs, true);
                _bufferViewGenerations[i] = bufferTexture.ViewGeneration;
            }

            return bufferTextures;
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

            for (int i = 0; i < _bufferTextures.Length; i++)
            {
                TextureBuffer bufferTexture = _bufferTextureRefs[i] ?? dummyBufferTexture;

                // On the same command buffer, only the views released since need to be looked at again.
                if (sameCommandBuffer && bufferTexture.ViewGeneration == _bufferViewGenerations[i])
                {
                    continue;
                }

                BufferView view = bufferTexture.GetBufferView(cbs, true);
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
            TextureBuffer dummyBufferTexture)
        {
            if (TryGetCachedDescriptorSets(cbs, program, setIndex, out DescriptorSet[] sets))
            {
                // We still need to ensure the current command buffer holds a reference to all used textures.

                if (!_isBuffer)
                {
                    GetImageInfos(_gd, cbs, dummyTexture);

                    return sets;
                }

                if (!UpdateBufferViews(cbs, dummyBufferTexture))
                {
                    return sets;
                }

                // A buffer texture got a new view (its storage was changed by another binding, which releases
                // the old view), so the cached set references a destroyed view and must be written again.
                ReleaseDescriptorSet();
                TryGetCachedDescriptorSets(cbs, program, setIndex, out sets);
            }

            DescriptorSetTemplate template = program.Templates[setIndex];

            DescriptorSetTemplateWriter tu = templateUpdater.Begin(template);

            if (!_isBuffer)
            {
                tu.Push(GetImageInfos(_gd, cbs, dummyTexture));
            }
            else
            {
                tu.Push(GetBufferViews(cbs, dummyBufferTexture));
            }

            templateUpdater.Commit(_gd, device, sets[0]);

            ReleaseStaleDescriptorSet();

            return sets;
        }
    }
}
