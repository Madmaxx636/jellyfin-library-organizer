using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.Organizer;
using SkiaSharp;
using Xunit;

public class ArtworkTests
{
    private static List<string> FakePosters(int n, int w = 400, int h = 600)
    {
        var dir = Path.Combine(Path.GetTempPath(), "org-art-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var paths = new List<string>();
        for (var i = 0; i < n; i++)
        {
            using var bmp = new SKBitmap(w, h);
            using var c = new SKCanvas(bmp);
            using var paint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h),
                    new[] { SKColor.FromHsl(i * 53 % 360, 70, 55), SKColor.FromHsl((i * 53 + 90) % 360, 60, 25) }, SKShaderTileMode.Clamp)
            };
            c.DrawRect(new SKRect(0, 0, w, h), paint);
            using var img = SKImage.FromBitmap(bmp);
            var path = Path.Combine(dir, $"p{i}.jpg");
            File.WriteAllBytes(path, img.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray());
            paths.Add(path);
        }

        return paths;
    }

    private static SKBitmap Decode(byte[] bytes) => SKBitmap.Decode(bytes);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void PostersAreTheRightSizeForAnyNumberOfTitles(int count)
    {
        var accent = ArtworkRenderer.Accent("Filipino Movies", null);
        var bytes = ArtworkRenderer.Poster("Filipino Movies", "12 movies", FakePosters(count), accent, "Mosaic");
        using var bmp = Decode(bytes);
        Assert.Equal(ArtworkRenderer.PosterWidth, bmp.Width);
        Assert.Equal(ArtworkRenderer.PosterHeight, bmp.Height);
    }

    [Fact]
    public void WideImageIsTheRightSize()
    {
        var bytes = ArtworkRenderer.Wide("Christmas Movies", "4 movies", FakePosters(4), ArtworkRenderer.Accent("Christmas Movies", null), "Mosaic");
        using var bmp = Decode(bytes);
        Assert.Equal(ArtworkRenderer.WideWidth, bmp.Width);
        Assert.Equal(ArtworkRenderer.WideHeight, bmp.Height);
    }

    [Fact]
    public void MissingOrBrokenImagesAreSkippedNotFatal()
    {
        var bad = Path.Combine(Path.GetTempPath(), "not-an-image.jpg");
        File.WriteAllText(bad, "nope");
        var bytes = ArtworkRenderer.Poster("X", "", new[] { bad, "/does/not/exist.jpg" }, ArtworkRenderer.Accent("X", null), "Mosaic");
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void AccentIsStableAndCustomColourWins()
    {
        Assert.Equal(ArtworkRenderer.Accent("Comedy Movies", null), ArtworkRenderer.Accent("Comedy Movies", null));
        Assert.NotEqual(ArtworkRenderer.Accent("Comedy Movies", null), ArtworkRenderer.Accent("Horror Movies", null));
        Assert.Equal(new SKColor(0xE5, 0x09, 0x14), ArtworkRenderer.Accent("Anything", "#E50914"));
    }

    [Fact]
    public void WritesSamplesForLookingAtWhenAskedTo()
    {
        var outDir = Environment.GetEnvironmentVariable("ORG_ART_OUT");
        if (string.IsNullOrEmpty(outDir)) return;
        Directory.CreateDirectory(outDir);
        foreach (var (name, n, style) in new[] { ("Filipino Movies", 6, "Mosaic"), ("Christmas Movies", 4, "Mosaic"), ("Action and Adventure Movies from the United States", 3, "Mosaic"), ("Documentary", 0, "Title"), ("Horror", 2, "Mosaic") })
        {
            var posters = FakePosters(n);
            var acc = ArtworkRenderer.Accent(name, null);
            File.WriteAllBytes(Path.Combine(outDir, $"poster-{name.Replace(' ', '_')}.jpg"), ArtworkRenderer.Poster(name, ArtworkRenderer.Subtitle(n + 6, "Movie"), posters, acc, style));
            File.WriteAllBytes(Path.Combine(outDir, $"wide-{name.Replace(' ', '_')}.jpg"), ArtworkRenderer.Wide(name, ArtworkRenderer.Subtitle(n + 6, "Movie"), posters, acc, style));
        }
    }
}

public class ArtworkHashAndGuardTests
{
    [Fact]
    public void HashChangesWhenTheContentsOrLookChange()
    {
        var a = Jellyfin.Plugin.Organizer.ArtworkService.Hash("X", "Mosaic", "", 5, new[] { "p1", "p2" }, new[] { "b1" });
        Assert.Equal(a, Jellyfin.Plugin.Organizer.ArtworkService.Hash("X", "Mosaic", "", 5, new[] { "p1", "p2" }, new[] { "b1" }));
        Assert.NotEqual(a, Jellyfin.Plugin.Organizer.ArtworkService.Hash("X", "Mosaic", "", 6, new[] { "p1", "p2" }, new[] { "b1" }));
        Assert.NotEqual(a, Jellyfin.Plugin.Organizer.ArtworkService.Hash("X", "Title", "", 5, new[] { "p1", "p2" }, new[] { "b1" }));
        Assert.NotEqual(a, Jellyfin.Plugin.Organizer.ArtworkService.Hash("X", "Mosaic", "#fff", 5, new[] { "p1", "p2" }, new[] { "b1" }));
        Assert.NotEqual(a, Jellyfin.Plugin.Organizer.ArtworkService.Hash("X", "Mosaic", "", 5, new[] { "p1", "p3" }, new[] { "b1" }));
    }

    [Fact]
    public void OldPageDoesNotResetArtworkSettings()
    {
        var existing = new Jellyfin.Plugin.Organizer.Configuration.PluginConfiguration
        {
            ArtworkMode = "Off", ArtworkStyle = "Title",
            Rules = new() { new Jellyfin.Plugin.Organizer.Configuration.CollectionRule { Name = "R", KeepOwnArtwork = true, AccentColor = "#112233", MinFieldMatches = 2 } }
        };
        var incoming = new Jellyfin.Plugin.Organizer.Configuration.PluginConfiguration
        {
            PageVersion = 2,
            Rules = new() { new Jellyfin.Plugin.Organizer.Configuration.CollectionRule { Name = "R", MinFieldMatches = 3 } }
        };
        Jellyfin.Plugin.Organizer.Configuration.PluginConfiguration.KeepNewerFields(incoming, existing);
        Assert.True(incoming.Rules[0].KeepOwnArtwork);
        Assert.Equal("#112233", incoming.Rules[0].AccentColor);
        Assert.Equal("Off", incoming.ArtworkMode);
        // A v2 page legitimately sends MinFieldMatches, so that one is kept as the user set it.
        Assert.Equal(3, incoming.Rules[0].MinFieldMatches);
    }
}
