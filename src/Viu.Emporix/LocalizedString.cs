using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Viu.Emporix;

/// <summary>
/// A text Emporix returns either translated or in every language it has.
/// </summary>
/// <remarks>
/// <para>
/// The same field comes back in two shapes depending on the request. Without an
/// <c>Accept-Language</c> header Emporix sends every translation:
/// </para>
/// <code>{ "name": { "de": "Kaffee", "en": "Coffee" } }</code>
/// <para>
/// With one it sends the single matching text:
/// </para>
/// <code>{ "name": "Kaffee" }</code>
/// <para>
/// Both are the same field, so both parse into this type. Which shape arrives
/// depends on <see cref="EmporixStorefrontContext.Language"/>: setting it makes
/// Emporix translate, leaving it unset makes Emporix send everything.
/// </para>
/// <para>
/// Read it with <see cref="ToString()"/> when any text will do, or with
/// <see cref="Get"/> when a specific language is wanted.
/// </para>
/// </remarks>
[JsonConverter(typeof(LocalizedStringConverter))]
public sealed class LocalizedString
{
    private readonly string? _text;
    private readonly IReadOnlyDictionary<string, string>? _translations;

    /// <summary>Creates a value holding one untagged text.</summary>
    /// <param name="text">The text.</param>
    public LocalizedString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
    }

    /// <summary>Creates a value holding translations by language tag.</summary>
    /// <param name="translations">The translations, keyed by language tag.</param>
    public LocalizedString(IReadOnlyDictionary<string, string> translations)
    {
        ArgumentNullException.ThrowIfNull(translations);
        _translations = translations;
    }

    /// <summary>The language tags this value carries, empty when it is a single text.</summary>
    public IReadOnlyCollection<string> Languages
        => _translations is null ? [] : (IReadOnlyCollection<string>)_translations.Keys;

    /// <summary>Whether Emporix sent every translation rather than one text.</summary>
    public bool IsTranslated => _translations is not null;

    /// <summary>Creates a value from a text.</summary>
    /// <param name="text">The text.</param>
    [return: NotNullIfNotNull(nameof(text))]
    public static implicit operator LocalizedString?(string? text)
        => text is null ? null : new LocalizedString(text);

    /// <summary>Reads the text for one language.</summary>
    /// <param name="language">The language tag, for example <c>de</c>.</param>
    /// <returns>The text, or <see langword="null"/> when this language is absent.</returns>
    /// <remarks>
    /// A single text answers for every language: when Emporix has already
    /// translated, there is nothing left to choose between.
    /// </remarks>
    public string? Get(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        if (_text is not null)
        {
            return _text;
        }

        return _translations!.TryGetValue(language, out string? text) ? text : null;
    }

    /// <summary>Reads the text for one language, falling back to any other.</summary>
    /// <param name="language">The preferred language tag.</param>
    /// <remarks>
    /// For display, where showing the wrong language beats showing nothing.
    /// </remarks>
    public string? GetOrAny(string language) => Get(language) ?? ToString();

    /// <summary>Returns some text: the single one, or the first translation.</summary>
    /// <remarks>
    /// Which translation «first» means is Emporix's ordering, not a choice made
    /// here — use <see cref="Get"/> when the language matters.
    /// </remarks>
    public override string ToString()
        => _text ?? _translations!.Values.FirstOrDefault() ?? string.Empty;

    /// <summary>Reads both shapes Emporix uses for a localized field.</summary>
    internal sealed class LocalizedStringConverter : JsonConverter<LocalizedString>
    {
        public override LocalizedString? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
            => reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.String => new LocalizedString(reader.GetString()!),
                JsonTokenType.StartObject => new LocalizedString(ReadTranslations(ref reader)),

                // Anything else is a shape neither branch of the specification
                // describes, and guessing at it would hide a real change.
                _ => throw new JsonException(
                    $"A localized value must be a string or an object, not {reader.TokenType}."),
            };

        public override void Write(
            Utf8JsonWriter writer,
            LocalizedString value,
            JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);

            // Written back in the shape it was read in: sending translations as
            // a single text would drop every language but one.
            if (value._text is not null)
            {
                writer.WriteStringValue(value._text);
                return;
            }

            writer.WriteStartObject();

            foreach ((string language, string text) in value._translations!)
            {
                writer.WriteString(language, text);
            }

            writer.WriteEndObject();
        }

        private static Dictionary<string, string> ReadTranslations(ref Utf8JsonReader reader)
        {
            Dictionary<string, string> translations = new(StringComparer.OrdinalIgnoreCase);

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string language = reader.GetString()!;
                reader.Read();

                // A null translation is «not translated», not an empty string.
                if (reader.TokenType is not JsonTokenType.Null)
                {
                    translations[language] = reader.GetString()!;
                }
            }

            return translations;
        }
    }
}
