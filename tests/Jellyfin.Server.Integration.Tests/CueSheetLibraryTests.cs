using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Api.Models.LibraryStructureDto;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Xunit;

namespace Jellyfin.Server.Integration.Tests;

public sealed class CueSheetLibraryTests : IClassFixture<JellyfinApplicationFactory>, IDisposable
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "jellyfin-cue-integration-" + Path.GetRandomFileName());
    private static string? _accessToken;

    public CueSheetLibraryTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public async Task MusicLibrary_WithValidCue_ExposesVirtualAudioTracks()
    {
        var mediaPath = Path.Combine(_testDirectory, "album.wav");
        WriteSilentWav(mediaPath, TimeSpan.FromSeconds(6));
        await File.WriteAllTextAsync(
            Path.Combine(_testDirectory, "album.cue"),
            """
            PERFORMER "Cue Artist"
            TITLE "Cue Album"
            FILE "album.wav" WAVE
              TRACK 01 AUDIO
                TITLE "First Cue Track"
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                TITLE "Second Cue Track"
                INDEX 01 00:03:00
            """,
            TestContext.Current.CancellationToken);

        var invalidFolder = Path.Combine(_testDirectory, "invalid");
        Directory.CreateDirectory(invalidFolder);
        WriteSilentWav(Path.Combine(invalidFolder, "01 - Normal.wav"), TimeSpan.FromSeconds(1));
        await File.WriteAllTextAsync(
            Path.Combine(invalidFolder, "broken.cue"),
            """
            TITLE "Broken"
            FILE "missing.wav" WAVE
              TRACK 01 AUDIO
                TITLE "Should Not Exist"
                INDEX 01 00:00:00
            """,
            TestContext.Current.CancellationToken);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));
        var libraryName = "Cue Test " + Path.GetFileName(_testDirectory);

        try
        {
            var body = new AddVirtualFolderDto
            {
                LibraryOptions = new LibraryOptions
                {
                    Enabled = true,
                    PathInfos = [new MediaPathInfo(_testDirectory)]
                }
            };

            using var createResponse = await client.PostAsJsonAsync(
                $"Library/VirtualFolders?name={Uri.EscapeDataString(libraryName)}&collectionType=music&refreshLibrary=true",
                body,
                _jsonOptions,
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, createResponse.StatusCode);

            var userDto = await AuthHelper.GetUserDtoAsync(client);
            var items = await WaitForAudioItems(client, userDto.Id, expectedMinimumCount: 3);

            Assert.Contains(items, i => string.Equals(i.Name, "First Cue Track", StringComparison.Ordinal));
            Assert.Contains(items, i => string.Equals(i.Name, "Second Cue Track", StringComparison.Ordinal));
            Assert.Contains(items, i => string.Equals(i.Name, "01 - Normal", StringComparison.Ordinal));
            Assert.DoesNotContain(items, i => string.Equals(i.Name, "album", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(items, i => string.Equals(i.Name, "Should Not Exist", StringComparison.Ordinal));

            var cueTrackIds = items
                .Where(i => string.Equals(i.Name, "First Cue Track", StringComparison.Ordinal)
                    || string.Equals(i.Name, "Second Cue Track", StringComparison.Ordinal))
                .Select(i => i.Id.ToString("N", CultureInfo.InvariantCulture));
            using var playbackItemsResponse = await client.GetAsync(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Users/{0}/Items?Ids={1}&ExcludeLocationTypes=Virtual&EnableTotalRecordCount=false",
                    userDto.Id,
                    string.Join(',', cueTrackIds)),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, playbackItemsResponse.StatusCode);
            var playbackItems = await playbackItemsResponse.Content.ReadFromJsonAsync<QueryResult<BaseItemDto>>(_jsonOptions, TestContext.Current.CancellationToken);
            Assert.Equal(2, playbackItems?.Items.Count);
        }
        finally
        {
            using var deleteResponse = await client.DeleteAsync(
                $"Library/VirtualFolders?name={Uri.EscapeDataString(libraryName)}&refreshLibrary=false",
                TestContext.Current.CancellationToken);
            Assert.True(deleteResponse.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound);
        }
    }

    public void Dispose()
    {
        Directory.Delete(_testDirectory, true);
    }

    private async Task<BaseItemDto[]> WaitForAudioItems(HttpClient client, Guid userId, int expectedMinimumCount)
    {
        for (var i = 0; i < 30; i++)
        {
            using var response = await client.GetAsync(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Items?userId={0}&Recursive=true&IncludeItemTypes=Audio",
                    userId),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var queryResult = await response.Content.ReadFromJsonAsync<QueryResult<BaseItemDto>>(_jsonOptions, TestContext.Current.CancellationToken);
            var items = queryResult?.Items ?? [];
            if (items.Count >= expectedMinimumCount)
            {
                return items.ToArray();
            }

            await Task.Delay(1000, TestContext.Current.CancellationToken);
        }

        return [];
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
