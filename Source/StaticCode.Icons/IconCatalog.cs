namespace StaticCode.Icons;

/// <summary>
/// Resolves icon shortcodes such as <c>material-account-circle</c> to the bundled icon sets and the custom icon sets
/// of the documentation. Each bundled set is loaded on first use.
/// </summary>
public static class IconCatalog
{
    public static IReadOnlyList<IconSet> BuiltInSets { get; } = new[]
    {
        new IconSet("material-", "mdi", "https://pictogrammers.com/library/mdi/"),
        new IconSet("fontawesome-brands-", "fa7-brands", "https://fontawesome.com/search?ic=brands"),
        new IconSet("fontawesome-regular-", "fa7-regular", "https://fontawesome.com/search?ic=free&s=regular"),
        new IconSet("fontawesome-solid-", "fa7-solid", "https://fontawesome.com/search?ic=free&s=solid"),
        new IconSet("octicons-", "octicon", "https://primer.style/octicons/"),
        new IconSet("simple-", "simple-icons", "https://simpleicons.org/"),
        new IconSet("dashboard-", "dashboard-icons", "https://dashboardicons.com/")
    };

    private static volatile IReadOnlyList<IconSet> _sets = BuiltInSets;

    /// <summary>The bundled icon sets followed by the custom icon sets set with <see cref="UseCustomIcons"/>.</summary>
    public static IReadOnlyList<IconSet> Sets => _sets;

    /// <summary>
    /// Replaces the custom icon sets with the SVG files in <paramref name="directory"/>, where every folder is one set.
    /// <c>null</c> removes the custom icon sets.
    /// </summary>
    /// <exception cref="InvalidOperationException">An icon or set name is invalid, an SVG file cannot be read, or a set
    /// name overlaps with another set, for example <c>material</c>.</exception>
    public static void UseCustomIcons(string? directory)
    {
        var customSets = directory is null ? Array.Empty<IconSet>() : CustomIconSets.Load(directory);
        foreach (var customSet in customSets)
        {
            var overlapping = BuiltInSets.Concat(customSets).FirstOrDefault(set => set != customSet && Overlaps(set.ShortcodePrefix, customSet.ShortcodePrefix));
            if (overlapping is not null)
                throw new InvalidOperationException(
                    $"The custom icon set '{customSet.Name}' cannot be used because its shortcodes would mix with the icon set '{overlapping.ShortcodePrefix.TrimEnd('-')}'. Rename its folder.");
        }

        _sets = BuiltInSets.Concat(customSets).ToArray();
    }

    /// <summary>
    /// Finds the icon for a shortcode name without colons, for example <c>octicons-mark-github-16</c>.
    /// </summary>
    public static bool TryGetIcon(string shortcode, out Icon icon)
    {
        foreach (var set in _sets)
        {
            if (shortcode.Length > set.ShortcodePrefix.Length
                && shortcode.StartsWith(set.ShortcodePrefix, StringComparison.Ordinal)
                && set.TryGetIcon(shortcode[set.ShortcodePrefix.Length..], out icon))
                return true;
        }

        icon = null!;
        return false;
    }

    private static bool Overlaps(string prefix, string otherPrefix) =>
        prefix.StartsWith(otherPrefix, StringComparison.Ordinal) || otherPrefix.StartsWith(prefix, StringComparison.Ordinal);
}
