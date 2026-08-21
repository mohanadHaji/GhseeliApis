using System.Globalization;

namespace GhseeliApis.Services.Configuration;

public static class ConfigurationLanguageResolver
{
    public const string Arabic = "ar";
    public const string Hebrew = "he";

    public static string Resolve(string? overrideLanguage, string? acceptLanguageHeader)
    {
        if (TryNormalizeOverride(overrideLanguage, out var normalized))
        {
            return normalized;
        }

        return ResolveFromHeader(acceptLanguageHeader);
    }

    public static string ResolveFromHeader(string? acceptLanguageHeader)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguageHeader))
        {
            return Arabic;
        }

        LanguageCandidate? bestSupported = null;
        var segments = acceptLanguageHeader.Split(',');
        for (var index = 0; index < segments.Length; index++)
        {
            if (!TryParseHeaderCandidate(segments[index], index, out var candidate) ||
                !TryNormalizeLanguageTag(candidate.Tag, out var normalized))
            {
                continue;
            }

            var supportedCandidate = new LanguageCandidate(normalized, candidate.Quality, candidate.Index);
            if (bestSupported is null ||
                supportedCandidate.Quality > bestSupported.Value.Quality ||
                (supportedCandidate.Quality == bestSupported.Value.Quality &&
                 supportedCandidate.Index < bestSupported.Value.Index))
            {
                bestSupported = supportedCandidate;
            }
        }

        return bestSupported?.Tag ?? Arabic;
    }

    public static bool TryNormalizeOverride(string? language, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        var trimmed = language.Trim();
        if (trimmed.Equals(Arabic, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Arabic;
            return true;
        }

        if (trimmed.Equals(Hebrew, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Hebrew;
            return true;
        }

        return false;
    }

    private static bool TryNormalizeLanguageTag(string? language, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        var trimmed = language.Trim();
        var separatorIndex = trimmed.IndexOf('-');
        var primaryTag = separatorIndex >= 0
            ? trimmed[..separatorIndex]
            : trimmed;

        if (!IsValidPrimaryTag(primaryTag))
        {
            return false;
        }

        if (primaryTag.Equals(Arabic, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Arabic;
            return true;
        }

        if (primaryTag.Equals(Hebrew, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Hebrew;
            return true;
        }

        return false;
    }

    private static bool TryParseHeaderCandidate(
        string? segment,
        int index,
        out LanguageCandidate candidate)
    {
        candidate = default;
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var pieces = segment.Split(';', StringSplitOptions.TrimEntries);
        if (pieces.Length == 0 || string.IsNullOrWhiteSpace(pieces[0]))
        {
            return false;
        }

        var quality = 1m;
        var qualitySpecified = false;
        foreach (var piece in pieces.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(piece))
            {
                continue;
            }

            if (!piece.StartsWith("q=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (qualitySpecified ||
                !TryParseQuality(piece[2..], out quality) ||
                quality <= 0m)
            {
                return false;
            }

            qualitySpecified = true;
        }

        candidate = new LanguageCandidate(pieces[0], quality, index);
        return true;
    }

    private static bool TryParseQuality(string? value, out decimal quality)
    {
        quality = 0m;
        if (string.IsNullOrWhiteSpace(value) ||
            !decimal.TryParse(
                value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed is < 0m or > 1m)
        {
            return false;
        }

        quality = parsed;
        return true;
    }

    private static bool IsValidPrimaryTag(string primaryTag)
    {
        if (string.IsNullOrWhiteSpace(primaryTag))
        {
            return false;
        }

        foreach (var character in primaryTag)
        {
            if (!char.IsAsciiLetter(character))
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct LanguageCandidate(string Tag, decimal Quality, int Index);
}
