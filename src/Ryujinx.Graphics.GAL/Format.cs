namespace Ryujinx.Graphics.GAL
{
    public enum Format
    {
        R8Unorm,
        R8Snorm,
        R8Uint,
        R8Sint,
        R16Float,
        R16Unorm,
        R16Snorm,
        R16Uint,
        R16Sint,
        R32Float,
        R32Uint,
        R32Sint,
        R8G8Unorm,
        R8G8Snorm,
        R8G8Uint,
        R8G8Sint,
        R16G16Float,
        R16G16Unorm,
        R16G16Snorm,
        R16G16Uint,
        R16G16Sint,
        R32G32Float,
        R32G32Uint,
        R32G32Sint,
        R8G8B8Unorm,
        R8G8B8Snorm,
        R8G8B8Uint,
        R8G8B8Sint,
        R16G16B16Float,
        R16G16B16Unorm,
        R16G16B16Snorm,
        R16G16B16Uint,
        R16G16B16Sint,
        R32G32B32Float,
        R32G32B32Uint,
        R32G32B32Sint,
        R8G8B8A8Unorm,
        R8G8B8A8Snorm,
        R8G8B8A8Uint,
        R8G8B8A8Sint,
        R16G16B16A16Float,
        R16G16B16A16Unorm,
        R16G16B16A16Snorm,
        R16G16B16A16Uint,
        R16G16B16A16Sint,
        R32G32B32A32Float,
        R32G32B32A32Uint,
        R32G32B32A32Sint,
        S8Uint,
        D16Unorm,
        S8UintD24Unorm,
        D32Float,
        D24UnormS8Uint,
        D32FloatS8Uint,
        R8G8B8A8Srgb,
        R4G4Unorm,
        R4G4B4A4Unorm,
        R5G5B5X1Unorm,
        R5G5B5A1Unorm,
        R5G6B5Unorm,
        R10G10B10A2Unorm,
        R10G10B10A2Uint,
        R11G11B10Float,
        R9G9B9E5Float,
        Bc1RgbaUnorm,
        Bc2Unorm,
        Bc3Unorm,
        Bc1RgbaSrgb,
        Bc2Srgb,
        Bc3Srgb,
        Bc4Unorm,
        Bc4Snorm,
        Bc5Unorm,
        Bc5Snorm,
        Bc7Unorm,
        Bc7Srgb,
        Bc6HSfloat,
        Bc6HUfloat,
        Etc2RgbUnorm,
        Etc2RgbaUnorm,
        Etc2RgbPtaUnorm,
        Etc2RgbSrgb,
        Etc2RgbaSrgb,
        Etc2RgbPtaSrgb,
        R8Uscaled,
        R8Sscaled,
        R16Uscaled,
        R16Sscaled,
        R32Uscaled,
        R32Sscaled,
        R8G8Uscaled,
        R8G8Sscaled,
        R16G16Uscaled,
        R16G16Sscaled,
        R32G32Uscaled,
        R32G32Sscaled,
        R8G8B8Uscaled,
        R8G8B8Sscaled,
        R16G16B16Uscaled,
        R16G16B16Sscaled,
        R32G32B32Uscaled,
        R32G32B32Sscaled,
        R8G8B8A8Uscaled,
        R8G8B8A8Sscaled,
        R16G16B16A16Uscaled,
        R16G16B16A16Sscaled,
        R32G32B32A32Uscaled,
        R32G32B32A32Sscaled,
        R10G10B10A2Snorm,
        R10G10B10A2Sint,
        R10G10B10A2Uscaled,
        R10G10B10A2Sscaled,
        Astc4x4Unorm,
        Astc5x4Unorm,
        Astc5x5Unorm,
        Astc6x5Unorm,
        Astc6x6Unorm,
        Astc8x5Unorm,
        Astc8x6Unorm,
        Astc8x8Unorm,
        Astc10x5Unorm,
        Astc10x6Unorm,
        Astc10x8Unorm,
        Astc10x10Unorm,
        Astc12x10Unorm,
        Astc12x12Unorm,
        Astc4x4Srgb,
        Astc5x4Srgb,
        Astc5x5Srgb,
        Astc6x5Srgb,
        Astc6x6Srgb,
        Astc8x5Srgb,
        Astc8x6Srgb,
        Astc8x8Srgb,
        Astc10x5Srgb,
        Astc10x6Srgb,
        Astc10x8Srgb,
        Astc10x10Srgb,
        Astc12x10Srgb,
        Astc12x12Srgb,
        B5G6R5Unorm,
        B5G5R5A1Unorm,
        A1B5G5R5Unorm,
        B8G8R8A8Unorm,
        B8G8R8A8Srgb,
        B10G10R10A2Unorm,
        X8UintD24Unorm,
        A8B8G8R8Uint,
    }

    public static class FormatExtensions
    {
        /// <summary>
        /// The largest scalar size for a buffer format.
        /// </summary>
        public const int MaxBufferFormatScalarSize = 4;

        extension(Format fmt)
        {
            /// <summary>
            /// Gets the byte size for a single component of this format, or its packed size.
            /// </summary>
            public int ScalarSize => fmt switch
            {
                Format.R8Unorm or Format.R8Snorm or Format.R8Uint or Format.R8Sint or Format.R8G8Unorm
                    or Format.R8G8Snorm or Format.R8G8Uint or Format.R8G8Sint or Format.R8G8B8Unorm
                    or Format.R8G8B8Snorm or Format.R8G8B8Uint or Format.R8G8B8Sint or Format.R8G8B8A8Unorm
                    or Format.R8G8B8A8Snorm or Format.R8G8B8A8Uint or Format.R8G8B8A8Sint or Format.R8G8B8A8Srgb
                    or Format.R4G4Unorm or Format.R8Uscaled or Format.R8Sscaled or Format.R8G8Uscaled
                    or Format.R8G8Sscaled or Format.R8G8B8Uscaled or Format.R8G8B8Sscaled or Format.R8G8B8A8Uscaled
                    or Format.R8G8B8A8Sscaled or Format.B8G8R8A8Unorm or Format.B8G8R8A8Srgb => 1,
                Format.R16Float or Format.R16Unorm or Format.R16Snorm or Format.R16Uint or Format.R16Sint
                    or Format.R16G16Float or Format.R16G16Unorm or Format.R16G16Snorm or Format.R16G16Uint
                    or Format.R16G16Sint or Format.R16G16B16Float or Format.R16G16B16Unorm or Format.R16G16B16Snorm
                    or Format.R16G16B16Uint or Format.R16G16B16Sint or Format.R16G16B16A16Float
                    or Format.R16G16B16A16Unorm or Format.R16G16B16A16Snorm or Format.R16G16B16A16Uint
                    or Format.R16G16B16A16Sint or Format.R4G4B4A4Unorm or Format.R5G5B5X1Unorm or Format.R5G5B5A1Unorm
                    or Format.R5G6B5Unorm or Format.R16Uscaled or Format.R16Sscaled or Format.R16G16Uscaled
                    or Format.R16G16Sscaled or Format.R16G16B16Uscaled or Format.R16G16B16Sscaled
                    or Format.R16G16B16A16Uscaled or Format.R16G16B16A16Sscaled or Format.B5G6R5Unorm
                    or Format.B5G5R5A1Unorm or Format.A1B5G5R5Unorm => 2,
                Format.R32Float or Format.R32Uint or Format.R32Sint or Format.R32G32Float or Format.R32G32Uint
                    or Format.R32G32Sint or Format.R32G32B32Float or Format.R32G32B32Uint or Format.R32G32B32Sint
                    or Format.R32G32B32A32Float or Format.R32G32B32A32Uint or Format.R32G32B32A32Sint
                    or Format.R10G10B10A2Unorm or Format.R10G10B10A2Uint or Format.R11G11B10Float
                    or Format.R9G9B9E5Float or Format.R32Uscaled or Format.R32Sscaled or Format.R32G32Uscaled
                    or Format.R32G32Sscaled or Format.R32G32B32Uscaled or Format.R32G32B32Sscaled
                    or Format.R32G32B32A32Uscaled or Format.R32G32B32A32Sscaled or Format.R10G10B10A2Snorm
                    or Format.R10G10B10A2Sint or Format.R10G10B10A2Uscaled or Format.R10G10B10A2Sscaled
                    or Format.B10G10R10A2Unorm => 4,
                Format.S8Uint => 1,
                Format.D16Unorm => 2,
                Format.S8UintD24Unorm or Format.X8UintD24Unorm or Format.D32Float or Format.D24UnormS8Uint => 4,
                Format.D32FloatS8Uint => 8,
                Format.Bc1RgbaUnorm or Format.Bc1RgbaSrgb => 8,
                Format.Bc2Unorm or Format.Bc3Unorm or Format.Bc2Srgb or Format.Bc3Srgb or Format.Bc4Unorm
                    or Format.Bc4Snorm or Format.Bc5Unorm or Format.Bc5Snorm or Format.Bc7Unorm or Format.Bc7Srgb
                    or Format.Bc6HSfloat or Format.Bc6HUfloat => 16,
                Format.Etc2RgbUnorm or Format.Etc2RgbPtaUnorm or Format.Etc2RgbSrgb or Format.Etc2RgbPtaSrgb => 8,
                Format.Etc2RgbaUnorm or Format.Etc2RgbaSrgb => 16,
                Format.Astc4x4Unorm or Format.Astc5x4Unorm or Format.Astc5x5Unorm or Format.Astc6x5Unorm
                    or Format.Astc6x6Unorm or Format.Astc8x5Unorm or Format.Astc8x6Unorm or Format.Astc8x8Unorm
                    or Format.Astc10x5Unorm or Format.Astc10x6Unorm or Format.Astc10x8Unorm or Format.Astc10x10Unorm
                    or Format.Astc12x10Unorm or Format.Astc12x12Unorm or Format.Astc4x4Srgb or Format.Astc5x4Srgb
                    or Format.Astc5x5Srgb or Format.Astc6x5Srgb or Format.Astc6x6Srgb or Format.Astc8x5Srgb
                    or Format.Astc8x6Srgb or Format.Astc8x8Srgb or Format.Astc10x5Srgb or Format.Astc10x6Srgb
                    or Format.Astc10x8Srgb or Format.Astc10x10Srgb or Format.Astc12x10Srgb
                    or Format.Astc12x12Srgb => 16,
                _ => 1
            };

            /// <summary>
            /// Checks if the texture format is a depth or depth-stencil format.
            /// </summary>
            public bool HasDepth => fmt is
                Format.D16Unorm or Format.D24UnormS8Uint or Format.S8UintD24Unorm or Format.X8UintD24Unorm
                or Format.D32Float or Format.D32FloatS8Uint;

            /// <summary>
            /// Checks if the texture format is a stencil or depth-stencil format.
            /// </summary>
            public bool HasStencil => fmt is
                Format.D24UnormS8Uint or Format.S8UintD24Unorm or Format.D32FloatS8Uint or Format.S8Uint;

            /// <summary>
            /// Checks if the texture format is valid to use as image format.
            /// </summary>
            public bool IsImageCompatible => fmt is
                Format.R8Unorm or Format.R8Snorm or Format.R8Uint or Format.R8Sint or Format.R16Float or Format.R16Unorm
                or Format.R16Snorm or Format.R16Uint or Format.R16Sint or Format.R32Float or Format.R32Uint
                or Format.R32Sint or Format.R8G8Unorm or Format.R8G8Snorm or Format.R8G8Uint or Format.R8G8Sint
                or Format.R16G16Float or Format.R16G16Unorm or Format.R16G16Snorm or Format.R16G16Uint
                or Format.R16G16Sint or Format.R32G32Float or Format.R32G32Uint or Format.R32G32Sint
                or Format.R8G8B8A8Unorm or Format.R8G8B8A8Snorm or Format.R8G8B8A8Uint or Format.R8G8B8A8Sint
                or Format.R16G16B16A16Float or Format.R16G16B16A16Unorm or Format.R16G16B16A16Snorm
                or Format.R16G16B16A16Uint or Format.R16G16B16A16Sint or Format.R32G32B32A32Float
                or Format.R32G32B32A32Uint or Format.R32G32B32A32Sint or Format.R10G10B10A2Unorm
                or Format.R10G10B10A2Uint or Format.R11G11B10Float or Format.B8G8R8A8Unorm;

            /// <summary>
            /// Checks if the texture format is valid to use as render target color format.
            /// </summary>
            public bool IsRtColorCompatible => fmt is
                Format.R32G32B32A32Float or Format.R32G32B32A32Sint or Format.R32G32B32A32Uint
                or Format.R16G16B16A16Unorm or Format.R16G16B16A16Snorm or Format.R16G16B16A16Sint
                or Format.R16G16B16A16Uint or Format.R16G16B16A16Float or Format.R32G32Float or Format.R32G32Sint
                or Format.R32G32Uint or Format.B8G8R8A8Unorm or Format.B8G8R8A8Srgb or Format.B10G10R10A2Unorm
                or Format.R10G10B10A2Unorm or Format.R10G10B10A2Uint or Format.R8G8B8A8Unorm or Format.R8G8B8A8Srgb
                or Format.R8G8B8A8Snorm or Format.R8G8B8A8Sint or Format.R8G8B8A8Uint or Format.R16G16Unorm
                or Format.R16G16Snorm or Format.R16G16Sint or Format.R16G16Uint or Format.R16G16Float
                or Format.R11G11B10Float or Format.R32Sint or Format.R32Uint or Format.R32Float
                or Format.B5G6R5Unorm or Format.B5G5R5A1Unorm or Format.R8G8Unorm or Format.R8G8Snorm
                or Format.R8G8Sint or Format.R8G8Uint or Format.R16Unorm or Format.R16Snorm or Format.R16Sint
                or Format.R16Uint or Format.R16Float or Format.R8Unorm or Format.R8Snorm or Format.R8Sint
                or Format.R8Uint;

            /// <summary>
            /// Checks if the texture format is 16 bit packed.
            /// </summary>
            public bool Is16BitPacked => fmt is
                Format.B5G6R5Unorm or Format.B5G5R5A1Unorm or Format.R5G5B5X1Unorm or Format.R5G5B5A1Unorm
                or Format.R5G6B5Unorm or Format.R4G4B4A4Unorm;

            /// <summary>
            /// Checks if the texture format is an ETC2 format.
            /// </summary>
            public bool IsEtc2 => fmt is
                Format.Etc2RgbaSrgb or Format.Etc2RgbaUnorm or Format.Etc2RgbPtaSrgb
                or Format.Etc2RgbPtaUnorm or Format.Etc2RgbSrgb or Format.Etc2RgbUnorm;

            /// <summary>
            /// Checks if the texture format is a BGR format.
            /// </summary>
            public bool IsBgr => fmt is
                Format.B5G6R5Unorm or Format.B5G5R5A1Unorm or Format.B8G8R8A8Unorm or Format.B8G8R8A8Srgb
                or Format.B10G10R10A2Unorm;

            /// <summary>
            /// Checks if the texture format is a depth, stencil or depth-stencil format.
            /// </summary>
            public bool IsDepthOrStencil => fmt is
                Format.D16Unorm or Format.D24UnormS8Uint or Format.S8UintD24Unorm or Format.X8UintD24Unorm
                or Format.D32Float or Format.D32FloatS8Uint or Format.S8Uint;

            /// <summary>
            /// Checks if the texture format is a float or sRGB color format.
            /// </summary>
            /// <remarks>
            /// Does not include normalized, compressed or depth formats.
            /// Float and sRGB formats do not participate in logical operations.
            /// </remarks>
            public bool IsFloatOrSrgb => fmt is
                Format.R8G8B8A8Srgb or Format.B8G8R8A8Srgb or Format.R16Float or Format.R16G16Float
                or Format.R16G16B16Float or Format.R16G16B16A16Float or Format.R32Float or Format.R32G32Float
                or Format.R32G32B32Float or Format.R32G32B32A32Float or Format.R11G11B10Float
                or Format.R9G9B9E5Float;
            
            /// <summary>
            /// Checks if the texture format is an ASTC Unorm format.
            /// </summary>
            public bool IsAstcUnorm => fmt is
                Format.Astc4x4Unorm or Format.Astc5x4Unorm or Format.Astc5x5Unorm or Format.Astc6x5Unorm
                or Format.Astc6x6Unorm or Format.Astc8x5Unorm or Format.Astc8x6Unorm or Format.Astc8x8Unorm
                or Format.Astc10x5Unorm or Format.Astc10x6Unorm or Format.Astc10x8Unorm or Format.Astc10x10Unorm
                or Format.Astc12x10Unorm or Format.Astc12x12Unorm;

            /// <summary>
            /// Checks if the texture format is an ASTC SRGB format.
            /// </summary>
            public bool IsAstcSrgb => fmt is
                Format.Astc4x4Srgb or Format.Astc5x4Srgb or Format.Astc5x5Srgb or Format.Astc6x5Srgb
                or Format.Astc6x6Srgb or Format.Astc8x5Srgb or Format.Astc8x6Srgb or Format.Astc8x8Srgb
                or Format.Astc10x5Srgb or Format.Astc10x6Srgb or Format.Astc10x8Srgb or Format.Astc10x10Srgb
                or Format.Astc12x10Srgb or Format.Astc12x12Srgb;

            /// <summary>
            /// Checks if the texture format is an ASTC format.
            /// </summary>
            public bool IsAstc => fmt.IsAstcUnorm || fmt.IsAstcSrgb;

            /// <summary>
            /// Checks if the texture format is an unsigned integer color format.
            /// </summary>
            public bool IsUnsignedInt => fmt is
                Format.R8Uint or Format.R16Uint or Format.R32Uint or Format.R8G8Uint or Format.R16G16Uint
                or Format.R32G32Uint or Format.R8G8B8Uint or Format.R16G16B16Uint or Format.R32G32B32Uint
                or Format.R8G8B8A8Uint or Format.R16G16B16A16Uint or Format.R32G32B32A32Uint
                or Format.R10G10B10A2Uint;

            /// <summary>
            /// Checks if the texture format is a signed integer color format.
            /// </summary>
            public bool IsSignedInt => fmt is
                Format.R8Sint or Format.R16Sint or Format.R32Sint or Format.R8G8Sint or Format.R16G16Sint
                or Format.R32G32Sint or Format.R8G8B8Sint or Format.R16G16B16Sint or Format.R32G32B32Sint
                or Format.R8G8B8A8Sint or Format.R16G16B16A16Sint or Format.R32G32B32A32Sint
                or Format.R10G10B10A2Sint;

            /// <summary>
            /// Checks if the texture format is an integer color format.
            /// </summary>
            public bool IsInt => fmt.IsUnsignedInt || fmt.IsSignedInt;
        }
    }
}
