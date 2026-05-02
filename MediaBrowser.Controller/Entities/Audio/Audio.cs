#nullable disable

#pragma warning disable CA1002, CA1724, CA1826, CS1591

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace MediaBrowser.Controller.Entities.Audio
{
    /// <summary>
    /// Class Audio.
    /// </summary>
    public class Audio : BaseItem,
        IHasAlbumArtist,
        IHasArtist,
        IHasMusicGenres,
        IHasLookupInfo<SongInfo>,
        IHasMediaSources
    {
        public Audio()
        {
            Artists = Array.Empty<string>();
            AlbumArtists = Array.Empty<string>();
            LyricFiles = Array.Empty<string>();
        }

        /// <inheritdoc />
        [JsonIgnore]
        public IReadOnlyList<string> Artists { get; set; }

        /// <inheritdoc />
        [JsonIgnore]
        public IReadOnlyList<string> AlbumArtists { get; set; }

        [JsonIgnore]
        public override bool SupportsPlayedStatus => true;

        [JsonIgnore]
        public override bool SupportsPeople => true;

        [JsonIgnore]
        public override bool SupportsAddingToPlaylist => true;

        [JsonIgnore]
        public override bool SupportsInheritedParentImages => true;

        [JsonIgnore]
        protected override bool SupportsOwnedItems => false;

        [JsonIgnore]
        public override Folder LatestItemsIndexContainer => AlbumEntity;

        [JsonIgnore]
        public MusicAlbum AlbumEntity => FindParent<MusicAlbum>();

        /// <summary>
        /// Gets the type of the media.
        /// </summary>
        /// <value>The type of the media.</value>
        [JsonIgnore]
        public override MediaType MediaType => MediaType.Audio;

        /// <summary>
        /// Gets or sets a value indicating whether this audio has lyrics.
        /// </summary>
        public bool? HasLyrics { get; set; }

        /// <summary>
        /// Gets or sets the list of lyric paths.
        /// </summary>
        public IReadOnlyList<string> LyricFiles { get; set; }

        /// <summary>
        /// Gets or sets the path to the CUE file that defined this virtual track.
        /// </summary>
        public string CueSheetPath { get; set; }

        /// <summary>
        /// Gets or sets the physical media file used to play this virtual CUE track.
        /// </summary>
        public string CueMediaSourcePath { get; set; }

        /// <summary>
        /// Gets or sets the 1-based CUE track number.
        /// </summary>
        public int? CueTrackNumber { get; set; }

        /// <summary>
        /// Gets or sets the CUE track start offset within <see cref="CueMediaSourcePath"/>.
        /// </summary>
        public long? CueStartTicks { get; set; }

        /// <summary>
        /// Gets or sets the CUE track end offset within <see cref="CueMediaSourcePath"/>.
        /// </summary>
        public long? CueEndTicks { get; set; }

        /// <summary>
        /// Gets or sets the CUE pregap duration for this virtual track.
        /// </summary>
        public long? CuePregapTicks { get; set; }

        [JsonIgnore]
        public bool IsCueTrack => !string.IsNullOrWhiteSpace(CueMediaSourcePath)
            && CueStartTicks.HasValue
            && CueEndTicks.HasValue;

        public override double GetDefaultPrimaryImageAspectRatio()
        {
            return 1;
        }

        public override bool CanDownload()
        {
            return IsFileProtocol;
        }

        /// <summary>
        /// Creates the name of the sort.
        /// </summary>
        /// <returns>System.String.</returns>
        protected override string CreateSortName()
        {
            return (ParentIndexNumber is not null ? ParentIndexNumber.Value.ToString("0000 - ", CultureInfo.InvariantCulture) : string.Empty)
                    + (IndexNumber is not null ? IndexNumber.Value.ToString("0000 - ", CultureInfo.InvariantCulture) : string.Empty) + Name;
        }

        public override List<string> GetUserDataKeys()
        {
            var list = base.GetUserDataKeys();

            var songKey = IndexNumber.HasValue ? IndexNumber.Value.ToString("0000", CultureInfo.InvariantCulture) : string.Empty;

            if (ParentIndexNumber.HasValue)
            {
                songKey = ParentIndexNumber.Value.ToString("0000", CultureInfo.InvariantCulture) + "-" + songKey;
            }

            songKey += Name;

            if (!string.IsNullOrEmpty(Album))
            {
                songKey = Album + "-" + songKey;
            }

            var albumArtist = AlbumArtists.FirstOrDefault();
            if (!string.IsNullOrEmpty(albumArtist))
            {
                songKey = albumArtist + "-" + songKey;
            }

            list.Insert(0, songKey);

            return list;
        }

        public override UnratedItem GetBlockUnratedType()
        {
            if (SourceType == SourceType.Library)
            {
                return UnratedItem.Music;
            }

            return base.GetBlockUnratedType();
        }

        public SongInfo GetLookupInfo()
        {
            var info = GetItemLookupInfo<SongInfo>();

            info.AlbumArtists = AlbumArtists;
            info.Album = Album;
            info.Artists = Artists;

            return info;
        }

        protected override IEnumerable<(BaseItem Item, MediaSourceType MediaSourceType)> GetAllItemsForMediaSources()
            => new[] { ((BaseItem)this, MediaSourceType.Default) };

        public override IReadOnlyList<MediaSourceInfo> GetMediaSources(bool enablePathSubstitution)
        {
            if (!IsCueTrack)
            {
                return base.GetMediaSources(enablePathSubstitution);
            }

            var mediaSource = new MediaSourceInfo
            {
                Id = Id.ToString("N", CultureInfo.InvariantCulture),
                Protocol = MediaProtocol.File,
                MediaStreams = MediaSourceManager.GetMediaStreams(Id),
                MediaAttachments = MediaSourceManager.GetMediaAttachments(Id),
                Name = Name,
                Path = CueMediaSourcePath,
                RunTimeTicks = RunTimeTicks,
                Container = Container ?? System.IO.Path.GetExtension(CueMediaSourcePath).TrimStart('.'),
                Size = Size,
                Type = MediaSourceType.Default,
                Bitrate = TotalBitrate,
                SupportsDirectPlay = false,
                SupportsDirectStream = false,
                SupportsTranscoding = true,
                CueStartPositionTicks = CueStartTicks,
                CueEndPositionTicks = CueEndTicks
            };

            mediaSource.InferTotalBitrate();

            return new[] { mediaSource };
        }
    }
}
