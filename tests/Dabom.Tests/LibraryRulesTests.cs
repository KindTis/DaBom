using Dabom.Library;

namespace Dabom.Tests;

[TestClass]
public sealed class LibraryRulesTests
{
    [TestMethod]
    public void DisplayTitle_UsesFileNameWhenMetadataTitleIsBlank()
    {
        var record = new VideoRecord { Title = "   " };

        Assert.AreEqual("Sample Movie", LibraryRules.DisplayTitle(@"D:\Movies\Sample Movie.mkv", record));
    }

    [TestMethod]
    public void Matches_NormalizesNfkcAndSearchesAllRequiredFields()
    {
        var record = new VideoRecord
        {
            Title = "ＤＵＮＥ",
            OriginalTitle = "Dune: Part Two",
            Director = "드니 빌뇌브",
            Actors = ["젠데이아"]
        };

        Assert.IsTrue(LibraryRules.Matches(@"D:\Movies\dune-part-two.mkv", record, "dune"));
        Assert.IsTrue(LibraryRules.Matches(@"D:\Movies\dune-part-two.mkv", record, "젠데이아"));
        Assert.IsFalse(LibraryRules.Matches(@"D:\Movies\dune-part-two.mkv", record, "놀란"));
    }

    [TestMethod]
    public void SelectFeatured_PrefersUnplayedThenOldestPlayed()
    {
        var records = new Dictionary<string, VideoRecord>(StringComparer.OrdinalIgnoreCase)
        {
            [@"D:\a.mkv"] = new() { LastPlayedUtc = DateTimeOffset.Parse("2026-01-02T00:00:00Z") },
            [@"D:\b.mkv"] = new(),
            [@"D:\c.mkv"] = new() { LastPlayedUtc = DateTimeOffset.Parse("2025-01-01T00:00:00Z") }
        };

        Assert.AreEqual(@"D:\b.mkv", LibraryRules.SelectFeaturedPath(records.Keys, records, _ => 0));

        records[@"D:\b.mkv"] = records[@"D:\b.mkv"] with
        {
            LastPlayedUtc = DateTimeOffset.Parse("2026-02-01T00:00:00Z")
        };
        Assert.AreEqual(@"D:\c.mkv", LibraryRules.SelectFeaturedPath(records.Keys, records, _ => 0));
    }

    [TestMethod]
    public void DurationText_UsesCumulativeHoursAndMissingFallback()
    {
        Assert.AreEqual("25시간 0분", LibraryRules.DurationText(TimeSpan.FromHours(25).Ticks));
        Assert.AreEqual("42분", LibraryRules.DurationText(TimeSpan.FromMinutes(42).Ticks));
        Assert.AreEqual("—", LibraryRules.DurationText(null));
    }
}
