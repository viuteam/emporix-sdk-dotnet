using Microsoft.AspNetCore.Diagnostics;
using Viu.Emporix;

namespace Viu.Emporix.Storefront;

/// <summary>
/// Derives the Emporix auth context from the incoming request.
/// </summary>
internal static class ShopperContext
{
    /// <summary>
    /// Returns the context this request acts under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole reason <see cref="AuthContext"/> is a parameter rather than a
    /// property on the client: one client instance serves every concurrent
    /// visitor, and each call carries its own identity. Storing the token on the
    /// client would serve one shopper's cart to the next.
    /// </para>
    /// <para>
    /// A signed-in visitor sends their own Emporix token; everyone else browses
    /// anonymously, and the SDK mints and refreshes that session itself.
    /// </para>
    /// </remarks>
    public static AuthContext Shopper(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? header = context.Request.Headers.Authorization.FirstOrDefault();

        return header is { Length: > 7 }
            && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? AuthContext.Customer(header[7..])
                : AuthContext.Anonymous();
    }
}

/// <summary>
/// Turns an SDK failure into an HTTP response, once, for every endpoint.
/// </summary>
/// <remarks>
/// Doing this per endpoint is how a «product not found» ends up reported as a
/// 500. The correlation id is passed on: it is what ties this response to what
/// Emporix logged, and a support request without it is guesswork.
/// </remarks>
internal static class EmporixProblem
{
    public static async Task WriteAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Exception? exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        (int status, string title) = exception switch
        {
            EmporixNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            EmporixValidationException => (StatusCodes.Status400BadRequest, "Rejected by Emporix"),
            EmporixAuthenticationException => (StatusCodes.Status401Unauthorized, "Not authenticated"),
            EmporixForbiddenException => (StatusCodes.Status403Forbidden, "Not allowed"),

            // Emporix is rate limiting us, not the visitor — but the visitor is
            // the one who has to wait, so the status is passed through.
            EmporixRateLimitException => (StatusCodes.Status429TooManyRequests, "Too many requests"),

            EmporixTimeoutException => (StatusCodes.Status504GatewayTimeout, "Emporix did not answer in time"),
            EmporixException => (StatusCodes.Status502BadGateway, "Emporix call failed"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected failure"),
        };

        context.Response.StatusCode = status;

        // The message can name a product or a field, which is fine; it never
        // carries a token, because the SDK's exceptions do not.
        await context.Response.WriteAsJsonAsync(
            new ProblemResponse(
                title,
                exception is EmporixException emporix ? emporix.Message : null,
                (exception as EmporixException)?.CorrelationId),
            StorefrontJsonContext.Default.ProblemResponse);
    }
}

/// <summary>What a failed call tells the caller.</summary>
/// <param name="Title">What kind of failure it was.</param>
/// <param name="Detail">What Emporix said, when it said anything.</param>
/// <param name="CorrelationId">The id to quote when asking Emporix about it.</param>
internal sealed record ProblemResponse(string Title, string? Detail, string? CorrelationId);
