using Markdown.Linter.Paths;

namespace Markdown.Linter.Test;

public class SuperiorPathUnitTest
{
    [Fact]
    public void Test1()
    {
        var path = SuperiorPath.Create("/aaa/bbb/ccc/testfile.md");
        var isRooted = path.IsRooted;
        var isFolder = path.IsFolder;
        var fullPath = path.FullPath;
        var elementCount = path.Count();
        var element1 = path.First();
        var fileName = path.FileName;
        Assert.True(isRooted);
        Assert.False(isFolder);
        Assert.Equal("testfile.md", fileName);
        Assert.Equal("/aaa/bbb/ccc/testfile.md", fullPath);
        Assert.Equal(4, elementCount);
    }
    
    [Theory]
    [InlineData("/a/readme.md", true, true)]
    [InlineData("a/readme.md", false, true)]
    [InlineData("a/b/", false, false)]
    public void TestRooted(string path, bool isRooted, bool isFile)
    {
        var sut = SuperiorPath.Create(path);
        Assert.Equal(isRooted, sut.IsRooted);
        Assert.Equal(isFile, sut.IsFile);
    }
    
    [Fact]
    
    public void TestPathCreation()
    {
        var sut = SuperiorPath.Root / "aaa" / "bbb" / "README.md";
        var fullPath = sut.FullPath;
        var fileName = sut.FileName;
        Assert.Equal("/aaa/bbb/README.md",  fullPath);
        Assert.Equal("README.md", fileName);
    }
}