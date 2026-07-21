using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Ryujinx.Graphics.RenderDocApi
{
    public static unsafe partial class RenderDoc
    {
        /// <summary>
        /// True if the API is available.
        /// </summary>
        public static bool IsAvailable => Api != null;

        /// <summary>
        /// Set the minimum version of the API you require.
        /// </summary>
        /// <remarks>Set this before you do anything else with the RenderDoc API, including <see cref="IsAvailable"/>.</remarks>
        public static RenderDocVersion MinimumRequired { get; set; } = RenderDocVersion.Version_1_0_0;

        /// <summary>
        /// Set to true to assert versions.
        /// </summary>
        public static bool AssertVersionEnabled { get; set; } = true;

        /// <summary>
        /// Version of the API available.
        /// </summary>
        [MemberNotNullWhen(true, nameof(IsAvailable))]
        public static Version? Version
        {
            get
            {
                if (!IsAvailable)
                    return null;

                int major, minor, build;
                Api->GetApiVersion(&major, &minor, &build);
                return new Version(major, minor, build);
            }
        }

        /// <summary>
        /// The current mask which determines what sections of the overlay render on each window.
        /// </summary>
        [RenderDocApiVersion(1, 0)]
        public static OverlayBits OverlayBits
        {
            get => Api->GetOverlayBits();
            set
            {
                Api->MaskOverlayBits(~value, value);
            }
        }

        /// <summary>
        /// The template for new captures.<br/>
        /// The template can either be a relative or absolute path, which determines where captures will be saved and how they will be named.
        /// If the path template is 'my_captures/example', then captures saved will be e.g.
        /// 'my_captures/example_frame123.rdc' and 'my_captures/example_frame456.rdc'.<br/>
        /// Relative paths will be saved relative to the process’s current working directory.<br/>
        /// </summary>
        /// <remarks>The default template is in a folder controlled by the UI - initially the system temporary folder, and the filename is the executable’s filename.</remarks>
        [RenderDocApiVersion(1, 0)]
        public static string CaptureFilePathTemplate
        {
            get
            {
                byte* ptr = Api->GetCaptureFilePathTemplate();
                return Marshal.PtrToStringUTF8((nint)ptr)!;
            }
            set
            {
                fixed (byte* ptr = value.ToNullTerminatedByteArray())
                {
                    Api->SetCaptureFilePathTemplate(ptr);
                }
            }
        }

        /// <summary>
        /// The amount of frame captures that have been made.
        /// </summary>
        [RenderDocApiVersion(1, 0)]
        public static int CaptureCount => Api->GetNumCaptures();

        /// <summary>
        /// Checks if the RenderDoc UI is currently connected to this process.
        /// </summary>
        [RenderDocApiVersion(1, 0)]
        public static bool IsTargetControlConnected => Api is not null && Api->IsTargetControlConnected() != 0;

        /// <summary>
        /// Checks if the current frame is capturing.
        /// </summary>
        [RenderDocApiVersion(1, 0)]
        public static bool IsFrameCapturing => Api is not null && Api->IsFrameCapturing() != 0;

        /// <summary>
        ///     Set one of the options for tweaking some behaviors of capturing.
        /// </summary>
        /// <param name="option">specifies which capture option should be set.</param>
        /// <param name="integer">the unsigned integer value to set for the option.</param>
        /// <remarks>Note that each option only takes effect from after it is set - so it is advised to set these options as early as possible, ideally before any graphics API has been initialized.</remarks>
        /// <returns>
        /// true, if the <paramref name="option"/> is valid, and the value set on the option is within valid ranges.<br/>
        /// false, if the option is not a <see cref="CaptureOption"/>, or the value is not valid for the option.
        /// </returns>
        [RenderDocApiVersion(1, 0)]
        public static bool SetCaptureOption(CaptureOption option, uint integer)
        {
            return Api is not null && Api->SetCaptureOptionU32(option, integer) != 0;
        }

        /// <summary>
        /// Set one of the options for tweaking some behaviors of capturing.
        /// </summary>
        /// <param name="option">specifies which capture option should be set.</param>
        /// <param name="boolean">the value to set for the option, converted to a 0 or 1 before setting.</param>
        /// <remarks>Note that each option only takes effect from after it is set - so it is advised to set these options as early as possible, ideally before any graphics API has been initialized.</remarks>
        /// <returns>
        /// true, if the <paramref name="option"/> is valid, and the value set on the option is within valid ranges.<br/>
        /// false, if the option is not a <see cref="CaptureOption"/>, or the value is not valid for the option.
        /// </returns>
        [RenderDocApiVersion(1, 0)]
        public static bool SetCaptureOption(CaptureOption option, bool boolean)
            => SetCaptureOption(option, boolean ? 1 : 0);

        /// <summary>
        /// Set one of the options for tweaking some behaviors of capturing.
        /// </summary>
        /// <param name="option">specifies which capture option should be set.</param>
        /// <param name="single">the floating point value to set for the option.</param>
        /// <remarks>Note that each option only takes effect from after it is set - so it is advised to set these options as early as possible, ideally before any graphics API has been initialized.</remarks>
        /// <returns>
        /// true, if the <paramref name="option"/> is valid, and the value set on the option is within valid ranges.<br/>
        /// false, if the option is not a <see cref="CaptureOption"/>, or the value is not valid for the option.
        /// </returns>
        [RenderDocApiVersion(1, 0)]
        public static bool SetCaptureOption(CaptureOption option, float single)
        {
            return Api is not null && Api->SetCaptureOptionF32(option, single) != 0;
        }

        /// <summary>
        /// Gets the current value of one of the different options in <see cref="CaptureOption"/>, writing it to an out parameter.
        /// </summary>
        /// <param name="option">specifies which capture option should be retrieved.</param>
        /// <param name="integer">the value of the capture option, if the option is a valid <see cref="CaptureOption"/> enum. Otherwise, <see cref="int.MaxValue"/>.</param>
        [RenderDocApiVersion(1, 0)]
        public static void GetCaptureOption(CaptureOption option, out uint integer)
        {
            integer = Api->GetCaptureOptionU32(option);
        }

        /// <summary>
        ///     Gets the current value of one of the different options in <see cref="CaptureOption"/>, writing it to an out parameter.
        /// </summary>
        /// <param name="option">specifies which capture option should be retrieved.</param>
        /// <param name="single">the value of the capture option, if the option is a valid <see cref="CaptureOption"/> enum. Otherwise, -<see cref="float.MaxValue"/>.</param>
        [RenderDocApiVersion(1, 0)]
        public static void GetCaptureOption(CaptureOption option, out float single)
        {
            single = Api->GetCaptureOptionF32(option);
        }

        /// <summary>
        /// Gets the current value of one of the different options in <see cref="CaptureOption"/>,
        /// converted to a boolean.
        /// </summary>
        /// <param name="option">specifies which capture option should be retrieved.</param>
        /// <returns>
        /// the value of the capture option, converted to bool, if the option is a valid <see cref="CaptureOption"/> enum.
        /// Otherwise, returns null.
        /// </returns>
        [RenderDocApiVersion(1, 0)]
        public static bool? GetCaptureOptionBool(CaptureOption option)
        {
            if (Api is null) return false;

            uint returnVal = GetCaptureOptionU32(option);
            if (returnVal == uint.MaxValue)
                return null;

            return returnVal is not 0;
        }

        /// <summary>
        /// Gets the current value of one of the different options in <see cref="CaptureOption"/>.
        /// </summary>
        /// <param name="option">specifies which capture option should be retrieved.</param>
        /// <returns>
        /// the value of the capture option, if the option is a valid <see cref="CaptureOption"/> enum.
        /// Otherwise, returns <see cref="int.MaxValue"/>.
        /// </returns>
        [RenderDocApiVersion(1, 0)]
        public static uint GetCaptureOptionU32(CaptureOption option) => Api->GetCaptureOptionU32(option);

        /// <summary>
        /// Gets the current value of one of the different options in <see cref="CaptureOption"/>.
        /// </summary>
        /// <param name="option">specifies which capture option should be retrieved.</param>
        /// <returns>
        /// the value of the capture option, if the option is a valid <see cref="CaptureOption"/> enum.
        /// Otherwise, returns -<see cref="float.MaxValue"/>.
        /// </returns>
        [RenderDocApiVersion(1, 0)]
        public static float GetCaptureOptionF32(CaptureOption option) => Api->GetCaptureOptionF32(option);

        /// <summary>
        /// Changes the key bindings in-application for changing the focussed window.
        /// </summary>
        /// <param name="buttons">lists the keys to bind.</param>
        [RenderDocApiVersion(1, 0)]
        public static void SetFocusToggleKeys(ReadOnlySpan<InputButton> buttons)
        {
            if (Api is null) return;

            fixed (InputButton* ptr = buttons)
            {
                Api->SetFocusToggleKeys(ptr, buttons.Length);
            }
        }

        /// <summary>
        /// Changes the key bindings in-application for triggering a capture on the current window.
        /// </summary>
        /// <param name="buttons">lists the keys to bind.</param>
        [RenderDocApiVersion(1, 0)]
        public static void SetCaptureKeys(ReadOnlySpan<InputButton> buttons)
        {
            if (Api is null) return;

            fixed (InputButton* ptr = buttons)
            {
                Api->SetCaptureKeys(ptr, buttons.Length);
            }
        }

        /// <summary>
        /// Attempts to remove RenderDoc and its hooks from the target process.<br/>
        /// It must be called as early as possible in the process, and will have undefined results
        /// if any graphics API functions have been called.
        /// </summary>
        [RenderDocApiVersion(1, 0)]
        public static void RemoveHooks()
        {
            if (Api is null) return;

            Api->RemoveHooks();
        }

        /// <summary>
        /// Remove RenderDoc’s crash handler from the target process.<br/>
        /// If you have your own crash handler that you want to handle any exceptions,
        /// RenderDoc’s handler could interfere; so it can be disabled.
        /// </summary>
        [RenderDocApiVersion(1, 0)]
        public static void UnloadCrashHandler()
        {
            if (Api is null) return;

            Api->UnloadCrashHandler();
        }

        /// <summary>
        /// Trigger a capture as if the user had pressed one of the capture hotkeys.<br/>
        /// The capture will be taken from the next frame presented to whichever window is considered current.
        /// </summary>
        [RenderDocApiVersion(1, 0)]
        public static void TriggerCapture()
        {
            if (Api is null) return;

            Api->TriggerCapture();
        }


        /// <summary>
        /// Gets the details of all frame capture in the current session.
        /// This simply calls <see cref="GetCapture"/> for each index available as specified by <see cref="CaptureCount"/>.
        /// </summary>
        /// <returns>An immutable array of structs representing RenderDoc Captures.</returns>
        public static ImmutableArray<Capture> GetCaptures()
        {
            if (Api is null) return [];
            int captureCount = CaptureCount;
            if (captureCount is 0) return [];

            ImmutableArray<Capture>.Builder captures = ImmutableArray.CreateBuilder<Capture>(captureCount);

            for (int captureIndex = 0; captureIndex < captureCount; captureIndex++)
            {
                if (GetCapture(captureIndex) is { } capture)
                    captures.Add(capture);
            }

            return captures.DrainToImmutable();
        }

        /// <summary>
        /// Gets the details of a particular frame capture, as specified by an index from 0 to <see cref="CaptureCount"/> - 1.
        /// </summary>
        /// <param name="index">specifies which capture to return the details of. Must be less than the value returned by <see cref="CaptureCount"/>.</param>
        /// <returns>A struct representing a RenderDoc Capture.</returns>
        [RenderDocApiVersion(1, 0)]
        public static Capture? GetCapture(int index)
        {
            if (Api is null) return null;

            int length = 0;
            if (Api->GetCapture(index, null, &length, null) == 0)
            {
                return null;
            }

            Span<byte> bytes = stackalloc byte[length + 1];
            long timestamp;

            fixed (byte* ptr = bytes)
                Api->GetCapture(index, ptr, &length, &timestamp);

            string fileName = Encoding.UTF8.GetString(bytes[length..]);
            return new Capture(index, fileName, DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime);
        }

        /// <summary>
        /// Determine the closest matching replay UI executable for the current RenderDoc module, and launch it.
        /// </summary>
        /// <param name="connectTargetControl">if the UI should immediately connect to the application.</param>
        /// <param name="commandLine">string to be appended to the command line, e.g. a capture filename. If this parameter is null, the command line will be unmodified.</param>
        /// <returns>true if the UI was successfully launched; false otherwise.</returns>
        [RenderDocApiVersion(1, 0)]
        public static bool LaunchReplayUI(bool connectTargetControl, string? commandLine = null)
        {
            if (Api is null) return false;

            if (commandLine == null)
            {
                return Api->LaunchReplayUI(connectTargetControl ? 1u : 0u, null) != 0;
            }

            fixed (byte* ptr = commandLine.ToNullTerminatedByteArray())
            {
                return Api->LaunchReplayUI(connectTargetControl ? 1u : 0u, ptr) != 0;
            }
        }

        /// <summary>
        /// Explicitly sets which window is considered active.<br/>
        /// The active window is the one that will be captured when the keybind to trigger a capture is pressed.
        /// </summary>
        /// <param name="hDevice">a handle to the API ‘device’ object that will be set active. Must be valid.</param>
        /// <param name="hWindow">a handle to the platform window handle that will be set active. Must be valid.</param>
        [RenderDocApiVersion(1, 0)]
        public static void SetActiveWindow(nint hDevice, nint hWindow)
        {
            if (Api is null) return;

            Api->SetActiveWindow((void*)hDevice, (void*)hWindow);
        }

        /// <summary>
        /// Immediately begin a capture for the specified device/window combination.
        /// </summary>
        /// <param name="hDevice">a handle to the API ‘device’ object that will be set active. May be <see cref="nint.Zero"/> to wildcard match.</param>
        /// <param name="hWindow">a handle to the platform window handle that will be set active. May be <see cref="nint.Zero"/> to wildcard match.</param>
        [RenderDocApiVersion(1, 0)]
        public static void StartFrameCapture(nint hDevice, nint hWindow)
        {
            if (Api is null) return;

            Api->StartFrameCapture((void*)hDevice, (void*)hWindow);
        }

        /// <summary>
        /// Immediately end an active capture for the specified device/window combination.
        /// </summary>
        /// <param name="hDevice">a handle to the API ‘device’ object that will be set active. May be <see cref="nint.Zero"/> to wildcard match.</param>
        /// <param name="hWindow">a handle to the platform window handle that will be set active. May be <see cref="nint.Zero"/> to wildcard match.</param>
        /// <returns>true if the capture succeeded; false otherwise.</returns>
        [RenderDocApiVersion(1, 0)]
        public static bool EndFrameCapture(nint hDevice, nint hWindow)
        {
            if (Api is null) return false;

            return Api->EndFrameCapture((void*)hDevice, (void*)hWindow) != 0;
        }

        /// <summary>
        /// Trigger multiple sequential frame captures as if the user had pressed one of the capture hotkeys before each frame.<br/>
        /// The captures will be taken from the next frames presented to whichever window is considered current.<br/>
        /// Each capture will be taken independently and saved to a separate file, with no reference to the other frames.
        /// </summary>
        /// <param name="numFrames">the number of frames to capture.</param>
        /// <remarks>Requires RenderDoc API version 1.1</remarks>
        [RenderDocApiVersion(1, 1)]
        public static void TriggerMultiFrameCapture(uint numFrames)
        {
            if (Api is null) return;

            AssertAtLeast(1, 1);
            Api->TriggerMultiFrameCapture(numFrames);
        }

        /// <summary>
        /// Adds an arbitrary comments field to the most recent capture,
        /// which will then be displayed in the UI to anyone opening the capture.
        /// <br/><br/>
        /// This is equivalent to calling <see cref="SetCaptureFileComments"/> with a null first (fileName) parameter.
        /// </summary>
        /// <param name="comments">the comments to set in the capture file.</param>
        /// <remarks>Requires RenderDoc API version 1.2</remarks>
        public static void SetMostRecentCaptureFileComments(string comments)
        {
            if (Api is null) return;

            AssertAtLeast(1, 2);

            byte[] commentBytes = comments.ToNullTerminatedByteArray();

            fixed (byte* pcomment = commentBytes)
            {
                Api->SetCaptureFileComments((byte*)nint.Zero, pcomment);
            }
        }

        /// <summary>
        /// Adds an arbitrary comments field to an existing capture on disk,
        /// which will then be displayed in the UI to anyone opening the capture.
        /// </summary>
        /// <param name="fileName">the path to the capture file to set comments in. If this path is null or an empty string, the most recent capture file that has been created will be used.</param>
        /// <param name="comments">the comments to set in the capture file.</param>
        /// <remarks>Requires RenderDoc API version 1.2</remarks>
        [RenderDocApiVersion(1, 2)]
        public static void SetCaptureFileComments(string? fileName, string comments)
        {
            if (Api is null) return;

            AssertAtLeast(1, 2);

            byte[] commentBytes = comments.ToNullTerminatedByteArray();

            fixed (byte* pcomment = commentBytes)
            {
                if (fileName is null)
                {
                    Api->SetCaptureFileComments((byte*)nint.Zero, pcomment);
                }
                else
                {
                    byte[] fileBytes = fileName.ToNullTerminatedByteArray();

                    fixed (byte* pfile = fileBytes)
                    {
                        Api->SetCaptureFileComments(pfile, pcomment);
                    }
                }
            }
        }

        /// <summary>
        /// Similar to <see cref="EndFrameCapture"/>, but the capture contents will be discarded immediately, and not processed and written to disk.<br/>
        /// This will be more efficient than <see cref="EndFrameCapture"/> if the frame capture is not needed.
        /// </summary>
        /// <param name="hDevice">a handle to the API ‘device’ object that will be set active. May be <see cref="nint.Zero"/> to wildcard match.</param>
        /// <param name="hWindow">a handle to the platform window handle that will be set active. May be <see cref="nint.Zero"/> to wildcard match.</param>
        /// <returns>true if the capture was discarded; false if there was an error or no capture was in progress.</returns>
        /// <remarks>Requires RenderDoc API version 1.4</remarks>
        [RenderDocApiVersion(1, 4)]
        public static bool DiscardFrameCapture(nint hDevice, nint hWindow)
        {
            if (Api is null) return false;

            AssertAtLeast(1, 4);
            return Api->DiscardFrameCapture((void*)hDevice, (void*)hWindow) != 0;
        }


        /// <summary>
        /// Requests that the currently connected replay UI raise its window to the top.<br/>
        /// This is only possible if an instance of the replay UI is currently connected, otherwise this method does nothing.<br/>
        /// This can be used in conjunction with <see cref="IsTargetControlConnected"/> and <see cref="LaunchReplayUI"/>,<br/> to intelligently handle showing the UI after making a capture.<br/><br/>
        /// Given OS differences, it is not guaranteed that the UI will be successfully raised even if the request is passed on.<br/>
        /// On some systems it may only be highlighted or otherwise indicated to the user.
        /// </summary>
        /// <returns>true if the request was passed onto the UI successfully; false if there is no UI connected or some other error occurred.</returns>
        /// <remarks>Requires RenderDoc API version 1.5</remarks>
        [RenderDocApiVersion(1, 5)]
        public static bool ShowReplayUI()
        {
            if (Api is null) return false;

            AssertAtLeast(1, 5);
            return Api->ShowReplayUI() != 0;
        }

        /// <summary>
        /// Sets a given title for the currently in-progress capture, which will be displayed in the UI.<br/>
        /// This can be used either with a user-defined capture using a manual start and end,
        /// or an automatic capture triggered by <see cref="TriggerCapture"/> or a keypress.<br/>
        /// If multiple captures are ongoing at once, the title will be applied to the first capture to end only.<br/>
        /// Any subsequent captures will not get any title unless the function is called again.
        /// This function can only be called while a capture is in-progress,
        /// after <see cref="StartFrameCapture"/> and before <see cref="EndFrameCapture"/>.<br/>
        /// If it is called elsewhere it will have no effect.
        /// If it is called multiple times within a capture, only the last title will have any effect.
        /// </summary>
        /// <param name="title">The title to set for the in-progress capture.</param>
        /// <remarks>Requires RenderDoc API version 1.6</remarks>
        [RenderDocApiVersion(1, 6)]
        public static void SetCaptureTitle(string title)
        {
            if (Api is null) return;

            AssertAtLeast(1, 6);
            fixed (byte* ptr = title.ToNullTerminatedByteArray())
                Api->SetCaptureTitle(ptr);
        }

        #region Dynamic Library loading

        /// <summary>
        /// Reload the internal RenderDoc API structure. Useful for manually refreshing <see cref="Api"/> while using process injection.
        /// </summary>
        /// <param name="ignoreAlreadyLoaded">Ignores the existing API function structure and overwrites it with a re-request.</param>
        /// <param name="requiredVersion">The version of the RenderDoc API required by your application.</param>
        public static void ReloadApi(bool ignoreAlreadyLoaded = false, RenderDocVersion? requiredVersion = null)
        {
            if (_loaded && !ignoreAlreadyLoaded)
                return;

            lock (typeof(RenderDoc))
            {
                // Prevent double loads.
                if (_loaded && !ignoreAlreadyLoaded)
                    return;

                if (requiredVersion.HasValue)
                    MinimumRequired = requiredVersion.Value;

                _loaded = true;
                _api = GetApi(MinimumRequired);

                if (_api != null)
                    AssertAtLeast(MinimumRequired);
            }
        }

        private static RenderDocApi* _api = null;
        private static bool _loaded;

        private static RenderDocApi* Api
        {
            get
            {
                ReloadApi();
                return _api;
            }
        }

        private static readonly Regex _dynamicLibraryPattern = RenderDocApiDynamicLibraryRegex();

        private static RenderDocApi* GetApi(RenderDocVersion minimumRequired = RenderDocVersion.Version_1_0_0)
        {
            foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
            {
                string moduleName = module.FileName ?? string.Empty;

                if (!_dynamicLibraryPattern.IsMatch(moduleName))
                    continue;

                if (!NativeLibrary.TryLoad(moduleName, out nint moduleHandle))
                    return null;

                if (!NativeLibrary.TryGetExport(moduleHandle, "RENDERDOC_GetAPI", out nint procAddress))
                    return null;

                var RENDERDOC_GetApi = (delegate* unmanaged[Cdecl]<RenderDocVersion, RenderDocApi**, int>)procAddress;

                RenderDocApi* api;
                return RENDERDOC_GetApi(minimumRequired, &api) != 0 ? api : null;
            }

            return null;
        }

        private static void AssertAtLeast(RenderDocVersion rdv, [CallerMemberName] string callee = "")
        {
            Version ver = rdv.SystemVersion;
            AssertAtLeast(ver.Major, ver.Minor, ver.Build, callee);
        }

        private static void AssertAtLeast(int major, int minor, int patch = 0, [CallerMemberName] string callee = "")
        {
            if (!AssertVersionEnabled)
                return;

            if (Version!.Major < major)
                goto fail;

            if (Version.Major > major)
                goto success;
            if (Version.Minor < minor)
                goto fail;
            if (Version.Minor > minor)
                goto success;
            if (Version.Build < patch)
                goto fail;

            success:
            return;

            fail:
            Version minVersion =
                typeof(RenderDoc).GetMethod(callee)!.GetCustomAttribute<RenderDocApiVersionAttribute>()!.MinVersion;
            throw new NotSupportedException(
                $"This API was introduced in RenderDoc API {minVersion}. Current API version is {Version}.");
        }

        private static byte[] ToNullTerminatedByteArray(this string str, Encoding? encoding = null)
        {
            encoding ??= Encoding.UTF8;

            return encoding.GetBytes(str + '\0');
        }

        [GeneratedRegex(@"(lib)?renderdoc(\.dll|\.so|\.dylib)(\.\d+)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex RenderDocApiDynamicLibraryRegex();

        #endregion
    }
}
