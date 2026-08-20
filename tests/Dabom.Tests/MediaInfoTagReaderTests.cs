using Dabom.Library;

namespace Dabom.Tests;

[TestClass]
public sealed class MediaInfoTagReaderTests
{
    [TestMethod]
    public void ParseTags_ExtractsMeaningfulVideoAndAudioSpecs()
    {
        var tags = MediaInfoTagReader.ParseTags(
            """
            {
              "media": {
                "track": [
                  {
                    "@type": "Video",
                    "Format": "HEVC",
                    "Width": "3840",
                    "Height": "2160",
                    "HDR_Format": "Dolby Vision / SMPTE ST 2086",
                    "HDR_Format_Compatibility": "HDR10 / HDR10"
                  },
                  {
                    "@type": "Audio",
                    "Format": "MLP FBA",
                    "Format_Commercial_IfAny": "Dolby TrueHD with Dolby Atmos",
                    "Channels": "8",
                    "ChannelLayout": "L R C LFE Ls Rs Lb Rb"
                  }
                ]
              }
            }
            """);

        CollectionAssert.AreEqual(
            new[]
            {
                "4K",
                "Dolby Vision",
                "HDR10",
                "Dolby TrueHD · Dolby Atmos · 7.1"
            },
            tags);
    }

    [TestMethod]
    public void ParseTags_InfersCommonChannelLayoutWhenLayoutIsMissing()
    {
        var tags = MediaInfoTagReader.ParseTags(
            """
            {
              "media": {
                "track": [
                  {
                    "@type": "Audio",
                    "Format": "AC-3",
                    "Channels": "6"
                  }
                ]
              }
            }
            """);

        CollectionAssert.AreEqual(new[] { "AC-3 · 5.1" }, tags);
    }

    [TestMethod]
    public void ParseTags_KeepsEachAudioFormatMatchedWithItsChannelLayout()
    {
        var tags = MediaInfoTagReader.ParseTags(
            """
            {
              "media": {
                "track": [
                  {
                    "@type": "Video",
                    "Format": "HEVC",
                    "Width": "3840",
                    "Height": "2160"
                  },
                  {
                    "@type": "Audio",
                    "Format_Commercial_IfAny": "Dolby TrueHD with Dolby Atmos",
                    "Channels": "8",
                    "ChannelLayout": "L R C LFE Ls Rs Lb Rb"
                  },
                  {
                    "@type": "Audio",
                    "Format": "AAC",
                    "Channels": "2",
                    "ChannelLayout": "L R"
                  }
                ]
              }
            }
            """);

        CollectionAssert.AreEqual(
            new[]
            {
                "4K",
                "Dolby TrueHD · Dolby Atmos · 7.1",
                "AAC · 2.0"
            },
            tags);
    }
}
