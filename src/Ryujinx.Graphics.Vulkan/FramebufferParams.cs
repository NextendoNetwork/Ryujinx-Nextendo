using Ryujinx.Graphics.GAL;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Linq;
using Format = Ryujinx.Graphics.GAL.Format;
using VkFormat = Silk.NET.Vulkan.Format;

namespace Ryujinx.Graphics.Vulkan
{
    class FramebufferParams
    {
        private readonly Device _device;
        private Auto<DisposableImageView>[] _attachments;
        private TextureView[] _colors;
        private TextureView _depthStencil;
        private TextureView[] _colorsCanonical;
        private TextureView _baseAttachment;
        private uint _validColorAttachments;
        private int _totalCount;

        public uint Width { get; private set; }
        public uint Height { get; private set; }
        public uint Layers { get; private set; }

        public uint[] AttachmentSamples { get; private set; }
        public VkFormat[] AttachmentFormats { get; private set; }
        public int[] AttachmentIndices { get; private set; }
        public uint AttachmentIntegerFormatMask { get; private set; }
        public bool LogicOpsAllowed { get; private set; }

        public int AttachmentsCount { get; private set; }
        public int MaxColorAttachmentIndex => ColorAttachmentsCount > 0 ? AttachmentIndices[ColorAttachmentsCount - 1] : -1;
        public bool HasDepthStencil { get; private set; }
        public int ColorAttachmentsCount => AttachmentsCount - (HasDepthStencil ? 1 : 0);

        public FramebufferParams(Device device, TextureView view, uint width, uint height)
        {
            Format format = view.Info.Format;

            bool isDepthStencil = format.IsDepthOrStencil;

            _device = device;
            _attachments = [view.GetImageViewForAttachment()];
            _validColorAttachments = isDepthStencil ? 0u : 1u;
            _baseAttachment = view;

            if (isDepthStencil)
            {
                _depthStencil = view;
            }
            else
            {
                _colors = [view];
                _colorsCanonical = [view];
            }

            Width = width;
            Height = height;
            Layers = 1;

            AttachmentSamples = [(uint)view.Info.Samples];
            AttachmentFormats = [view.VkFormat];
            AttachmentIndices = isDepthStencil ? [] : [0];
            AttachmentIntegerFormatMask = format.IsInt ? 1u : 0u;
            LogicOpsAllowed = !format.IsFloatOrSrgb;

            AttachmentsCount = 1;
            _totalCount = 1;

            HasDepthStencil = isDepthStencil;
        }

        public FramebufferParams(Device device, ReadOnlySpan<ITexture> colors, ITexture depthStencil)
        {
            _device = device;

            int colorsCount = 0;
            _colorsCanonical = new TextureView[colors.Length];

            for (int i = 0; i < colors.Length; i++)
            {
                ITexture color = colors[i];
                if (color is TextureView { Valid: true } view)
                {
                    colorsCount++;
                    _colorsCanonical[i] = view;
                }
                else
                {
                    _colorsCanonical[i] = null;
                }
            }

            int count = colorsCount + (IsValidTextureView(depthStencil) ? 1 : 0);

            _attachments = new Auto<DisposableImageView>[count];
            _colors = new TextureView[colorsCount];

            AttachmentSamples = new uint[count];
            AttachmentFormats = new VkFormat[count];
            AttachmentIndices = new int[colorsCount];

            uint width = uint.MaxValue;
            uint height = uint.MaxValue;
            uint layers = uint.MaxValue;

            int index = 0;
            int bindIndex = 0;
            uint attachmentIntegerFormatMask = 0;
            bool allFormatsFloatOrSrgb = colorsCount != 0;

            foreach (ITexture color in colors)
            {
                if (IsValidTextureView(color))
                {
                    TextureView texture = (TextureView)color;

                    _attachments[index] = texture.GetImageViewForAttachment();
                    _colors[index] = texture;
                    _validColorAttachments |= 1u << bindIndex;
                    _baseAttachment = texture;

                    AttachmentSamples[index] = (uint)texture.Info.Samples;
                    AttachmentFormats[index] = texture.VkFormat;
                    AttachmentIndices[index] = bindIndex;

                    Format format = texture.Info.Format;

                    if (format.IsInt)
                    {
                        attachmentIntegerFormatMask |= 1u << bindIndex;
                    }

                    allFormatsFloatOrSrgb &= format.IsFloatOrSrgb;

                    width = Math.Min(width, (uint)texture.Width);
                    height = Math.Min(height, (uint)texture.Height);
                    layers = Math.Min(layers, (uint)texture.Layers);

                    if (++index >= colorsCount)
                    {
                        break;
                    }
                }

                bindIndex++;
            }

            AttachmentIntegerFormatMask = attachmentIntegerFormatMask;
            LogicOpsAllowed = !allFormatsFloatOrSrgb;

            if (depthStencil is TextureView { Valid: true } dsTexture)
            {
                _attachments[count - 1] = dsTexture.GetImageViewForAttachment();
                _depthStencil = dsTexture;
                _baseAttachment ??= dsTexture;

                AttachmentSamples[count - 1] = (uint)dsTexture.Info.Samples;
                AttachmentFormats[count - 1] = dsTexture.VkFormat;

                width = Math.Min(width, (uint)dsTexture.Width);
                height = Math.Min(height, (uint)dsTexture.Height);
                layers = Math.Min(layers, (uint)dsTexture.Layers);

                HasDepthStencil = true;
            }

            if (count == 0)
            {
                width = height = layers = 1;
            }

            Width = width;
            Height = height;
            Layers = layers;

            AttachmentsCount = count;
            _totalCount = colors.Length;
        }
        
