internal sealed class IngestionQueueProcessor(
    IDocumentJobRepository jobRepository,
    DocumentProcessor documentProcessor,
    ILogger<IngestionQueueProcessor> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting ingestion queue processor.");
        var consecutiveLoopFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            DocumentIngestionJob? claimedJob = null;
            try
            {
                claimedJob = await jobRepository.TryClaimNextQueuedAsync(cancellationToken);
                if (claimedJob is null)
                {
                    consecutiveLoopFailures = 0;
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                await documentProcessor.ProcessDocumentAsync(claimedJob.DocumentId, cancellationToken);
                consecutiveLoopFailures = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveLoopFailures++;
                var backoff = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(consecutiveLoopFailures, 5))));

                if (claimedJob is not null)
                {
                    var isTransient = ex is HttpRequestException hre
                        && hre.StatusCode == System.Net.HttpStatusCode.TooManyRequests;

                    if (isTransient)
                    {
                        logger.LogWarning(ex, "Transient rate-limit error for document {DocumentId}; will re-queue.", claimedJob.DocumentId);
                        await jobRepository.UpdateStatusAsync(
                            claimedJob.DocumentId,
                            "Queued",
                            0,
                            null,
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        logger.LogError(ex, "Unexpected error while processing document {DocumentId}", claimedJob.DocumentId);
                        await jobRepository.UpdateStatusAsync(
                            claimedJob.DocumentId,
                            "Failed",
                            100,
                            ex.Message,
                            cancellationToken: cancellationToken);
                    }
                }
                else
                {
                    logger.LogError(ex, "Unexpected error while claiming queued ingestion jobs.");
                }

                try
                {
                    await Task.Delay(backoff, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        logger.LogInformation("Stopping ingestion queue processor.");
    }
}
