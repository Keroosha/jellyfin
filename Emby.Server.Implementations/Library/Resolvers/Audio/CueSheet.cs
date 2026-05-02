using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ATL;
using Microsoft.Extensions.Logging;

#pragma warning disable SA1402

namespace Emby.Server.Implementations.Library.Resolvers.Audio;

internal sealed class CueSheet
{
    public CueSheet(
        string sheetPath,
        string? title,
        string? performer,
        int? year,
        string? genre,
        IReadOnlyList<CueSheetTrack> tracks,
        IReadOnlySet<string> referencedFiles)
    {
        SheetPath = sheetPath;
        Title = title;
        Performer = performer;
        Year = year;
        Genre = genre;
        Tracks = tracks;
        ReferencedFiles = referencedFiles;
    }

    public string SheetPath { get; }

    public string? Title { get; }

    public string? Performer { get; }

    public int? Year { get; }

    public string? Genre { get; }

    public IReadOnlyList<CueSheetTrack> Tracks { get; }

    public IReadOnlySet<string> ReferencedFiles { get; }
}

internal sealed class CueSheetTrack
{
    public required int Number { get; init; }

    public required string MediaPath { get; init; }

    public required long StartTicks { get; init; }

    public required long EndTicks { get; init; }

    public long? PregapTicks { get; init; }

    public string? Title { get; init; }

    public string? Performer { get; init; }

    public string? Isrc { get; init; }
}

internal static class CueSheetParser
{
    private const long TicksPerFrame = TimeSpan.TicksPerSecond / 75;
    private static readonly char[] _quoteTrimChars = [' ', '\t', '"'];

    public static CueSheet? TryReadSidecar(string cuePath, ILogger logger)
    {
        try
        {
            return TryParse(File.ReadAllText(cuePath), cuePath, Path.GetDirectoryName(cuePath) ?? string.Empty, null, logger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to read CUE sheet {CuePath}", cuePath);
            return null;
        }
    }

    public static CueSheet? TryReadEmbedded(string mediaPath, ILogger logger)
    {
        try
        {
            var track = new Track(mediaPath);
            var cueText = track.AdditionalFields
                .FirstOrDefault(i => string.Equals(i.Key, "CUESHEET", StringComparison.OrdinalIgnoreCase))
                .Value;
            if (string.IsNullOrWhiteSpace(cueText))
            {
                return null;
            }

            return TryParse(cueText, mediaPath, Path.GetDirectoryName(mediaPath) ?? string.Empty, mediaPath, logger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogWarning(ex, "Unable to read embedded CUE sheet from {MediaPath}", mediaPath);
            return null;
        }
    }

    public static CueSheet? TryParse(string cueText, string sheetPath, string basePath, string? embeddedMediaPath, ILogger logger)
    {
        var albumTitle = default(string);
        var albumPerformer = default(string);
        var albumYear = default(int?);
        var albumGenre = default(string);
        var currentFile = default(string);
        var parsedTracks = new List<ParsedTrack>();
        var currentTrack = default(ParsedTrack);

        foreach (var rawLine in cueText.Replace("\uFEFF", string.Empty, StringComparison.Ordinal).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var (command, value) = SplitCommand(line);
            if (command.Length == 0)
            {
                continue;
            }

            if (string.Equals(command, "REM", StringComparison.OrdinalIgnoreCase))
            {
                var (remCommand, remValue) = SplitCommand(value);
                if (string.Equals(remCommand, "DATE", StringComparison.OrdinalIgnoreCase))
                {
                    albumYear = ParseYear(remValue);
                }
                else if (string.Equals(remCommand, "GENRE", StringComparison.OrdinalIgnoreCase))
                {
                    albumGenre = Unquote(remValue);
                }

                continue;
            }

            if (string.Equals(command, "FILE", StringComparison.OrdinalIgnoreCase))
            {
                currentFile = ResolveFile(value, basePath);
                currentTrack = null;
                continue;
            }

            if (string.Equals(command, "TRACK", StringComparison.OrdinalIgnoreCase))
            {
                if (currentFile is null)
                {
                    if (embeddedMediaPath is null)
                    {
                        logger.LogWarning("Ignoring CUE sheet {CuePath}: TRACK appears before FILE", sheetPath);
                        return null;
                    }

                    currentFile = embeddedMediaPath;
                }

                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !string.Equals(parts[1], "AUDIO", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var trackNumber))
                {
                    currentTrack = null;
                    continue;
                }

                currentTrack = new ParsedTrack(trackNumber, currentFile);
                parsedTracks.Add(currentTrack);
                continue;
            }

            if (currentTrack is null)
            {
                if (string.Equals(command, "TITLE", StringComparison.OrdinalIgnoreCase))
                {
                    albumTitle = Unquote(value);
                }
                else if (string.Equals(command, "PERFORMER", StringComparison.OrdinalIgnoreCase))
                {
                    albumPerformer = Unquote(value);
                }

                continue;
            }

            if (string.Equals(command, "TITLE", StringComparison.OrdinalIgnoreCase))
            {
                currentTrack.Title = Unquote(value);
            }
            else if (string.Equals(command, "PERFORMER", StringComparison.OrdinalIgnoreCase))
            {
                currentTrack.Performer = Unquote(value);
            }
            else if (string.Equals(command, "ISRC", StringComparison.OrdinalIgnoreCase))
            {
                currentTrack.Isrc = Unquote(value);
            }
            else if (string.Equals(command, "PREGAP", StringComparison.OrdinalIgnoreCase) && TryParseTimecode(value, out var pregap))
            {
                currentTrack.PregapTicks = pregap;
            }
            else if (string.Equals(command, "INDEX", StringComparison.OrdinalIgnoreCase))
            {
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && TryParseTimecode(parts[1], out var ticks))
                {
                    if (string.Equals(parts[0], "00", StringComparison.Ordinal))
                    {
                        currentTrack.Index00Ticks = ticks;
                    }
                    else if (string.Equals(parts[0], "01", StringComparison.Ordinal))
                    {
                        currentTrack.Index01Ticks = ticks;
                    }
                }
            }
        }

        if (parsedTracks.Count == 0 || parsedTracks.Any(i => !i.Index01Ticks.HasValue))
        {
            logger.LogWarning("Ignoring CUE sheet {CuePath}: no playable AUDIO tracks with INDEX 01 were found", sheetPath);
            return null;
        }

        var referencedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in parsedTracks.Select(i => i.MediaPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(file))
            {
                logger.LogWarning("Ignoring CUE sheet {CuePath}: referenced file {MediaPath} does not exist", sheetPath, file);
                return null;
            }

            referencedFiles.Add(file);
        }

