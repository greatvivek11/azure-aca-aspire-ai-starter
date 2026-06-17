namespace AcaAspireAiTemplate.Backend.Infrastructure.Logging;

/// <summary>
/// Sanitizes user-controlled values before they are written to log entries.
/// Prevents CWE-117 / cs/log-forging by stripping characters that can be used
/// to forge new log lines in plain-text or HTML sinks.
/// </summary>
internal static class LogSanitizer
{
    /// <summary>
    /// Removes CR, LF, and other control characters from a user-supplied string
    /// so that it cannot be used to inject synthetic log entries.
    /// Returns an empty string if the input is null or whitespace.
    /// </summary>
    internal static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Strip control characters (CR, LF, tab, null, etc.) that could forge new lines
        // or corrupt structured log output.
        return string.Create(value.Length, value, static (span, src) =>
        {
            var writeIndex = 0;
            foreach (var ch in src)
            {
                if (!char.IsControl(ch))
                {
                    span[writeIndex++] = ch;
                }
            }

            span[writeIndex..].Fill('\0');
        }).TrimEnd('\0');
    }
}
