using OpenTK;
using OpenTK.Graphics.OpenGL;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.Graphics.OpenGL;
using Ryujinx.Input.HLE;
using System;
using SDL;
using static SDL.SDL3;
using System.Runtime.InteropServices;

namespace Ryujinx.Headless
{
    unsafe class OpenGLWindow : WindowBase
    {
        private static void CheckResult(bool result)
        {
            if (!result)
            {
                throw new InvalidOperationException($"SDL_GL function returned an error: {SDL_GetError()}");
            }
        }

        private static void SetupOpenGLAttributes(bool sharedContext, GraphicsDebugLevel debugLevel)
        {
            CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, 4));
            CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, 3));
            CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL_GLProfile.SDL_GL_CONTEXT_PROFILE_COMPATIBILITY));
            CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_FLAGS, debugLevel != GraphicsDebugLevel.None ? (int)SDL_GLContextFlag.SDL_GL_CONTEXT_DEBUG_FLAG : 0));
            CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_SHARE_WITH_CURRENT_CONTEXT, sharedContext ? 1 : 0));

            CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_ACCELERATED_VISUAL, 1));
            CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_RED_SIZE, 8));
            CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_GREEN_SIZE, 8));
            CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_BLUE_SIZE, 8));
            CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_ALPHA_SIZE, 8));
            CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_DEPTH_SIZE, 16));
            CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_STENCIL_SIZE, 0));
            CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_DOUBLEBUFFER, 1));
            CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_STEREO, 0));
        }

        private class OpenToolkitBindingsContext : IBindingsContext
        {
            public nint GetProcAddress(string procName)
            {
                return SDL_GL_GetProcAddress(procName);
            }
        }

        private class SDL3OpenGLContext : IOpenGLContext
        {
            private readonly SDL_GLContextState* _context;
            private readonly SDL_Window* _window;
            private readonly bool _shouldDisposeWindow;

            public SDL3OpenGLContext(SDL_GLContextState* context, SDL_Window* window, bool shouldDisposeWindow = true)
            {
                _context = context;
                _window = window;
                _shouldDisposeWindow = shouldDisposeWindow;
            }

            public unsafe static SDL3OpenGLContext CreateBackgroundContext(SDL3OpenGLContext sharedContext)
            {
                sharedContext.MakeCurrent();

                // Ensure we share our contexts.
                SetupOpenGLAttributes(true, GraphicsDebugLevel.None);
                SDL_Window* windowHandle = SDL_CreateWindow("Ryujinx background context window", 1, 1, SDL_WindowFlags.SDL_WINDOW_OPENGL | SDL_WindowFlags.SDL_WINDOW_HIDDEN);
                SDL_GLContextState* context = SDL_GL_CreateContext(windowHandle);

                GL.LoadBindings(new OpenToolkitBindingsContext());

                CheckResult(SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_SHARE_WITH_CURRENT_CONTEXT, 0));

                CheckResult(SDL_GL_MakeCurrent(windowHandle, null));

                return new SDL3OpenGLContext(context, windowHandle);
            }

            public void MakeCurrent()
            {
                if (SDL_GL_GetCurrentContext() == _context || SDL_GL_GetCurrentWindow() == _window)
                {
                    return;
                }

                bool res = SDL_GL_MakeCurrent(_window, _context);

                if (!res)
                {
                    string errorMessage = $"SDL_GL_CreateContext failed with error \"{SDL_GetError()}\"";

                    Logger.Error?.Print(LogClass.Application, errorMessage);

                    throw new Exception(errorMessage);
                }
            }

            public bool HasContext() => SDL_GL_GetCurrentContext() != null;

            public void Dispose()
            {
                SDL_GL_DestroyContext(_context);

                if (_shouldDisposeWindow)
                {
                    SDL_DestroyWindow(_window);
                }
            }
        }

        private SDL3OpenGLContext _openGLContext;

        public OpenGLWindow(
            InputManager inputManager,
            GraphicsDebugLevel glLogLevel,
            AspectRatio aspectRatio,
            bool enableMouse,
            HideCursorMode hideCursorMode,
            bool ignoreControllerApplet)
            : base(inputManager, glLogLevel, aspectRatio, enableMouse, hideCursorMode, ignoreControllerApplet)
        {
        }

        public override SDL_WindowFlags WindowFlags => SDL_WindowFlags.SDL_WINDOW_OPENGL;

        protected override void InitializeWindowRenderer()
        {
            // Ensure to not share this context with other contexts before this point.
            SetupOpenGLAttributes(false, GlLogLevel);
            SDL_GLContextState* context = SDL_GL_CreateContext(WindowHandle);
            CheckResult(SDL_GL_SetSwapInterval(1));

            if (context == null)
            {
                string errorMessage = $"SDL_GL_CreateContext failed with error \"{SDL_GetError()}\"";

                Logger.Error?.Print(LogClass.Application, errorMessage);

                throw new Exception(errorMessage);
            }

            // NOTE: The window handle needs to be disposed by the thread that created it and is handled separately.
            _openGLContext = new SDL3OpenGLContext(context, WindowHandle, false);

            // First take exclusivity on the OpenGL context.
            ((OpenGLRenderer)Renderer).InitializeBackgroundContext(SDL3OpenGLContext.CreateBackgroundContext(_openGLContext));

            _openGLContext.MakeCurrent();

            GL.ClearColor(0, 0, 0, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            SwapBuffers();

            if (IsExclusiveFullscreen)
            {
                Renderer?.Window.SetSize(ExclusiveFullscreenWidth, ExclusiveFullscreenHeight);
                MouseDriver.SetClientSize(ExclusiveFullscreenWidth, ExclusiveFullscreenHeight);
            }
            else if (IsFullscreen)
            {
                // NOTE: grabbing the main display's dimensions directly as OpenGL doesn't scale along like the VulkanWindow.
                SDL_Rect displayBounds = new();
                if (!SDL_GetDisplayBounds(DisplayId, &displayBounds))
                {
                    Logger.Warning?.Print(LogClass.Application, $"Could not retrieve display bounds: {SDL_GetError()}");

                    // Fallback to defaults
                    displayBounds.w = DefaultWidth;
                    displayBounds.h = DefaultHeight;
                }

                Renderer?.Window.SetSize(displayBounds.w, displayBounds.h);
                MouseDriver.SetClientSize(displayBounds.w, displayBounds.h);
            }
            else
            {
                Renderer?.Window.SetSize(DefaultWidth, DefaultHeight);
                MouseDriver.SetClientSize(DefaultWidth, DefaultHeight);
            }
        }

        protected override void InitializeRenderer() { }

        protected override void FinalizeWindowRenderer()
        {
            // Try to bind the OpenGL context before calling the gpu disposal.
            _openGLContext.MakeCurrent();

            Device.DisposeGpu();

            // Unbind context and destroy everything
            CheckResult(SDL_GL_MakeCurrent(WindowHandle, null));
            _openGLContext.Dispose();
        }

        protected override void SwapBuffers()
        {
            SDL_GL_SwapWindow(WindowHandle);
        }
    }
}
