using Rdo.Api.Infrastructure;

namespace Rdo.Api.Endpoints;

/// <summary>Every /api call needs a known key; the caller is attached to the request for handlers and the audit.</summary>
public sealed class CallerFilter(ApiKeys keys) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var caller = keys.Find(context.HttpContext.Request.Headers["X-Api-Key"].ToString());
        if (caller is null)
        {
            return Results.Problem("Unknown or missing API key.", statusCode: StatusCodes.Status401Unauthorized);
        }

        context.HttpContext.Items["caller"] = caller;
        return await next(context).ConfigureAwait(false);
    }
}

public static class HttpContextCaller
{
    public static Caller Caller(this HttpContext ctx) => (Caller)ctx.Items["caller"]!;

    public static IResult? RequireRole(this HttpContext ctx, string role) =>
        ctx.Caller().Role == role ? null : Results.Problem($"This needs the {role} role.", statusCode: StatusCodes.Status403Forbidden);
}
