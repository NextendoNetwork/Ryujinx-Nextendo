using Ryujinx.Common.Memory;
using Ryujinx.Graphics.GAL;
using Silk.NET.Vulkan;
using System;
using VkFormat = Silk.NET.Vulkan.Format;

namespace Ryujinx.Graphics.Vulkan
{
    class TextureBuffer : ITexture
    {
        private readonly VulkanRenderer _gd;

        private BufferHandle _bufferHandle;
        private int _offset;
        private int _size;
        private Auto<DisposableBufferView> _bufferView;

        private BufferHolder _bufferHolder;

        public int Width { get; }
        public int Height { get; }

        public VkFormat VkFormat { get; }

        /// <summary>
        /// Incremented every time the view is released, so arrays can tell which of their views may be stale.
        /// </summary>
        public int ViewGeneration { get; private set; }

        public TextureBuffer(VulkanRenderer gd, TextureCreateInfo info)
        {
            _gd = gd;
            Width = info.Width;
            Height = info.Height;
            VkFormat = FormatTable.GetFormat(info.Format);

            gd.Textures.Add(this);
        }

        public void CopyTo(ITexture destination, int firstLayer, int firstLevel)
        {
            throw new NotSupportedException();
        }

        public void CopyTo(ITexture destination, int srcLayer, int dstLayer, int srcLevel, int dstLevel)
        {
            throw new NotSupportedException();
        }

        public void CopyTo(ITexture destination, Extents2D srcRegion, Extents2D dstRegion, bool linearFilter)
        {
            throw new NotSupportedException();
        }

        public ITexture CreateView(TextureCreateInfo info, int firstLayer, int firstLevel)
        {
            throw new NotSupportedException();
        }

        public PinnedSpan<byte> GetData()
        {
            return _gd.GetBufferData(_bufferHandle, _offset, _size);
        }

        public PinnedSpan<byte> GetData(int layer, int level)
        {
            return GetData();
        }

        public void CopyTo(BufferRange range, int layer, int level, int stride)
        {
            throw new NotImplementedException();
        }

        public void Release()
        {
            if (_gd.Textures.Remove(this))
            {
                ReleaseImpl();
            }
        }

        private void ReleaseImpl()
        {
            _bufferView?.Dispose();
            _bufferView = null;
            ViewGeneration++;
            _gd.BufferViewReleaseCount++;
        }

        /// <inheritdoc/>
        public void SetData(MemoryOwner<byte> data)
        {
            _gd.SetBufferData(_bufferHandle, _offset, data.Span);
            data.Dispose();
        }

        /// <inheritdoc/>
        public void SetData(MemoryOwner<byte> data, int layer, int level)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public void SetData(MemoryOwner<byte> data, int layer, int level, Rectangle<int> region)
        {
            throw new NotSupportedException();
        }

        public void SetStorage(BufferRange buffer)
        {
            // Handles are reused after a buffer is deleted, so compare the buffer itself as well. Releasing the view
            // whenever any buffer is created is too costly, as every array holding the view must then be rebuilt.
            _gd.BufferManager.TryGetBuffer(buffer.Handle, out BufferHolder holder);

            if (_bufferHandle == buffer.Handle &&
                _offset == buffer.Offset &&
                _size == buffer.Size &&
                _bufferHolder == holder)
            {
                return;
            }

            _bufferHandle = buffer.Handle;
            _offset = buffer.Offset;
            _size = buffer.Size;
            _bufferHolder = holder;

            ReleaseImpl();
        }

        public BufferView GetBufferView(CommandBufferScoped cbs, bool write)
        {
            _bufferView ??= _gd.BufferManager.CreateView(_bufferHandle, VkFormat, _offset, _size, ReleaseImpl);

            return _bufferView?.Get(cbs, _offset, _size, write).Value ?? default;
        }
    }
}
