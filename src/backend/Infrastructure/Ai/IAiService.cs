namespace AIHub.Backend.Infrastructure.Ai;

public interface IAiService
{
    Task<string> InvokePromptAsync(string prompt);
}