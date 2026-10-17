using Ryujinx.Common.Logging;
using Ryujinx.HLE.UI;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Notifications of the emulator drawn like the console's (a dark card at the top
    /// left, an icon in a circle, one or two lines), laid over the picture by the GPU and sliding in
    /// and out like the console's: free text and icon (update of the emulator...).
    /// </summary>
    public static class NextendoConsoleToasts
    {
        // The card, in pixels of a 1920x1080 picture (the size of the console's notifications).
        private const int CardWidth = 560;
        private const int CardHeight = 114;
        private const double SlideSeconds = 0.25;
        private const double HoldSeconds = 4.5;
        private const float Margin = 30;

        private static readonly ConcurrentQueue<(string Title, string Text, byte[] Icon, string Glyph)> _queue = new();
        private static Thread _thread;
        private static readonly object _lock = new();

        /// <summary>The window of the GPU to lay the card over (the running emulation's), or null.</summary>
        public static Func<Ryujinx.Graphics.Gpu.Window> Target { get; set; }

        public static void Show(string title, string text, byte[] icon = null, string glyph = null)
        {
            // [Nextendo] the HOME Menu as the console has it: only the system's own notifications
            if (Ryujinx.Common.Configuration.NextendoHomeMenuVanilla.Enabled)
            {
                return;
            }

            _queue.Enqueue((title, text, icon, glyph));

            lock (_lock)
            {
                if (_thread is { IsAlive: true })
                {
                    return;
                }

                _thread = new Thread(Run) { IsBackground = true, Name = "Nextendo.ConsoleToasts" };
                _thread.Start();
            }
        }

        private static void Run()
        {
            while (_queue.TryDequeue(out (string Title, string Text, byte[] Icon, string Glyph) toast))
            {
                // One notification on screen at a time: this card waits for the console's ones.
                Ryujinx.Horizon.Ovln.NotificationTurn.Take(SlideSeconds * 2 + HoldSeconds + 0.3);

                try
                {
                    Play(toast.Title, toast.Text, toast.Icon, toast.Glyph);
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo][notif] notification not shown: {e.Message}");
                }
                finally
                {
                    Ryujinx.Horizon.Ovln.NotificationTurn.Release();
                }
            }
        }

        private static void Play(string title, string text, byte[] icon, string glyph)
        {
            if (Target?.Invoke() is not { } window)
            {
                return;
            }

            window.SetNotificationPicture(Draw(title, text, icon, glyph), CardWidth, CardHeight);

            float width = CardWidth / 1920f;
            float height = CardHeight / 1080f;
            Stopwatch clock = Stopwatch.StartNew();
            double total = SlideSeconds * 2 + HoldSeconds;

            while (clock.Elapsed.TotalSeconds < total)
            {
                double t = clock.Elapsed.TotalSeconds;
                double shown;

                if (t < SlideSeconds)
                {
                    shown = EaseOut(t / SlideSeconds);
                }
                else if (t > SlideSeconds + HoldSeconds)
                {
                    shown = 1 - EaseIn((t - SlideSeconds - HoldSeconds) / SlideSeconds);
                }
                else
                {
                    shown = 1;
                }

                // away from the edges, as the console's (20 of 1280: 30 pixels of 1920)
                window.SetNotificationPlace(true, (float)((shown - 1) * width + shown * Margin / 1920f), Margin / 1080f, width, height);

                Thread.Sleep(8);
            }

            window.SetNotificationPlace(false, 0, 0, 0, 0);
        }

        private static readonly Regex _svgPath = new(@"<path[^>]*? fill=""#([0-9A-Fa-f]{6})""[^>]*? d=""([^""]+)""", RegexOptions.Compiled);

        // The paths of an SVG of the assets (fill, then even-odd outline: holes are cut out).
        private static void DrawPictogram(SKCanvas canvas, string name, SKRect place)
        {
            string svg;

            // "fit:name": the drawing made as big as the place (a flair alone, drawn small in a
            // corner of its square for going over a controller).
            bool fit = name.StartsWith("fit:", StringComparison.Ordinal);

            if (fit)
            {
                name = name[4..];
            }

            try
            {
                using Stream stream = typeof(NextendoConsoleToasts).Assembly.GetManifestResourceStream($"Ryujinx.Assets.ControllerIcons.{name}.svg");

                if (stream == null)
                {
                    return;
                }

                using StreamReader reader = new(stream);
                svg = reader.ReadToEnd();
            }
            catch (Exception)
            {
                return;
            }

            SKRect drawing = SKRect.Create(0, 0, 64, 64);

            if (fit)
            {
                drawing = SKRect.Empty;

                foreach (Match match in _svgPath.Matches(svg))
                {
                    using SKPath path = SKPath.ParseSvgPathData(match.Groups[2].Value);

                    if (path != null)
                    {
                        drawing = drawing.IsEmpty ? path.Bounds : SKRect.Union(drawing, path.Bounds);
                    }
                }

                // square, centred on the drawing
                float side = Math.Max(drawing.Width, drawing.Height) * 1.1f;
                drawing = SKRect.Create(drawing.MidX - side / 2, drawing.MidY - side / 2, side, side);
            }

            canvas.Save();
            canvas.Translate(place.Left, place.Top);
            canvas.Scale(place.Width / drawing.Width, place.Height / drawing.Height);
            canvas.Translate(-drawing.Left, -drawing.Top);

            foreach (Match match in _svgPath.Matches(svg))
            {
                using SKPath path = SKPath.ParseSvgPathData(match.Groups[2].Value);

                if (path == null)
                {
                    continue;
                }

                using SKPaint paint = new() { Color = SKColor.Parse(match.Groups[1].Value), IsAntialias = true };
                canvas.DrawPath(path, paint);
            }

            canvas.Restore();
        }

        private static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

        private static double EaseIn(double t) => Math.Pow(Math.Clamp(t, 0, 1), 3);

        // The card: black with round corners, the icon in a circle, the text in white.
        private static byte[] Draw(string title, string text, byte[] icon, string glyph)
        {
            SKImageInfo info = new(CardWidth, CardHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);

            using SKBitmap bitmap = new(info);
            using SKCanvas canvas = new(bitmap);

            // as the console's notifications now are (NextendoFriendPage): black, round corners in
            // Discord's proportion (a fifth of the height), no edge
            canvas.Clear(SKColors.Transparent);

            using (SKPaint card = new() { Color = new SKColor(0, 0, 0, 235), IsAntialias = true })
            {
                canvas.DrawRoundRect(0, 0, CardWidth, CardHeight, CardHeight * 0.19f, CardHeight * 0.19f, card);
            }

            const float iconSize = 76;
            SKRect iconRect = SKRect.Create(26, (CardHeight - iconSize) / 2, iconSize, iconSize);
            SKBitmap decoded = icon != null ? SKBitmap.Decode(icon) : null;

            if (decoded != null && decoded.Width > decoded.Height * 1.3f)
            {
                // A screenshot: a 16:9 frame with rounded corners.
                iconRect = SKRect.Create(22, (CardHeight - 72) / 2, 128, 72);

                using (decoded)
                using (SKPath frame = new())
                {
                    frame.AddRoundRect(iconRect, 6, 6);
                    canvas.Save();
                    canvas.ClipPath(frame, SKClipOperation.Intersect, true);
                    canvas.DrawBitmap(decoded, iconRect, new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true });
                    canvas.Restore();
                }

                decoded = null;
            }
            else if (decoded is { } picture)
            {
                // A game icon or a badge: a square with slightly rounded corners.
                using (picture)
                using (SKPath square = new())
                {
                    square.AddRoundRect(iconRect, 6, 6);
                    canvas.Save();
                    canvas.ClipPath(square, SKClipOperation.Intersect, true);
                    canvas.DrawBitmap(picture, iconRect, new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true });
                    canvas.Restore();
                }
            }
            else if (!string.IsNullOrEmpty(glyph))
            {
                // A pictogram of Assets/ControllerIcons (Kenney's Input Prompts, 64x64), white like
                // the HOME Menu's own.
                iconRect = SKRect.Create(18, (CardHeight - 92) / 2f, 92, 92);
                // "controller+flair": the controller a little smaller at the top left, the flair
                // (battery, disconnected) over its bottom right corner, as Kenney composes them.
                string[] parts = glyph.Split('+');

                if (parts.Length > 1)
                {
                    DrawPictogram(canvas, parts[0], SKRect.Create(iconRect.Left, iconRect.Top, iconRect.Width * 0.86f, iconRect.Height * 0.86f));
                    DrawPictogram(canvas, parts[1], iconRect);
                }
                else
                {
                    DrawPictogram(canvas, glyph, iconRect);
                }
            }

            float textLeft = iconRect.Right + 28;
            float textWidth = CardWidth - textLeft - 20;

            using SKTypeface typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal) ?? SKTypeface.Default;
            using SKPaint white = new() { Color = SKColors.White, IsAntialias = true, Typeface = TypefaceFallback.For(typeface, title), TextSize = 27 };
            using SKPaint grey = new() { Color = new SKColor(0xC8, 0xC8, 0xC8), IsAntialias = true, Typeface = TypefaceFallback.For(typeface, text), TextSize = 23 };

            if (string.IsNullOrEmpty(text))
            {
                canvas.DrawText(Fit(title, white, textWidth), textLeft, CardHeight / 2f + 10, white);
            }
            else
            {
                canvas.DrawText(Fit(title, white, textWidth), textLeft, CardHeight / 2f - 6, white);
                canvas.DrawText(Fit(text, grey, textWidth), textLeft, CardHeight / 2f + 26, grey);
            }

            canvas.Flush();

            return bitmap.Bytes;
        }

        // A line too long for the card ends in "...".
        private static string Fit(string line, SKPaint paint, float width)
        {
            if (paint.MeasureText(line) <= width)
            {
                return line;
            }

            while (line.Length > 1 && paint.MeasureText(line + "…") > width)
            {
                line = line[..^1];
            }

            return line + "…";
        }
    }
}
