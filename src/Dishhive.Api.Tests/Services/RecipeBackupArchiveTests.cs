using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Dishhive.Api.Services.Import;

namespace Dishhive.Api.Tests.Services;

public sealed class RecipeBackupArchiveTests
{
    [Fact]
    public async Task Zip_PreservesUnicode_AndCompressesText()
    {
        var json = "{\"name\":\"Crème brûlée\",\"description\":\"" + new string('a', 10000) + "\"}";
        var bytes = RecipeBackupArchive.Create(json);
        Assert.True(bytes.Length < Encoding.UTF8.GetByteCount(json) / 4);
        using var source = new MemoryStream(bytes);
        using var document = await RecipeBackupArchive.ReadDocumentAsync(source);
        Assert.Equal(json, Encoding.UTF8.GetString(document.ToArray()));
    }

    [Fact]
    public async Task PlainJson_RemainsSupported()
    {
        const string json = "{\"@type\":\"Recipe\",\"name\":\"Soup\"}";
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(json));
        using var document = await RecipeBackupArchive.ReadDocumentAsync(source);
        Assert.Equal(json, Encoding.UTF8.GetString(document.ToArray()));
    }

    [Theory]
    [InlineData("../recipes.json")]
    [InlineData("other.json")]
    public async Task Zip_RejectsUnexpectedDocumentPaths(string name)
    {
        using var input = new MemoryStream();
        using (var archive = new ZipArchive(input, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write("{}");
        }
        input.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => RecipeBackupArchive.ReadDocumentAsync(input));
    }

    [Fact]
    public async Task Zip_RejectsAmbiguousDuplicateDocuments()
    {
        using var input = new MemoryStream();
        using (var archive = new ZipArchive(input, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < 2; i++)
            {
                using var writer = new StreamWriter(archive.CreateEntry("recipes.json").Open());
                writer.Write("{}");
            }
        }
        input.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => RecipeBackupArchive.ReadDocumentAsync(input));
    }

    [Fact]
    public async Task Zip_RejectsOversizedExpandedDocumentBeforeReadingItsContent()
    {
        var zip = RecipeBackupArchive.Create("{}");
        var directory = zip.AsSpan().IndexOf(new byte[] { 0x50, 0x4b, 0x01, 0x02 });
        BinaryPrimitives.WriteInt32LittleEndian(zip.AsSpan(directory + 24), RecipeBackupArchive.MaxDocumentBytes + 1);
        using var input = new MemoryStream(zip);
        await Assert.ThrowsAsync<InvalidDataException>(() => RecipeBackupArchive.ReadDocumentAsync(input));
    }
}
