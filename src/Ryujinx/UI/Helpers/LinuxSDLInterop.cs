using System;
using System.Runtime.InteropServices;

namespace Ryujinx.Ava.UI.Helpers
{
    public class LinuxSDLInterop
    {
        // TODO: add a parameter for prompt style
        // TODO: look into adding text for the button
        // TODO: check success of prompt box
        public static int SimpleMessageBox(string caption, string text)
        {
            const string sdl = "SDL2";
                        
            [DllImport(sdl)]
            static extern int SDL_Init(uint flags);
                        
            [DllImport(sdl, CallingConvention = CallingConvention.Cdecl)]
            static extern int SDL_ShowSimpleMessageBox(uint flags, string title, string message, IntPtr window);
                        
            [DllImport(sdl)]
            static extern void SDL_Quit();
                        
            SDL_Init(0); 
            SDL_ShowSimpleMessageBox(32 /* 32 = warning style */, caption, text, IntPtr.Zero);
            SDL_Quit();
            return 0;
        }
    }
}