        public FramebufferParams Update(ReadOnlySpan<ITexture> colors, ITexture depthStencil)
        {
            int colorsCount = 0;
            
            foreach (ITexture color in colors)
            {
                if (IsValidTextureView(color))
                {
                    colorsCount++;
                }
            }

            int count = colorsCount + (IsValidTextureView(depthStencil) ? 1 : 0);
            
            Array.Clear(_attachments);
            Array.Clear(_colors);

            if (_attachments.Length < count)
            {
                Array.Resize(ref _attachments, count);
            }
            if (_colors.Length < colorsCount)
            {
                Array.Resize(ref _colors, colorsCount);
            }
            if (_colorsCanonical.Length < colors.Length)
            {
                Array.Resize(ref _colorsCanonical, colors.Length);
            }

            for (int i = 0; i < colors.Length; i++)
            {
                ITexture color = colors[i];
                if (color is TextureView { Valid: true } view)
                {
                    _colorsCanonical[i] = view;
                }
                else
                {
                    _colorsCanonical[i] = null;
                }
            }

            Array.Clear(AttachmentSamples);
            Array.Clear(AttachmentFormats);
            Array.Clear(AttachmentIndices);
            
            if (AttachmentSamples.Length < count)
            {
                uint[] attachmentSamples = AttachmentSamples;
                Array.Resize(ref attachmentSamples, count);
                AttachmentSamples = attachmentSamples;
            }
            if (AttachmentFormats.Length < count)
            {
                VkFormat[] attachmentFormats = AttachmentFormats;
                Array.Resize(ref attachmentFormats, count);
                AttachmentFormats = attachmentFormats;
            }
            if (AttachmentIndices.Length < colorsCount)
            {
                int[] attachmentIndices = AttachmentIndices;
                Array.Resize(ref attachmentIndices, colorsCount);
                AttachmentIndices = attachmentIndices;
            }
            
            uint width = uint.MaxValue;
            uint height = uint.MaxValue;
            uint layers = uint.MaxValue;

            int index = 0;
            uint attachmentIntegerFormatMask = 0;
            bool allFormatsFloatOrSrgb = colorsCount != 0;

            _validColorAttachments = 0;
            _baseAttachment = null;

            for (int bindIndex = 0; bindIndex < colors.Length; bindIndex++)
            {
                TextureView texture = _colorsCanonical[bindIndex];
                if (texture is not null)
                {
                    _attachments[index] = texture.GetImageViewForAttachment();
                    _colors[index] = texture;
                    _validColorAttachments |= 1u << bindIndex;
                    _baseAttachment = texture;

                    AttachmentSamples[index] = (uint)texture.Info.Samples;
                    AttachmentFormats[index] = texture.VkFormat;
                    AttachmentIndices[index] = bindIndex;

                    Format format = texture.Info.Format;

                    if (format.IsInt)
                    {
                        attachmentIntegerFormatMask |= 1u << bindIndex;
                    }

                    allFormatsFloatOrSrgb &= format.IsFloatOrSrgb;

                    width = Math.Min(width, (uint)texture.Width);
                    height = Math.Min(height, (uint)texture.Height);
                    layers = Math.Min(layers, (uint)texture.Layers);

                    if (++index >= colorsCount)
                    {
                        break;
                    }
                }
            }

            AttachmentIntegerFormatMask = attachmentIntegerFormatMask;
            LogicOpsAllowed = !allFormatsFloatOrSrgb;
            _depthStencil = null;
            HasDepthStencil = false;

            if (depthStencil is TextureView { Valid: true } dsTexture)
            {
                _attachments[count - 1] = dsTexture.GetImageViewForAttachment();
                _depthStencil = dsTexture;
                _baseAttachment ??= dsTexture;

                AttachmentSamples[count - 1] = (uint)dsTexture.Info.Samples;
                AttachmentFormats[count - 1] = dsTexture.VkFormat;

                width = Math.Min(width, (uint)dsTexture.Width);
                height = Math.Min(height, (uint)dsTexture.Height);
                layers = Math.Min(layers, (uint)dsTexture.Layers);

                HasDepthStencil = true;
            }

            if (count == 0)
            {
                width = height = layers = 1;
            }

            Width = width;
            Height = height;
            Layers = layers;

            AttachmentsCount = count;
            _totalCount = colors.Length;

            return this;
        }

        public Auto<DisposableImageView> GetAttachment(int index)
        {
            if ((uint)index >= AttachmentsCount)
            {
                return null;
            }

            return _attachments[index];
        }

