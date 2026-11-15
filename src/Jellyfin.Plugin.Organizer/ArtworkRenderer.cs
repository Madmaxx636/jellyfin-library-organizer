using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace Jellyfin.Plugin.Organizer;

/// <summary>Draws collection posters, thumbs and backdrops from the artwork of the titles inside.</summary>
public static class ArtworkRenderer
{
    public const int PosterWidth = 600;
    public const int PosterHeight = 900;
    public const int WideWidth = 1280;
    public const int WideHeight = 720;

    private static readonly string[] FontNames = { "Inter", "Noto Sans", "DejaVu Sans", "Liberation Sans", "Arial" };

    /// <summary>A pleasant colour from the name (stable across runs), or the user's own #RRGGBB.</summary>
    public static SKColor Accent(string name, string? custom)
    {
        if (!string.IsNullOrWhiteSpace(custom) && SKColor.TryParse(custom.Trim(), out var c))
        {
            return c.WithAlpha(255);
        }

        uint hash = 2166136261;
        foreach (var ch in name.ToLowerInvariant())
        {
            hash = (hash ^ ch) * 16777619;
        }

        return SKColor.FromHsl(hash % 360, 62, 46);
    }

    public static byte[] Poster(string title, string subtitle, IReadOnlyList<string> images, SKColor accent, string style)
    {
        using var bmp = new SKBitmap(PosterWidth, PosterHeight);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(Dark(accent, 0.18f));

        var art = Load(images, 6);
        try
        {
            var top = new SKRect(0, 0, PosterWidth, 640);
            if (style != "Title" && art.Count > 0)
            {
                Mosaic(canvas, art, top);
            }
            else
            {
                Gradient(canvas, top, accent);
            }

            // Fade the artwork into the title panel.
            using var fade = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 380), new SKPoint(0, 700),
                    new[] { Dark(accent, 0.18f).WithAlpha(0), Dark(accent, 0.18f) },
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(new SKRect(0, 380, PosterWidth, 700), fade);
            canvas.DrawRect(new SKRect(0, 700, PosterWidth, PosterHeight), new SKPaint { Color = Dark(accent, 0.18f) });

            using var bar = new SKPaint { Color = accent, IsAntialias = true };
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(48, 676, 148, 684), 4), bar);

            DrawTitle(canvas, title, 48, 702, PosterWidth - 96, 168, 74, 38, SKColors.White, 3, subtitle, centered: false);
        }
        finally
        {
            Dispose(art);
        }

        return Encode(bmp);
    }

    public static byte[] Wide(string title, string subtitle, IReadOnlyList<string> images, SKColor accent, string style)
    {
        using var bmp = new SKBitmap(WideWidth, WideHeight);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(Dark(accent, 0.14f));

        var art = Load(images, 5);
        try
        {
            var area = new SKRect(0, 0, WideWidth, WideHeight);
            if (style != "Title" && art.Count > 0)
            {
                var w = WideWidth / (float)art.Count;
                for (var i = 0; i < art.Count; i++)
                {
                    Cover(canvas, art[i], new SKRect(i * w, 0, (i + 1) * w + 1, WideHeight));
                }
            }
            else
            {
                Gradient(canvas, area, accent);
            }

            using var shade = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(WideWidth, 0),
                    new[] { Dark(accent, 0.14f).WithAlpha(245), Dark(accent, 0.14f).WithAlpha(150), Dark(accent, 0.14f).WithAlpha(40) },
                    new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(area, shade);

            using var bar = new SKPaint { Color = accent, IsAntialias = true };
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(80, 300, 200, 310), 5), bar);
            DrawTitle(canvas, title, 80, 330, 640, 330, 96, 48, SKColors.White, 3, subtitle, centered: false);
        }
        finally
        {
            Dispose(art);
        }

        return Encode(bmp);
    }

    private static SKColor Dark(SKColor accent, float lightness)
    {
        accent.ToHsl(out var h, out var s, out _);
        return SKColor.FromHsl(h, Math.Min(s, 45), lightness * 100);
    }

    private static void Gradient(SKCanvas canvas, SKRect area, SKColor accent)
    {
        accent.ToHsl(out var h, out var s, out var l);
        var a = SKColor.FromHsl(h, s, Math.Min(l + 8, 70));
        var b = SKColor.FromHsl((h + 40) % 360, s, Math.Max(l - 18, 12));
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(area.Left, area.Top), new SKPoint(area.Right, area.Bottom),
                new[] { a, b }, SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(area, paint);
    }

    private static void Mosaic(SKCanvas canvas, List<SKBitmap> art, SKRect area)
    {
        var rects = new List<SKRect>();
        var w = area.Width;
        var h = area.Height;
        switch (Math.Min(art.Count, 6))
        {
            case 1:
                rects.Add(area);
                break;
            case 2:
                rects.Add(new SKRect(0, 0, w / 2, h));
                rects.Add(new SKRect(w / 2, 0, w, h));
                break;
            case 3:
                rects.Add(new SKRect(0, 0, w, h * 0.55f));
                rects.Add(new SKRect(0, h * 0.55f, w / 2, h));
                rects.Add(new SKRect(w / 2, h * 0.55f, w, h));
                break;
            case 4:
            case 5:
                for (var i = 0; i < 4; i++)
                {
                    rects.Add(new SKRect((i % 2) * w / 2, (i / 2) * h / 2, (i % 2 + 1) * w / 2, (i / 2 + 1) * h / 2));
                }

                break;
            default:
                // Six portrait tiles in 3 columns x 2 rows keep real posters close to their own shape.
                for (var i = 0; i < 6; i++)
                {
                    rects.Add(new SKRect((i % 3) * w / 3, (i / 3) * h / 2, (i % 3 + 1) * w / 3, (i / 3 + 1) * h / 2));
                }

                break;
        }

        for (var i = 0; i < rects.Count; i++)
        {
            Cover(canvas, art[i], rects[i]);
        }

        // Thin dark gaps between tiles keep the posters readable as separate pieces.
        using var gap = new SKPaint { Color = new SKColor(0, 0, 0, 120), StrokeWidth = 3, Style = SKPaintStyle.Stroke };
        foreach (var r in rects)
        {
            canvas.DrawRect(r, gap);
        }
    }

    /// <summary>Fills the rectangle with the image, cropping the overflow like CSS "cover".</summary>
    private static void Cover(SKCanvas canvas, SKBitmap source, SKRect dest)
    {
        var scale = Math.Max(dest.Width / source.Width, dest.Height / source.Height);
        var sw = dest.Width / scale;
        var sh = dest.Height / scale;
        var sx = (source.Width - sw) / 2;
        var sy = (source.Height - sh) / 3; // favour the upper part: faces and titles sit there
        using var image = SKImage.FromBitmap(source);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(image, new SKRect(sx, sy, sx + sw, sy + sh), dest,
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
    }

    private static void DrawTitle(
        SKCanvas canvas, string title, float x, float y, float maxWidth, float maxHeight,
        float startSize, float minSize, SKColor color, int maxLines, string subtitle, bool centered)
    {
        using var typeface = Typeface(SKFontStyle.Bold);
        var size = startSize;
        List<string> lines;
        using var font = new SKFont(typeface, size);
        while (true)
        {
            font.Size = size;
            lines = Wrap(title, font, maxWidth);
            var subtitleHeight = string.IsNullOrWhiteSpace(subtitle) ? 0 : Math.Max(26, size * 0.42f) + 18;
            if ((lines.Count <= maxLines && lines.Count * size * 1.12f + subtitleHeight <= maxHeight) || size <= minSize)
            {
                break;
            }

            size -= 2;
        }

        if (lines.Count > maxLines)
        {
            lines = lines.Take(maxLines).ToList();
            lines[^1] = lines[^1].TrimEnd() + "…";
        }

        using var paint = new SKPaint { Color = color, IsAntialias = true };
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 140), IsAntialias = true };
        var baseline = y + size;
        foreach (var line in lines)
        {
            var lx = centered ? x + (maxWidth - font.MeasureText(line)) / 2 : x;
            canvas.DrawText(line, lx + 2, baseline + 2, SKTextAlign.Left, font, shadow);
            canvas.DrawText(line, lx, baseline, SKTextAlign.Left, font, paint);
            baseline += size * 1.12f;
        }

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            using var small = new SKFont(Typeface(SKFontStyle.Normal), Math.Max(26, size * 0.42f));
            using var sub = new SKPaint { Color = new SKColor(255, 255, 255, 200), IsAntialias = true };
            canvas.DrawText(subtitle, x, baseline - size * 0.12f + 6, SKTextAlign.Left, small, sub);
        }
    }

    private static List<string> Wrap(string text, SKFont font, float maxWidth)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureText(candidate) <= maxWidth || current.Length == 0)
            {
                current = candidate;
            }
            else
            {
                lines.Add(current);
                current = word;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    private static SKTypeface Typeface(SKFontStyle style)
    {
        foreach (var name in FontNames)
        {
            var t = SKTypeface.FromFamilyName(name, style);
            if (t is not null && string.Equals(t.FamilyName, name, StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }

            t?.Dispose();
        }

        return SKTypeface.FromFamilyName(null, style) ?? SKTypeface.Default;
    }

    private static List<SKBitmap> Load(IReadOnlyList<string> paths, int max)
    {
        var result = new List<SKBitmap>();
        foreach (var path in paths)
        {
            if (result.Count >= max)
            {
                break;
            }

            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var bmp = SKBitmap.Decode(path);
                if (bmp is not null && bmp.Width > 0 && bmp.Height > 0)
                {
                    result.Add(bmp);
                }
            }
            catch (Exception)
            {
                // An unreadable image is simply skipped.
            }
        }

        return result;
    }

    private static void Dispose(IEnumerable<SKBitmap> bitmaps)
    {
        foreach (var b in bitmaps)
        {
            b.Dispose();
        }
    }

    private static byte[] Encode(SKBitmap bmp)
    {
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    public static string Subtitle(int count, string mediaType)
    {
        var noun = mediaType switch
        {
            "Series" => "series",
            "Both" => count == 1 ? "title" : "titles",
            _ => count == 1 ? "movie" : "movies"
        };
        return string.Create(CultureInfo.InvariantCulture, $"{count} {noun}");
    }
}
