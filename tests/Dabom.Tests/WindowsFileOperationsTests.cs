using Dabom.Library;
using System.IO;

namespace Dabom.Tests;

[TestClass]
public sealed class WindowsFileOperationsTests
{
    [TestMethod]
    public void Probe_SameFileTwice_ReturnsSameIdentity()
    {
        var root = Directory.CreateTempSubdirectory("dabom-file-id-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            File.WriteAllText(path, "original");

            var first = WindowsFileOperations.Probe(path);
            var second = WindowsFileOperations.Probe(path);

            Assert.AreEqual(VideoFileStatus.Present, first.Status);
            Assert.IsNotNull(first.Identity);
            Assert.AreEqual(first, second);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void Probe_ReplacementMovedOverPath_ReturnsReplacementIdentity()
    {
        var root = Directory.CreateTempSubdirectory("dabom-file-replace-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var replacement = Path.Combine(root.FullName, "Replacement.mkv");
            File.WriteAllText(path, "original");
            File.WriteAllText(replacement, "replacement");
            var original = WindowsFileOperations.Probe(path);
            var replacementBeforeMove = WindowsFileOperations.Probe(replacement);

            File.Move(replacement, path, true);
            var current = WindowsFileOperations.Probe(path);

            Assert.AreEqual(VideoFileStatus.Present, current.Status);
            Assert.AreNotEqual(original.Identity, current.Identity);
            Assert.AreEqual(replacementBeforeMove.Identity, current.Identity);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void Probe_MissingPath_ReturnsMissingWithoutIdentity()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"dabom-missing-{Guid.NewGuid():N}",
            "Movie.mkv");

        var result = WindowsFileOperations.Probe(path);

        Assert.AreEqual(VideoFileStatus.Missing, result.Status);
        Assert.IsNull(result.Identity);
    }
}