        if (embeddedMediaPath is not null && !referencedFiles.Contains(embeddedMediaPath))
        {
            logger.LogWarning("Ignoring embedded CUE sheet in {MediaPath}: it does not reference its containing file", embeddedMediaPath);
            return null;
        }

        var tracks = new List<CueSheetTrack>(parsedTracks.Count);
        for (var i = 0; i < parsedTracks.Count; i++)
        {
            var parsed = parsedTracks[i];
            var start = parsed.Index01Ticks!.Value;
            var nextInSameFile = parsedTracks.Skip(i + 1).FirstOrDefault(next => string.Equals(next.MediaPath, parsed.MediaPath, StringComparison.OrdinalIgnoreCase));
            var end = GetTrackEnd(nextInSameFile);
            if (end <= start)
            {
                end = GetMediaRuntimeTicks(parsed.MediaPath);
            }

            if (end <= start)
            {
                logger.LogWarning("Ignoring CUE sheet {CuePath}: track {TrackNumber} has an invalid duration", sheetPath, parsed.Number);
                return null;
            }

            tracks.Add(new CueSheetTrack
            {
                Number = parsed.Number,
                MediaPath = parsed.MediaPath,
                StartTicks = start,
                EndTicks = end,
                PregapTicks = parsed.PregapTicks,
                Title = parsed.Title,
                Performer = parsed.Performer,
                Isrc = parsed.Isrc
            });
        }

        return new CueSheet(sheetPath, albumTitle, albumPerformer, albumYear, albumGenre, tracks, referencedFiles);
    }

    private static long GetTrackEnd(ParsedTrack? next)
    {
        if (next is null)
        {
            return 0;
        }

        if (next.Index00Ticks.HasValue)
        {
            return next.Index00Ticks.Value;
        }

        var end = next.Index01Ticks!.Value;
        if (next.PregapTicks.HasValue)
        {
            end -= next.PregapTicks.Value;
        }

        return Math.Max(0, end);
    }

    private static long GetMediaRuntimeTicks(string mediaPath)
    {
        try
        {
            return TimeSpan.FromMilliseconds(new Track(mediaPath).DurationMs).Ticks;
        }
        catch
        {
            return 0;
        }
    }

    private static (string Command, string Value) SplitCommand(string line)
    {
        var index = line.IndexOfAny([' ', '\t']);
        return index == -1
            ? (line, string.Empty)
            : (line[..index], line[(index + 1)..].Trim());
    }

    private static int? ParseYear(string value)
    {
        var yearPart = Unquote(value)?.Split(['.', '-', '/'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(yearPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ? year : null;
    }

    private static string? Unquote(string value)
    {
        var result = value.Trim(_quoteTrimChars);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string ResolveFile(string value, string basePath)
    {
        var fileName = ExtractQuotedOrFirstToken(value);
        return Path.GetFullPath(Path.IsPathRooted(fileName) ? fileName : Path.Combine(basePath, fileName));
    }

    private static string ExtractQuotedOrFirstToken(string value)
    {
        value = value.Trim();
        if (value.StartsWith('"'))
        {
            var endQuote = value.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return value[1..endQuote];
            }
        }

        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    }

    private static bool TryParseTimecode(string value, out long ticks)
    {
        ticks = 0;
        var parts = value.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames)
            || seconds is < 0 or > 59
            || frames is < 0 or > 74)
        {
            return false;
        }

        ticks = (((minutes * 60L) + seconds) * TimeSpan.TicksPerSecond) + (frames * TicksPerFrame);
        return true;
    }

    private sealed class ParsedTrack
    {
        public ParsedTrack(int number, string mediaPath)
        {
            Number = number;
            MediaPath = mediaPath;
        }

        public int Number { get; }

        public string MediaPath { get; }

        public string? Title { get; set; }

        public string? Performer { get; set; }

        public string? Isrc { get; set; }

        public long? PregapTicks { get; set; }

        public long? Index00Ticks { get; set; }

        public long? Index01Ticks { get; set; }
    }
}
