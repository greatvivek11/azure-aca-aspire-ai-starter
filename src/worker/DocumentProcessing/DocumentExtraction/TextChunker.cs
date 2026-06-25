internal sealed class TextChunker
{
    public List<string> ChunkText(string text, int chunkSize, int overlap)
    {
        var normalized = text.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return [];
        }

        var chunks = new List<string>();
        var index = 0;
        var step = Math.Max(1, chunkSize - overlap);
        while (index < words.Length)
        {
            var length = Math.Min(chunkSize, words.Length - index);
            chunks.Add(string.Join(' ', words.Skip(index).Take(length)));
            if (index + length >= words.Length)
            {
                break;
            }

            index += step;
        }

        return chunks;
    }
}
