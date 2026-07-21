// ReSharper disable UnusedMember.Global

namespace Ryujinx.Graphics.RenderDocApi
{
    public enum CaptureOption
    {
        /// <summary>
        /// specifies whether the application is allowed to enable vsync. Default is on.
        /// </summary>
        AllowVsync = 0,
        /// <summary>
        /// specifies whether the application is allowed to enter exclusive fullscreen. Default is on.
        /// </summary>
        AllowFullscreen = 1,
        /// <summary>
        /// specifies whether (where possible) API-specific debugging is enabled. Default is off.
        /// </summary>
        ApiValidation = 2,
        /// <summary>
        /// specifies whether each API call should save a callstack. Default is off.
        /// </summary>
        CaptureCallstacks = 3,
        /// <summary>
        /// specifies whether, if <see cref="CaptureCallstacks"/> is enabled, callstacks are only saved on actions. Default is off.
        /// </summary>
        CaptureCallstacksOnlyDraws = 4,
        /// <summary>
        /// specifies a delay in seconds after launching a process to pause, to allow debuggers to attach. <br/>
        /// This will only apply to child processes since the delay happens at process startup. Default is 0.
        /// </summary>
        DelayForDebugger = 5,
        /// <summary>
        /// specifies whether any mapped memory updates should be bounds-checked for overruns,
        /// and uninitialised buffers are initialized to <code>0xDDDDDDDD</code> to catch use of uninitialised data.
        /// Only supported on D3D11 and OpenGL. Default is off.
        /// </summary>
        /// <remarks>
        /// This option is only valid for OpenGL and D3D11. Explicit APIs such as D3D12 and Vulkan do
        /// not do the same kind of interception &amp; checking, and undefined contents are really undefined.
        /// </remarks>
        VerifyBufferAccess = 6,
        /// <summary>
        /// Hooks any system API calls that create child processes, and injects
        /// RenderDoc into them recursively with the same options.
        /// </summary>
        HookIntoChildren = 7,
        /// <summary>
        /// specifies whether all live resources at the time of capture should be included in the capture,
        /// even if they are not referenced by the frame. Default is off.
        /// </summary>
        RefAllSources = 8,
        /// <summary>
        /// By default, RenderDoc skips saving initial states for resources where the
        /// previous contents don't appear to be used, assuming that writes before
        /// reads indicate previous contents aren't used.
        /// </summary>
        /// <remarks>
        /// **NOTE**: As of RenderDoc v1.1 this option has been deprecated. Setting or
        /// getting it will be ignored, to allow compatibility with older versions.
        /// In v1.1 the option acts as if it's always enabled.
        /// </remarks>
        SaveAllInitials = 9,
        /// <summary>
        /// In APIs that allow for the recording of command lists to be replayed later,
        /// RenderDoc may choose to not capture command lists before a frame capture is
        /// triggered, to reduce overheads. This means any command lists recorded once
        /// and replayed many times will not be available and may cause a failure to
        /// capture.
        /// </summary>
        /// <remarks>
        /// NOTE: This is only true for APIs where multithreading is difficult or
        /// discouraged. Newer APIs like Vulkan and D3D12 will ignore this option
        /// and always capture all command lists since the API is heavily oriented
        /// around it and the overheads have been reduced by API design.
        /// </remarks>
        CaptureAllCmdLists = 10,
        /// <summary>
        /// Mute API debugging output when the <see cref="ApiValidation"/> option is enabled.
        /// </summary>
        DebugOutputMute = 11,
        /// <summary>
        /// Allow vendor extensions to be used even when they may be
        /// incompatible with RenderDoc and cause corrupted replays or crashes.
        /// </summary>
        AllowUnsupportedVendorExtensions = 12,
        /// <summary>
        /// Define a soft memory limit which some APIs may aim to keep overhead under where
        /// possible. Anything above this limit will where possible be saved directly to disk during
        /// capture.<br/>
        /// This will cause increased disk space use (which may cause a capture to fail if disk space is
        /// exhausted) as well as slower capture times.
        /// <br/><br/>
        /// Not all memory allocations may be deferred like this so it is not a guarantee of a memory
        /// limit.
        /// <br/><br/>
        /// Units are in MBs, suggested values would range from 200MB to 1000MB.
        /// </summary>
        SoftMemoryLimit = 13,
    }
}
