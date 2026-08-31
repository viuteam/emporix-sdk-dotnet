using System.Text.Json.Serialization;

namespace Viu.Emporix;

/// <summary>
/// The body for signing in through a social identity provider.
/// </summary>
/// <remarks>
/// The specification declares this body inline rather than as a named schema,
/// so the generator produces nothing for it.
/// </remarks>
public sealed class SocialLoginRequest
{
    /// <summary>The token the identity provider issued.</summary>
    [JsonPropertyName("token")]
    public required string Token { get; init; }
}
