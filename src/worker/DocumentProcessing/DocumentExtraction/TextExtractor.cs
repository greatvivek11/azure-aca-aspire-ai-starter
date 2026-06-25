using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

internal sealed class TextExtractor
{
    public async Task<string> ExtractTextAsync(string fileName, Stream blobStream)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".txt" => await ReadTextFileAsync(blobStream),
            ".pdf" => await ReadPdfFileAsync(blobStream),
            ".docx" => await ReadDocxFileAsync(blobStream),
            _ => await ReadTextFileAsync(blobStream)
        };
    }

    private static async Task<string> ReadTextFileAsync(Stream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private static Task<string> ReadPdfFileAsync(Stream stream)
    {
        stream.Position = 0;
        using var document = PdfDocument.Open(stream);
        var content = string.Join("\n", document.GetPages().Select(page => page.Text));
        return Task.FromResult(content);
    }

    private static Task<string> ReadDocxFileAsync(Stream stream)
    {
        stream.Position = 0;
        using var document = WordprocessingDocument.Open(stream, false);
        var text = document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
        return Task.FromResult(text);
    }
}
