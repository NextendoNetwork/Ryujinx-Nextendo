using System;
using System.Runtime.InteropServices;

namespace Ryujinx.Ava.UI.Helpers
{
    public class macOSNativeInterop
    {
        // TODO: add a parameter for prompt style
        // TODO: check success of prompt box
        public static int SimpleMessageBox(string caption, string text, string button)
        {
            
            // Grab what we need to make the message box.
            const string ObjCRuntime = "/usr/lib/libobjc.A.dylib";
            const string FoundationFramework = "/System/Library/Frameworks/Foundation.framework/Foundation";
            const string AppKitFramework = "/System/Library/Frameworks/AppKit.framework/AppKit";
                        
            [DllImport(ObjCRuntime, EntryPoint = "sel_registerName")]
            static extern IntPtr GetSelector(string name);
                        
            [DllImport(ObjCRuntime, EntryPoint = "objc_getClass")]
            static extern IntPtr GetClass(string name);

            [DllImport(FoundationFramework, EntryPoint = "objc_msgSend")]
            static extern IntPtr SendMessage(IntPtr target, IntPtr selector);

            [DllImport(FoundationFramework, EntryPoint = "objc_msgSend")]
            static extern IntPtr SendMessageWithParameter(IntPtr target, IntPtr selector, IntPtr param);

            [DllImport(ObjCRuntime)]
            static extern IntPtr dlopen(string path, int mode);

            dlopen(AppKitFramework, 0x1); // have to invoke AppKit so that NSAlert doesn't return a null pointer
                        
            IntPtr NSStringClass = GetClass("NSString");
            IntPtr Selector = GetSelector("stringWithUTF8String:");
            IntPtr SharedApp = SendMessage(GetClass("NSApplication"), GetSelector("sharedApplication"));
            IntPtr NSAlert = SendMessage(GetClass("NSAlert"), GetSelector("alloc"));
            IntPtr AlertInstance = SendMessage(NSAlert, GetSelector("init"));
                        
            // Create caption, text, and button text.
            IntPtr boxCaption = SendMessageWithParameter(NSStringClass, Selector, Marshal.StringToHGlobalAnsi(caption));
            IntPtr boxText = SendMessageWithParameter(NSStringClass, Selector, Marshal.StringToHGlobalAnsi(text));
            IntPtr boxButton = SendMessageWithParameter(NSStringClass, Selector, Marshal.StringToHGlobalAnsi(button));
                        
            // Set up the window.
            SendMessageWithParameter(SharedApp, GetSelector("setActivationPolicy:"), IntPtr.Zero); // Give it a window.
            SendMessageWithParameter(SharedApp, GetSelector("activateIgnoringOtherApps:"), (IntPtr) 1); // Force it to the front.
                        
            // Set up the message box.
            SendMessageWithParameter(AlertInstance, GetSelector("setAlertStyle:"), IntPtr.Zero); // Set style to warning.
            SendMessageWithParameter(AlertInstance, GetSelector("setMessageText:"), boxCaption);
            SendMessageWithParameter(AlertInstance, GetSelector("setInformativeText:"), boxText);
            SendMessageWithParameter(AlertInstance, GetSelector("addButtonWithTitle:"), boxButton);
                        
            // Send prompt to user, then clean up.
            SendMessage(AlertInstance, GetSelector("runModal"));
            SendMessage(AlertInstance, GetSelector("release"));
            return 0;
        }
    }
}
