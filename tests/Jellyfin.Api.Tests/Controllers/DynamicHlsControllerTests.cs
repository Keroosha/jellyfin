using System;
using System.Globalization;
using System.Reflection;
using Jellyfin.Api.Controllers;
using Jellyfin.Api.Helpers;
using Jellyfin.MediaEncoding.Hls.Playlist;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers
{
    public class DynamicHlsControllerTests
    {
        [Theory]
        [MemberData(nameof(GetSegmentLengths_Success_TestData))]
        public void GetSegmentLengths_Success(long runtimeTicks, int segmentlength, double[] expected)
        {
            var res = DynamicHlsController.GetSegmentLengthsInternal(runtimeTicks, segmentlength);
            Assert.Equal(expected.Length, res.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], res[i]);
            }
        }

        public static TheoryData<long, int, double[]> GetSegmentLengths_Success_TestData()
        {
            var data = new TheoryData<long, int, double[]>();
            data.Add(0, 6, Array.Empty<double>());
            data.Add(
                TimeSpan.FromSeconds(3).Ticks,
                6,
                new double[] { 3 });
            data.Add(
                TimeSpan.FromSeconds(6).Ticks,
                6,
                new double[] { 6 });
            data.Add(
                TimeSpan.FromSeconds(3.3333333).Ticks,
                6,
                new double[] { 3.3333333 });
            data.Add(
                TimeSpan.FromSeconds(9.3333333).Ticks,
                6,
                new double[] { 6, 3.3333333 });

            return data;
        }

        [Theory]
        [InlineData(5, "00:00:05.000", "00:00:04.000")]
        [InlineData(9, "00:00:09.000", "00:00:00.000")]
        [InlineData(12, "00:00:12.000", "00:00:00.000")]
        public void GetCommandLineArguments_CueMediaSource_AddsClampedDurationAndOmitsCopyTimestamp(int startSeconds, string expectedSeek, string expectedDuration)
        {
            var mediaSourceManager = Mock.Of<IMediaSourceManager>();
            var transcodeManager = Mock.Of<ITranscodeManager>();
            var mediaEncoder = GetMediaEncoder();
            var encodingHelper = GetEncodingHelper(mediaEncoder);
            var controller = GetController(mediaSourceManager, transcodeManager, mediaEncoder, encodingHelper);
            var state = new StreamState(mediaSourceManager, TranscodingJobType.Hls, transcodeManager)
            {
                Request = new StreamingRequestDto
                {
                    SegmentContainer = "mp4",
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
                OutputAudioCodec = "copy"
            };

            var result = InvokeGetCommandLineArguments(controller, "/tmp/playlist.m3u8", state);

            Assert.Contains("-ss " + expectedSeek, result, StringComparison.Ordinal);
            Assert.Contains("-t " + expectedDuration, result, StringComparison.Ordinal);
            Assert.DoesNotContain("-copyts", result, StringComparison.Ordinal);
            Assert.DoesNotContain("-avoid_negative_ts", result, StringComparison.Ordinal);
        }

        private static string InvokeGetCommandLineArguments(DynamicHlsController controller, string outputPath, StreamState state)
        {
            var method = typeof(DynamicHlsController).GetMethod("GetCommandLineArguments", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return Assert.IsType<string>(method.Invoke(controller, new object[] { outputPath, state, false, 0 }));
        }

        private static DynamicHlsController GetController(
            IMediaSourceManager mediaSourceManager,
            ITranscodeManager transcodeManager,
            IMediaEncoder mediaEncoder,
            EncodingHelper encodingHelper)
        {
            var serverConfigurationManager = new Mock<IServerConfigurationManager>();
            serverConfigurationManager.Setup(c => c.GetConfiguration("encoding")).Returns(new EncodingOptions());

            return new DynamicHlsController(
                Mock.Of<ILibraryManager>(),
                Mock.Of<IUserManager>(),
                mediaSourceManager,
                serverConfigurationManager.Object,
                mediaEncoder,
                Mock.Of<IFileSystem>(),
                transcodeManager,
                Mock.Of<ILogger<DynamicHlsController>>(),
                null!,
                encodingHelper,
                Mock.Of<IDynamicHlsPlaylistGenerator>());
        }

        private static EncodingHelper GetEncodingHelper(IMediaEncoder mediaEncoder)
        {
            return new EncodingHelper(
                Mock.Of<MediaBrowser.Common.Configuration.IApplicationPaths>(),
                mediaEncoder,
                Mock.Of<ISubtitleEncoder>(),
                Mock.Of<IConfiguration>(),
                Mock.Of<MediaBrowser.Common.Configuration.IConfigurationManager>(),
                Mock.Of<IPathManager>());
        }

        private static IMediaEncoder GetMediaEncoder()
        {
            var mediaEncoder = new Mock<IMediaEncoder>();
            mediaEncoder.Setup(e => e.EncoderVersion).Returns(new Version(8, 0));
            mediaEncoder.Setup(e => e.SupportsEncoder(It.IsAny<string>())).Returns(false);
            mediaEncoder.Setup(e => e.GetInputPathArgument(It.IsAny<EncodingJobInfo>())).Returns("\"/tmp/in.flac\"");
            mediaEncoder
                .Setup(e => e.GetTimeParameter(It.IsAny<long>()))
                .Returns<long>(ticks => TimeSpan.FromTicks(ticks).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));

            return mediaEncoder.Object;
        }
    }
}
