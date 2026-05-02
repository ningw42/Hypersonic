using System.Globalization;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;

using Microsoft.Graphics.Canvas.Text;

namespace Hypersonic.Services;

/// <summary>
/// Default <see cref="ISystemFontCatalog"/> implementation backed by DirectWrite
/// through Win2D. The font list is computed lazily on first access and cached
/// for the lifetime of the singleton.
/// </summary>
public sealed class SystemFontCatalog : ISystemFontCatalog
{
    private readonly Lazy<IReadOnlyList<string>> _familyNames;
    private readonly Lazy<HashSet<string>> _familyLookup;
    private readonly ILogger _log;

    public SystemFontCatalog(ILoggingService logService)
        : this(logService, EnumerateInstalledFontsCore)
    {
    }

    /// <summary>
    /// Test seam — accepts a custom enumerator so tests don't depend on the
    /// host's installed fonts.
    /// </summary>
    internal SystemFontCatalog(ILoggingService logService, Func<IEnumerable<string>> enumerator)
    {
        ArgumentNullException.ThrowIfNull(enumerator);

        _log = logService.GetLogger(nameof(SystemFontCatalog));

        _familyNames = new Lazy<IReadOnlyList<string>>(
            () =>
            {
                var raw = enumerator()
                    .Where(static n => !string.IsNullOrWhiteSpace(n))
                    .Select(static n => n.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                _log.Info("System font catalog loaded", new { Count = raw.Length });
                return raw;
            },
            LazyThreadSafetyMode.ExecutionAndPublication);

        _familyLookup = new Lazy<HashSet<string>>(
            () => new HashSet<string>(_familyNames.Value, StringComparer.OrdinalIgnoreCase),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<string> FamilyNames => _familyNames.Value;

    public bool Contains(string? familyName)
    {
        if (string.IsNullOrWhiteSpace(familyName))
        {
            return false;
        }

        return _familyLookup.Value.Contains(familyName.Trim());
    }

    public IEnumerable<string> Suggest(string? query, int maxResults = 12)
    {
        if (maxResults <= 0)
        {
            return [];
        }

        var trimmed = query?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return _familyNames.Value.Take(maxResults).ToArray();
        }

        return _familyNames.Value
            .Where(n => n.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateInstalledFontsCore()
    {
        using var fonts = CanvasFontSet.GetSystemFontSet();
        return fonts
            .GetPropertyValues(CanvasFontPropertyIdentifier.PreferredFamilyName, GetPreferredLocaleNames())
            .Select(static p => p.Value)
            .ToArray();
    }

    private static string GetPreferredLocaleNames()
    {
        return string.Join(
            ";",
            new[]
            {
                CultureInfo.CurrentUICulture.Name,
                CultureInfo.CurrentUICulture.Parent.Name,
                "en-US",
            }
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
