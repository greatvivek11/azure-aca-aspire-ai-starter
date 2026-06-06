using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace AIHub.Backend.Infrastructure.Ai;

public class SemanticKernelService : IAiService
{
    private readonly Kernel _kernel;
    private readonly AzureOpenAiOptions _options;

    public SemanticKernelService(Kernel kernel, IOptions<AzureOpenAiOptions> options)
    {
        _kernel = kernel;
        _options = options.Value;
    }

    public async Task<string> InvokePromptAsync(string prompt)
    {
        var response = await _kernel.InvokePromptAsync(prompt);
        return response.ToString();
    }
}