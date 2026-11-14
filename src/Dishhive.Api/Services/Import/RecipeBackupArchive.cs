using System.IO.Compression;
using System.Text;

namespace Dishhive.Api.Services.Import;

/// <summary>
/// A portable ZIP containing recipes.json. Images remain schema.org data URIs, so
/// extracting the document also produces an ordinary, self-contained JSON backup.
/// No archive paths are ever extracted to the filesystem.
/// </summary>
public static class RecipeBackupArchive
{
    public const int MaxUploadBytes = 64 * 1024 * 1024;
    public const int MaxDocumentBytes = 256 * 1024 * 1024;
    public const string DocumentName = "recipes.json";

    public static byte[] Create(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxDocumentBytes)
            throw new InvalidDataException("The recipe library exceeds the backup document limit.");
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(DocumentName, CompressionLevel.SmallestSize);
            using var stream = entry.Open();
            stream.Write(bytes);
        }
        if (output.Length > MaxUploadBytes)
            throw new InvalidDataException("The recipe library exceeds the backup upload limit.");
        return output.ToArray();
    }

    public static async Task<MemoryStream> ReadDocumentAsync(
        Stream input, CancellationToken cancellationToken = default)
    {
        var buffer = await ReadBoundedAsync(input, MaxUploadBytes, cancellationToken);
        if (buffer.Length < 2 || buffer.GetBuffer()[0] != 'P' || buffer.GetBuffer()[1] != 'K')
            return buffer; // Direct JSON import, including non-seekable uploads.
        using (buffer)
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Read))
        {
            if (archive.Entries.Count != 1 || archive.Entries[0].FullName != DocumentName)
                throw new InvalidDataException("A recipe backup must contain only recipes.json.");
            var entry = archive.Entries[0];
            if (entry.Length > MaxDocumentBytes)
                throw new InvalidDataException("The recipe backup expands beyond the document limit.");
            await using var stream = entry.Open();
            return await ReadBoundedAsync(stream, MaxDocumentBytes, cancellationToken);
        }
    }

    private static async Task<MemoryStream> ReadBoundedAsync(
        Stream source, int limit, CancellationToken cancellationToken)
    {
        var output = new MemoryStream();
        try
        {
            var chunk = new byte[81920];
            int count;
            while ((count = await source.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (output.Length + count > limit)
                    throw new InvalidDataException("The recipe backup exceeds the size limit.");
                await output.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
            }
            output.Position = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }
}