        public Auto<DisposableImageView> GetDepthStencilAttachment()
        {
            if (!HasDepthStencil)
            {
                return null;
            }

            return _attachments[AttachmentsCount - 1];
        }

        public ComponentType GetAttachmentComponentType(int index)
        {
            if (_colors != null && (uint)index < ColorAttachmentsCount)
            {
                Format format = _colors[index].Info.Format;

                if (format.IsSignedInt)
                {
                    return ComponentType.SignedInteger;
                }

                if (format.IsUnsignedInt)
                {
                    return ComponentType.UnsignedInteger;
                }
            }

            return ComponentType.Float;
        }

        public ImageAspectFlags GetDepthStencilAspectFlags()
        {
            if (_depthStencil == null)
            {
                return ImageAspectFlags.None;
            }

            return _depthStencil.Info.Format.ConvertAspectFlags();
        }

        public bool IsValidColorAttachment(int bindIndex)
        {
            return (uint)bindIndex < Constants.MaxRenderTargets && (_validColorAttachments & (1u << bindIndex)) != 0;
        }

        private static bool IsValidTextureView(ITexture texture)
        {
            return texture is TextureView { Valid: true };
        }

        public ClearRect GetClearRect(Rectangle<int> scissor, int layer, int layerCount)
        {
            int x = scissor.X;
            int y = scissor.Y;
            int width = Math.Min((int)Width - scissor.X, scissor.Width);
            int height = Math.Min((int)Height - scissor.Y, scissor.Height);

            return new ClearRect(new Rect2D(new Offset2D(x, y), new Extent2D((uint)width, (uint)height)), (uint)layer, (uint)layerCount);
        }

        public unsafe Auto<DisposableFramebuffer> Create(Vk api, CommandBufferScoped cbs, Auto<DisposableRenderPass> renderPass)
        {
            ImageView* attachments = stackalloc ImageView[AttachmentsCount];

            for (int i = 0; i < AttachmentsCount; i++)
            {
                attachments[i] = _attachments[i].Get(cbs).Value;
            }

            FramebufferCreateInfo framebufferCreateInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = renderPass.Get(cbs).Value,
                AttachmentCount = (uint)AttachmentsCount,
                PAttachments = attachments,
                Width = Width,
                Height = Height,
                Layers = Layers,
            };

            api.CreateFramebuffer(_device, in framebufferCreateInfo, null, out Framebuffer framebuffer).ThrowOnError();
            return new Auto<DisposableFramebuffer>(new DisposableFramebuffer(api, _device, framebuffer), null, _attachments[..AttachmentsCount]);
        }

        public TextureView[] GetAttachmentViews()
        {
            TextureView[] result = new TextureView[AttachmentsCount];
            _colors?.AsSpan(..ColorAttachmentsCount).CopyTo(result.AsSpan());

            if (_depthStencil != null)
            {
                result[^1] = _depthStencil;
            }

            return result;
        }

        public RenderPassCacheKey GetRenderPassCacheKey()
        {
            return new RenderPassCacheKey(_depthStencil, _colorsCanonical);
        }

        public void InsertLoadOpBarriers(VulkanRenderer gd, CommandBufferScoped cbs)
        {
            if (_colors != null)
            {
                int count = ColorAttachmentsCount;
                
                for (int i = 0; i < count; i++)
                {
                    TextureView color = _colors[i];
                    // If Clear or DontCare were used, this would need to be write bit.
                    color.Storage?.QueueLoadOpBarrier(cbs, false);
                }
            }

            _depthStencil?.Storage?.QueueLoadOpBarrier(cbs, true);

            gd.Barriers.Flush(cbs, false, null, null);
        }

        public void AddStoreOpUsage()
        {
            if (_colors != null)
            {
                int count = ColorAttachmentsCount;
                
                for (int i = 0; i < count; i++)
                {
                    TextureView color = _colors[i];
                    color.Storage?.AddStoreOpUsage(false);
                }
            }

            _depthStencil?.Storage?.AddStoreOpUsage(true);
        }

        public void ClearBindings()
        {
            _depthStencil?.Storage.ClearBindings();

            for (int i = 0; i < _totalCount; i++)
            {
                _colorsCanonical[i]?.Storage.ClearBindings();
            }
        }

        public void AddBindings()
        {
            _depthStencil?.Storage.AddBinding(_depthStencil);

            for (int i = 0; i < _totalCount; i++)
            {
                TextureView color = _colorsCanonical[i];
                color?.Storage.AddBinding(color);
            }
        }

        public (RenderPassHolder rpHolder, Auto<DisposableFramebuffer> framebuffer) GetPassAndFramebuffer(
            VulkanRenderer gd,
            Device device,
            CommandBufferScoped cbs)
        {
            return _baseAttachment.GetPassAndFramebuffer(gd, device, cbs, this);
        }

        public TextureView GetColorView(int index)
        {
            return _colorsCanonical[index];
        }

        public TextureView GetDepthStencilView()
        {
            return _depthStencil;
        }
    }
}
