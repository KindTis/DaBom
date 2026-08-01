using Dabom.Library;
using Dabom.Metadata;

namespace Dabom.Tests;

[TestClass]
public sealed class MediaFilenameParserTests
{
    [DataTestMethod]
    [DataRow(
        "007.No.Time.to.Die.2021.2160p.WEB-DL.DDP5.1.Atmos.HDR.HEVC-NOTIMETOCRY.mp4",
        MediaType.Movie, "007 No Time to Die", 2021, null, null)]
    [DataRow(
        "1917.2019.2160p.UHD.BluRay.x265.10bit.HDR.DTS-HD.MA.TrueHD.7.1.Atmos-SWTYBLZ.mp4",
        MediaType.Movie, "1917", 2019, null, null)]
    [DataRow("도깨비.E04.161209.720p-NEXT.mp4",
        MediaType.TvEpisode, "도깨비", null, 1, 4)]
    [DataRow("도깨비.E05.161216.720p-NEXT.mp4",
        MediaType.TvEpisode, "도깨비", null, 1, 5)]
    [DataRow(
        "Evangelion.3.33.You.Can.(Not).Redo.2012.1080p.BluRay.x264-CHD.mp4",
        MediaType.Movie, "Evangelion 3.33 You Can (Not) Redo", 2012, null, null)]
    [DataRow(
        "John.Wick.Chapter.4.2023.2160p.WEB-DL.DDP5.1.Atmos.DV.HDR10.h265-CMRG.mp4",
        MediaType.Movie, "John Wick Chapter 4", 2023, null, null)]
    [DataRow(
        "My.Movie.2024-NTb.mkv",
        MediaType.Movie, "My Movie", 2024, null, null)]
    [DataRow(
        "Nobody.2.2025.Hybrid.2160p.WEB-DL.DV.HDR.DDP5.1.Atmos.H265-AOC.mkv",
        MediaType.Movie, "Nobody 2", 2025, null, null)]
    [DataRow(
        "Thor.Ragnarok.2017.IMAX.2160p.DSNP.WEB-DL.x265.10bit.HDR.DTS-HD.MA.TrueHD.7.1.Atmos-SWTYBLZ.mkv",
        MediaType.Movie, "Thor Ragnarok", 2017, null, null)]
    [DataRow(
        "Underworld.2003.UNRATED.1080p.BluRay.x264.DTS-FGT.mkv",
        MediaType.Movie, "Underworld", 2003, null, null)]
    [DataRow(
        "The.Mandalorian.S02E01.Chapter.16.The.Rescue.2160p.WEB-DL.DDP5.1.Atmos.HDR.x265-MZABI.mkv",
        MediaType.TvEpisode, "The Mandalorian", null, 2, 1)]
    [DataRow(
        "The.Mandalorian.S02E02.Chapter.16.The.Rescue.2160p.WEB-DL.DDP5.1.Atmos.HDR.x265-MZABI.mkv",
        MediaType.TvEpisode, "The Mandalorian", null, 2, 2)]
    public void Parse_ExtractsExpectedQuery(
        string fileName,
        MediaType mediaType,
        string title,
        int? year,
        int? season,
        int? episode)
    {
        var actual = new MediaFilenameParser().Parse(fileName);

        Assert.IsNotNull(actual);
        Assert.AreEqual(mediaType, actual.MediaType);
        Assert.AreEqual(title, actual.Title);
        Assert.AreEqual(year, actual.Year);
        Assert.AreEqual(season, actual.SeasonNumber);
        Assert.AreEqual(episode, actual.EpisodeNumber);
    }

    [TestMethod]
    public void Parse_WhenStemHasNoUnicodeLetterOrDigit_ReturnsNull()
    {
        Assert.IsNull(new MediaFilenameParser().Parse(@"D:\Movies\._-().mkv"));
    }
}
