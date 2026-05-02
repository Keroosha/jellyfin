using System;
using System.Globalization;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Controller.Tests.MediaEncoding
{
    public class EncodingHelperTests
    {
        [Fact]
        public void GetCueRemainingTicks_NotCueMediaSource_ReturnsNull()
        {
            var mediaSource = new MediaSourceInfo();

            var result = EncodingHelper.GetCueRemainingTicks(mediaSource, TimeSpan.FromSeconds(3).Ticks);

            Assert.Null(result);
        }

        [Fact]
        public void GetCueRemainingTicks_StartInsideCueTrack_ReturnsRemainingTicks()
        {
            var mediaSource = new MediaSourceInfo
            {
                CueStartPositionTicks = TimeSpan.FromSeconds(3).Ticks,
                CueEndPositionTicks = TimeSpan.FromSeconds(9).Ticks
            };

            var result = EncodingHelper.GetCueRemainingTicks(mediaSource, TimeSpan.FromSeconds(5).Ticks);

            Assert.Equal(TimeSpan.FromSeconds(4).Ticks, result);
        }

        [Theory]
        [InlineData(9)]
        [InlineData(12)]
        public void GetCueRemainingTicks_StartAtOrAfterCueTrackEnd_ReturnsZero(int startSeconds)
        {
            var mediaSource = new MediaSourceInfo
            {
                CueStartPositionTicks = TimeSpan.FromSeconds(3).Ticks,
                CueEndPositionTicks = TimeSpan.FromSeconds(9).Ticks
            };

            var result = EncodingHelper.GetCueRemainingTicks(mediaSource, TimeSpan.FromSeconds(startSeconds).Ticks);

            Assert.Equal(0, result);
        }

        [Fact]
        public void GetCueRemainingTicks_NullStart_UsesCueStart()
        {
            var mediaSource = new MediaSourceInfo
            {
                CueStartPositionTicks = TimeSpan.FromSeconds(3).Ticks,
                CueEndPositionTicks = TimeSpan.FromSeconds(9).Ticks
            };

            var result = EncodingHelper.GetCueRemainingTicks(mediaSource, null);

            Assert.Equal(TimeSpan.FromSeconds(6).Ticks, result);
        }

        [Theory]
        [InlineData(5, "00:00:05.000", "00:00:04.000")]
        [InlineData(9, "00:00:09.000", "00:00:00.000")]
        [InlineData(12, "00:00:12.000", "00:00:00.000")]
        public void GetProgressiveAudioFullCommandLine_CueMediaSource_AddsClampedDuration(int startSeconds, string expectedSeek, string expectedDuration)
        {
            var helper = GetEncodingHelper();
            var state = new EncodingJobInfo(TranscodingJobType.Progressive)
            {
                BaseRequest = new BaseEncodingJobOptions
                {
                    StartTimeTicks = TimeSpan.FromSeconds(startSeconds).Ticks
                },
                MediaSource = new MediaSourceInfo
                {
                    Container = "flac",
                    CueStartPositionTicks = TimeSpan.FromSeconds(3).Ticks,
                    CueEndPositionTicks = TimeSpan.FromSeconds(9).Ticks
                },
                AudioStream = new MediaStream
                {
                    Codec = "flac"
                },
                OutputAudioCodec = "mp3",
                OutputContainer = "mp3"
            };

            var result = helper.GetProgressiveAudioFullCommandLine(state, new EncodingOptions(), "/tmp/out.mp3");

            Assert.Contains("-ss " + expectedSeek, result, StringComparison.Ordinal);
            Assert.Contains("-t " + expectedDuration, result, StringComparison.Ordinal);
        }

        private static EncodingHelper GetEncodingHelper()
        {
            var mediaEncoder = new Mock<IMediaEncoder>();
            mediaEncoder.Setup(e => e.EncoderVersion).Returns(new Version(8, 0));
            mediaEncoder.Setup(e => e.SupportsEncoder(It.IsAny<string>())).Returns(false);
            mediaEncoder.Setup(e => e.GetInputPathArgument(It.IsAny<EncodingJobInfo>())).Returns("\"/tmp/in.flac\"");
            mediaEncoder
                .Setup(e => e.GetTimeParameter(It.IsAny<long>()))
                .Returns<long>(ticks => TimeSpan.FromTicks(ticks).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));

            var configuration = new Mock<IConfiguration>();
            return new EncodingHelper(
                Mock.Of<MediaBrowser.Common.Configuration.IApplicationPaths>(),
                mediaEncoder.Object,
                Mock.Of<ISubtitleEncoder>(),
                configuration.Object,
                Mock.Of<MediaBrowser.Common.Configuration.IConfigurationManager>(),
                Mock.Of<IPathManager>());
        }
    }
}
