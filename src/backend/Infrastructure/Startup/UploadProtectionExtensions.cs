using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace AcaAspireAiTemplate.Backend.Infrastructure.Startup;

public static class UploadProtectionExtensions
{
    public static IApplicationBuilder UseUploadRequestProtection(this IApplicationBuilder app, long uploadMaxRequestBytes)
    {
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method)
                && context.Request.Path.StartsWithSegments("/v1/uploads", StringComparison.OrdinalIgnoreCase))
            {
                if (context.Request.ContentLength is long contentLength && contentLength > uploadMaxRequestBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        message = $"Upload payload too large. Maximum allowed is {uploadMaxRequestBytes} bytes."
                    });
                    return;
                }

                var maxBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (maxBodySizeFeature is not null && !maxBodySizeFeature.IsReadOnly)
                {
                    maxBodySizeFeature.MaxRequestBodySize = uploadMaxRequestBytes;
                }
            }

            await next();
        });

        return app;
    }
}
