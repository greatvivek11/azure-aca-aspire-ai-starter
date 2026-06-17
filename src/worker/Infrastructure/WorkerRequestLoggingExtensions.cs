using System.Diagnostics;

internal static class WorkerRequestLoggingExtensions
{
    public static IApplicationBuilder UseWorkerRequestLogging(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            var started = Stopwatch.GetTimestamp();

            try
            {
                await next();
                app.Logger().LogInformation(
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms",
                    LogSanitizer.Sanitize(context.Request.Method),
                    LogSanitizer.Sanitize(context.Request.Path),
                    context.Response.StatusCode,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (Exception ex)
            {
                app.Logger().LogError(
                    ex,
                    "HTTP {Method} {Path} failed after {ElapsedMilliseconds} ms",
                    LogSanitizer.Sanitize(context.Request.Method),
                    LogSanitizer.Sanitize(context.Request.Path),
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                throw;
            }
        });

        return app;
    }

    private static ILogger Logger(this IApplicationBuilder app)
    {
        return app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("WorkerHttp");
    }
}
