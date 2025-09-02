using Microsoft.SemanticKernel;

namespace AIHub.Backend.Infrastructure.Ai;

public class SemanticKernelService : IAiService
{
    private readonly Kernel _kernel;

    public SemanticKernelService(Kernel kernel)
    {
        _kernel = kernel;
    }

    public async Task<string> InvokePromptAsync(string prompt)
    {
        var response = await _kernel.InvokePromptAsync(prompt);
        return response.ToString();
    }
}