namespace Ryujinx.Graphics.Gpu
{
    /// <summary>
    /// General GPU and graphics configuration.
    /// </summary>
    public static class GraphicsConfig
    {
        /// <summary>
        /// Resolution scale.
        /// </summary>
        public static float ResScale { get; set; } = 1f;

        /// <summary>
        /// Max Anisotropy. Values range from 0 - 16. Set to -1 to let the game decide.
        /// </summary>
        public static float MaxAnisotropy { get; set; } = -1;

        /// <summary>
        /// Base directory used to write shader code dumps.
        /// Set to null to disable code dumping.
        /// </summary>
        public static string ShadersDumpPath { get; set; }

        /// <summary>
        /// Fast GPU time calculates the internal GPU time ticks as if the GPU was capable of
        /// processing commands almost instantly, instead of using the host timer.
        /// This can avoid lower resolution on some games when GPU performance is poor.
        /// </summary>
        public static bool FastGpuTime { get; set; } = true;

        /// <summary>
        /// Enables or disables fast 2d engine texture copies entirely on CPU when possible.
        /// Reduces stuttering and # of textures in games that copy textures around for streaming,
        /// as textures will not need to be created for the copy, and the data does not need to be
        /// flushed from GPU.
        /// </summary>
        public static bool Fast2DCopy { get; set; } = true;

        /// <summary>
        /// Enables or disables the Just-in-Time compiler for GPU Macro code.
        /// </summary>
        public static bool EnableMacroJit { get; set; } = true;

        /// <summary>
        /// Enables or disables high-level emulation of common GPU Macro code.
        /// </summary>
        public static bool EnableMacroHLE { get; set; } = true;

        /// <summary>
        /// Title id of the current running game.
        /// Used by the shader cache.
        /// </summary>
        public static string TitleId { get; set; }

        /// <summary>
        /// Enables or disables the shader cache.
        /// </summary>
        public static bool EnableShaderCache { get; set; }

        /// <summary>
        /// Enables or disables shader SPIR-V compilation.
        /// </summary>
        public static bool EnableSpirvCompilationOnVulkan { get; set; } = true;

        /// <summary>
        /// Enables or disables recompression of compressed textures that are not natively supported by the host.
        /// </summary>
        public static bool EnableTextureRecompression { get; set; } = false;

        /// <summary>
        /// Enables or disables color space passthrough, if available.
        /// </summary>
        public static bool EnableColorSpacePassthrough { get; set; } = false;
    }
}
