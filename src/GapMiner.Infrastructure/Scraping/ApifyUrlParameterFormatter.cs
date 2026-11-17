using System.Reflection;
using Refit;

namespace GapMiner.Infrastructure.Scraping;

/// <summary>
/// URL parameter formatter that renders query parameters exactly as the Apify
/// API v2 reference documents them. Apify specifies boolean query parameters
/// as <c>true</c>/<c>1</c> (lowercase), while Refit's default formatter
/// renders <see cref="bool"/> values culture-style as <c>True</c>/<c>False</c>;
/// every other type keeps the default formatting.
/// </summary>
public sealed class ApifyUrlParameterFormatter : IUrlParameterFormatter
{
    private readonly DefaultUrlParameterFormatter fallback = new();

    /// <inheritdoc />
    public string? Format(object? value, ICustomAttributeProvider attributeProvider, Type type)
    {
        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        return fallback.Format(value, attributeProvider, type);
    }
}
