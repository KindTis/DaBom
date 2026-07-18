using Dabom.Library;

namespace Dabom.Tests;

[TestClass]
public sealed class WindowsDurationReaderTests
{
    [TestMethod]
    public void PropVariant_HasExpectedWindowsAbiSize()
    {
        Assert.AreEqual(24, WindowsDurationReader.PropVariantSize);
    }
}
