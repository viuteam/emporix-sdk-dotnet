using System.Net;

namespace Viu.Emporix.Tests;

public class EmporixErrorParserTests
{
    private const string Request = "GET /product/acme/products";

    private static EmporixApiException Create(
        HttpStatusCode status,
        string? body = null,
        TimeSpan? retryAfter = null)
        => EmporixErrorParser.CreateException(status, Request, body, retryAfter);

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, typeof(EmporixAuthenticationException))]
    [InlineData(HttpStatusCode.Forbidden, typeof(EmporixForbiddenException))]
    [InlineData(HttpStatusCode.NotFound, typeof(EmporixNotFoundException))]
    [InlineData(HttpStatusCode.BadRequest, typeof(EmporixValidationException))]
    [InlineData(HttpStatusCode.UnprocessableEntity, typeof(EmporixValidationException))]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(EmporixRateLimitException))]
    [InlineData(HttpStatusCode.InternalServerError, typeof(EmporixServerException))]
    [InlineData(HttpStatusCode.BadGateway, typeof(EmporixServerException))]
    [InlineData(HttpStatusCode.ServiceUnavailable, typeof(EmporixServerException))]
    [InlineData(HttpStatusCode.GatewayTimeout, typeof(EmporixServerException))]
    public void Maps_status_code_to_exception_type(HttpStatusCode status, Type expected)
    {
        Assert.IsType(expected, Create(status));
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.UnsupportedMediaType)]
    public void Unmapped_status_falls_back_to_the_base_type(HttpStatusCode status)
    {
        // Exactly the base type, no specialisation.
        Assert.Equal(typeof(EmporixApiException), Create(status).GetType());
    }

    [Fact]
    public void Reads_the_documented_error_format()
    {
        const string body = """
            {
              "code": 400,
              "status": "Bad Request",
              "message": "Validation failed",
              "errorCode": "PRODUCT_INVALID",
              "details": ["name must not be empty", "code is required"]
            }
            """;

        EmporixApiException exception = Create(HttpStatusCode.BadRequest, body);

        Assert.Equal("PRODUCT_INVALID", exception.ErrorCode);
        Assert.Equal(2, exception.Details.Count);
        Assert.Contains("name must not be empty", exception.Details);
        Assert.Contains("Validation failed", exception.Message, StringComparison.Ordinal);
        Assert.Equal(body, exception.RawBody);
    }

    [Fact]
    public void Reads_the_gateway_fault_format_used_for_401()
    {
        // For a 401 Emporix' gateway answers in a different shape than the rest
        // of the API. The Node SDK does not read it.
        const string body = """
            {
              "fault": {
                "faultstring": "Invalid access token",
                "detail": { "errorcode": "oauth.v2.InvalidAccessToken" }
              }
            }
            """;

        EmporixApiException exception = Create(HttpStatusCode.Unauthorized, body);

        Assert.IsType<EmporixAuthenticationException>(exception);
        Assert.Equal("oauth.v2.InvalidAccessToken", exception.ErrorCode);
        Assert.Contains("Invalid access token", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Extracts_the_missing_scope_from_details()
    {
        const string body = """
            { "message": "Forbidden", "details": ["missing scope: product.product_manage"] }
            """;

        EmporixInsufficientScopeException exception =
            Assert.IsType<EmporixInsufficientScopeException>(Create(HttpStatusCode.Forbidden, body));

        Assert.Equal("product.product_manage", exception.RequiredScope);
        // Must still be caught by a catch on 403.
        Assert.IsAssignableFrom<EmporixForbiddenException>(exception);
    }

    [Fact]
    public void Plain_403_without_scope_hint_stays_a_forbidden_exception()
    {
        const string body = """{ "message": "Forbidden" }""";

        Assert.Equal(typeof(EmporixForbiddenException), Create(HttpStatusCode.Forbidden, body).GetType());
    }

    [Theory]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("Service Temporarily Unavailable")]
    [InlineData("{ this is not valid JSON")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"nur ein String\"")]
    public void Non_object_body_never_throws_and_is_preserved(string body)
    {
        // The crux: a proxy returning HTML must not raise a JsonException and
        // thereby hide the actual HTTP information.
        EmporixApiException exception = Create(HttpStatusCode.BadGateway, body);

        Assert.IsType<EmporixServerException>(exception);
        Assert.Equal(body, exception.RawBody);
        Assert.Null(exception.ErrorCode);
        Assert.Empty(exception.Details);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_body_is_handled(string? body)
    {
        EmporixApiException exception = Create(HttpStatusCode.NotFound, body);

        Assert.Empty(exception.Details);
        Assert.Contains("404", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Message_carries_request_and_status_even_without_a_body()
    {
        EmporixApiException exception = Create(HttpStatusCode.NotFound);

        Assert.Contains(Request, exception.Message, StringComparison.Ordinal);
        Assert.Contains("404", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rate_limit_exposes_retry_after()
    {
        // The Node SDK reads Retry-After only internally for the backoff and drops it.
        EmporixRateLimitException exception = Assert.IsType<EmporixRateLimitException>(
            Create(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(30)));

        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
    }

    [Fact]
    public void Rate_limit_without_header_has_no_retry_after()
    {
        EmporixRateLimitException exception =
            Assert.IsType<EmporixRateLimitException>(Create(HttpStatusCode.TooManyRequests));

        Assert.Null(exception.RetryAfter);
    }

    [Fact]
    public void Non_string_detail_entries_are_kept_as_raw_json()
    {
        const string body = """
            { "details": ["plain", { "field": "name", "issue": "required" }, null] }
            """;

        EmporixApiException exception = Create(HttpStatusCode.BadRequest, body);

        Assert.Equal(2, exception.Details.Count);
        Assert.Equal("plain", exception.Details[0]);
        Assert.Contains("\"field\"", exception.Details[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Details_of_wrong_shape_are_ignored()
    {
        const string body = """{ "message": "x", "details": "not an array" }""";

        Assert.Empty(Create(HttpStatusCode.BadRequest, body).Details);
    }

    [Fact]
    public void All_api_exceptions_are_catchable_as_emporix_exception()
    {
        Assert.IsAssignableFrom<EmporixException>(Create(HttpStatusCode.NotFound));
    }

    [Fact]
    public void Transport_exceptions_share_a_base_type()
    {
        // Network and timeout failures must be catchable together without
        // sweeping up API failures.
        EmporixTimeoutException timeout = new("too slow", TimeSpan.FromSeconds(5));
        EmporixNetworkException network = new("DNS failed");

        Assert.IsAssignableFrom<EmporixTransportException>(timeout);
        Assert.IsAssignableFrom<EmporixTransportException>(network);
        Assert.IsNotAssignableFrom<EmporixApiException>(timeout);
        Assert.Equal(TimeSpan.FromSeconds(5), timeout.Timeout);
    }

    [Fact]
    public void Correlation_id_is_settable_by_the_sdk_and_readable_by_callers()
    {
        EmporixApiException exception = Create(HttpStatusCode.NotFound);
        exception.CorrelationId = "abc-123";

        Assert.Equal("abc-123", exception.CorrelationId);
    }
}
