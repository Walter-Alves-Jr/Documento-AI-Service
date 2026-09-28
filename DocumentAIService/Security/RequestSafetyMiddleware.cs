using System.Text.Json;

namespace DocumentAIService.Security;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 128)
            correlationId = Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        await next(context);
    }
}

public sealed class SafeExceptionMiddleware(RequestDelegate next, ILogger<SafeExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha técnica não tratada. CorrelationId: {CorrelationId}", context.TraceIdentifier);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/problem+json";
                await JsonSerializer.SerializeAsync(context.Response.Body, new
                {
                    type = "https://httpstatuses.com/500",
                    title = "Erro interno ao processar a solicitação.",
                    status = 500,
                    correlationId = context.TraceIdentifier
                });
            }
        }
    }
}
