using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Emby.Naming.Audio;
using Emby.Naming.Common;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using AudioItem = MediaBrowser.Controller.Entities.Audio.Audio;

namespace Emby.Server.Implementations.Library.Resolvers.Audio;

/// <summary>
/// Resolves valid CUE sheets into virtual audio tracks.
/// </summary>
public sealed class CueSheetAudioResolver : ItemResolver<AudioItem>, IMultiItemResolver
{
    private readonly ILogger<CueSheetAudioResolver> _logger;
    private readonly NamingOptions _namingOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="CueSheetAudioResolver"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="namingOptions">The naming options.</param>
    public CueSheetAudioResolver(ILogger<CueSheetAudioResolver> logger, NamingOptions namingOptions)
    {
        _logger = logger;
        _namingOptions = namingOptions;
    }

    /// <inheritdoc />
    public override ResolverPriority Priority => ResolverPriority.Fourth;

    /// <inheritdoc />
    public MultiItemResolverResult ResolveMultiple(
        Folder parent,
        List<FileSystemMetadata> files,
        CollectionType? collectionType,
        IDirectoryService directoryService)
    {
        if (collectionType != CollectionType.music)
        {
            return new MultiItemResolverResult();
        }

        var cueSheets = new List<CueSheet>();
        var sidecarReferencedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cuePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cueFile in files.Where(i => !i.IsDirectory && Path.GetExtension(i.FullName.AsSpan()).Equals(".cue", StringComparison.OrdinalIgnoreCase)))
        {
            var cueSheet = CueSheetParser.TryReadSidecar(cueFile.FullName, _logger);
            if (cueSheet is null)
            {
                continue;
            }

            cueSheets.Add(cueSheet);
            cuePaths.Add(cueFile.FullName);
            sidecarReferencedFiles.UnionWith(cueSheet.ReferencedFiles);
        }

        foreach (var audioFile in files.Where(i => !i.IsDirectory && AudioFileParser.IsAudioFile(i.FullName, _namingOptions) && !sidecarReferencedFiles.Contains(i.FullName)))
        {
            if (Path.GetExtension(audioFile.FullName.AsSpan()).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cueSheet = CueSheetParser.TryReadEmbedded(audioFile.FullName, _logger);
            if (cueSheet is null)
            {
                continue;
            }

            cueSheets.Add(cueSheet);
            sidecarReferencedFiles.UnionWith(cueSheet.ReferencedFiles);
        }

        if (cueSheets.Count == 0)
        {
            return new MultiItemResolverResult();
        }

        var result = new MultiItemResolverResult
        {
            Items = cueSheets.SelectMany(CreateTracks).Cast<BaseItem>().ToList(),
            ExtraFiles = files
                .Where(i => i.IsDirectory || (!cuePaths.Contains(i.FullName) && !sidecarReferencedFiles.Contains(i.FullName)))
                .ToList()
        };

        return result;
    }

    /// <inheritdoc />
    protected override AudioItem? Resolve(ItemResolveArgs args)
    {
        return null;
    }

    private IEnumerable<AudioItem> CreateTracks(CueSheet cueSheet)
    {
        foreach (var track in cueSheet.Tracks)
        {
            var mediaInfo = new FileInfo(track.MediaPath);
            var item = new AudioItem
            {
                Path = cueSheet.SheetPath + "#cue-track=" + track.Number.ToString("00", CultureInfo.InvariantCulture),
                IsInMixedFolder = true,
                Name = track.Title ?? "Track " + track.Number.ToString(CultureInfo.InvariantCulture),
                Album = cueSheet.Title,
                Artists = string.IsNullOrWhiteSpace(track.Performer) ? [] : [track.Performer],
                AlbumArtists = string.IsNullOrWhiteSpace(cueSheet.Performer) ? [] : [cueSheet.Performer],
                IndexNumber = track.Number,
                ProductionYear = cueSheet.Year,
                Genres = string.IsNullOrWhiteSpace(cueSheet.Genre) ? [] : [cueSheet.Genre],
                RunTimeTicks = track.EndTicks - track.StartTicks,
                Size = mediaInfo.Exists ? mediaInfo.Length : null,
                CueSheetPath = cueSheet.SheetPath,
                CueMediaSourcePath = track.MediaPath,
                CueTrackNumber = track.Number,
                CueStartTicks = track.StartTicks,
                CueEndTicks = track.EndTicks,
                CuePregapTicks = track.PregapTicks
            };

            if (!string.IsNullOrWhiteSpace(track.Isrc))
            {
                item.ProviderIds["ISRC"] = track.Isrc;
            }

            yield return item;
        }
    }
}
