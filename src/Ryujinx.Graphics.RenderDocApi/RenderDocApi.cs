namespace Ryujinx.Graphics.RenderDocApi
{
#pragma warning disable CS0649
    internal unsafe struct RenderDocApi
    {
        public delegate* unmanaged[Cdecl]<int*, int*, int*, void> GetApiVersion;

        public delegate* unmanaged[Cdecl]<CaptureOption, uint, int> SetCaptureOptionU32;
        public delegate* unmanaged[Cdecl]<CaptureOption, float, int> SetCaptureOptionF32;
        public delegate* unmanaged[Cdecl]<CaptureOption, uint> GetCaptureOptionU32;
        public delegate* unmanaged[Cdecl]<CaptureOption, float> GetCaptureOptionF32;

        public delegate* unmanaged[Cdecl]<InputButton*, int, void> SetFocusToggleKeys;
        public delegate* unmanaged[Cdecl]<InputButton*, int, void> SetCaptureKeys;

        public delegate* unmanaged[Cdecl]<OverlayBits> GetOverlayBits;
        public delegate* unmanaged[Cdecl]<OverlayBits, OverlayBits, void> MaskOverlayBits;

        public delegate* unmanaged[Cdecl]<void> RemoveHooks;
        public delegate* unmanaged[Cdecl]<void> UnloadCrashHandler;
        public delegate* unmanaged[Cdecl]<byte*, void> SetCaptureFilePathTemplate;
        public delegate* unmanaged[Cdecl]<byte*> GetCaptureFilePathTemplate;

        public delegate* unmanaged[Cdecl]<int> GetNumCaptures;
        public delegate* unmanaged[Cdecl]<int, byte*, int*, long*, uint> GetCapture;
        public delegate* unmanaged[Cdecl]<void> TriggerCapture;
        public delegate* unmanaged[Cdecl]<uint> IsTargetControlConnected;
        public delegate* unmanaged[Cdecl]<uint, byte*, uint> LaunchReplayUI;

        public delegate* unmanaged[Cdecl]<void*, void*, void> SetActiveWindow;
        public delegate* unmanaged[Cdecl]<void*, void*, void> StartFrameCapture;
        public delegate* unmanaged[Cdecl]<uint> IsFrameCapturing;
        public delegate* unmanaged[Cdecl]<void*, void*, uint> EndFrameCapture;

        // 1.1
        public delegate* unmanaged[Cdecl]<uint, void> TriggerMultiFrameCapture;

        // 1.2
        public delegate* unmanaged[Cdecl]<byte*, byte*, void> SetCaptureFileComments;

        // 1.3
        public delegate* unmanaged[Cdecl]<void*, void*, uint> DiscardFrameCapture;

        // 1.5
        public delegate* unmanaged[Cdecl]<uint> ShowReplayUI;

        // 1.6
        public delegate* unmanaged[Cdecl]<byte*, void> SetCaptureTitle;
    }
#pragma warning restore CS0649
}
