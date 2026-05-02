using System;
using System.IO;
using Emby.Server.Implementations.Library.Resolvers.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Library.Resolvers.Audio;

public sealed class CueSheetParserTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "jellyfin-cue-tests-" + Path.GetRandomFileName());

    public CueSheetParserTests()
    {
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public void TryParse_ValidSingleFileCue_ReturnsVirtualTracks()
    {
        var mediaPath = Path.Combine(_testDirectory, "album.wav");
        WriteSilentWav(mediaPath, TimeSpan.FromSeconds(6));
        var cuePath = Path.Combine(_testDirectory, "album.cue");
        var cue = """
            REM GENRE "Industrial Metal"
            REM DATE 2019
            PERFORMER "Artist"
            TITLE "Album"
            FILE "album.wav" WAVE
              TRACK 01 AUDIO
                TITLE "One"
                PERFORMER "Artist"
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                TITLE "Two"
                PREGAP 00:00:10
                INDEX 00 00:02:50
                INDEX 01 00:03:00
            """;

        var result = CueSheetParser.TryParse(cue, cuePath, _testDirectory, null, NullLogger.Instance);

        Assert.NotNull(result);
        Assert.Equal("Album", result.Title);
        Assert.Equal("Artist", result.Performer);
        Assert.Equal(2019, result.Year);
        Assert.Equal("Industrial Metal", result.Genre);
        Assert.Equal(2, result.Tracks.Count);
        Assert.Equal("One", result.Tracks[0].Title);
        Assert.Equal((2 * TimeSpan.TicksPerSecond) + (50 * (TimeSpan.TicksPerSecond / 75)), result.Tracks[0].EndTicks);
        Assert.Equal("Two", result.Tracks[1].Title);
        Assert.Equal(3 * TimeSpan.TicksPerSecond, result.Tracks[1].StartTicks);
        Assert.Equal(10 * (TimeSpan.TicksPerSecond / 75), result.Tracks[1].PregapTicks);
    }

    [Fact]
    public void TryParse_MissingReferencedFile_ReturnsNull()
    {
        var cuePath = Path.Combine(_testDirectory, "missing.cue");
        var cue = """
            PERFORMER "3Teeth"
            TITLE "Metawar"
            FILE "01 - Hyperstition.wav" WAVE
              TRACK 01 AUDIO
                TITLE "Hyperstition"
                INDEX 01 00:00:00
            """;

        var result = CueSheetParser.TryParse(cue, cuePath, _testDirectory, null, NullLogger.Instance);

        Assert.Null(result);
    }

    [Fact]
    public void TryParse_EmbeddedCueThatReferencesDifferentFile_ReturnsNull()
    {
        var mediaPath = Path.Combine(_testDirectory, "album.wav");
        WriteSilentWav(mediaPath, TimeSpan.FromSeconds(1));
        var cue = """
            FILE "other.wav" WAVE
              TRACK 01 AUDIO
                INDEX 01 00:00:00
            """;

        var result = CueSheetParser.TryParse(cue, mediaPath, _testDirectory, mediaPath, NullLogger.Instance);

        Assert.Null(result);
    }

    [Fact]
    public void TryParse_EmbeddedCueWithoutFile_UsesContainingFile()
    {
        var mediaPath = Path.Combine(_testDirectory, "embedded.wav");
        WriteSilentWav(mediaPath, TimeSpan.FromSeconds(2));
        var cue = """
            TRACK 01 AUDIO
              TITLE "Embedded"
              INDEX 01 00:00:00
            """;

        var result = CueSheetParser.TryParse(cue, mediaPath, _testDirectory, mediaPath, NullLogger.Instance);

        Assert.NotNull(result);
        Assert.Single(result.Tracks);
        Assert.Equal(mediaPath, result.Tracks[0].MediaPath);
        Assert.Equal("Embedded", result.Tracks[0].Title);
    }

    public void Dispose()
    {
        Directory.Delete(_testDirectory, true);
    }

    private static void WriteSilentWav(string path, TimeSpan duration)
    {
        const int sampleRate = 44100;
        const short channels = 1;
        const short bitsPerSample = 16;
        var sampleCount = (int)(sampleRate * duration.TotalSeconds);
        var dataSize = sampleCount * channels * (bitsPerSample / 8);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
    }
}
