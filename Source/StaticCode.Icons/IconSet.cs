namespace StaticCode.Icons;

/// <summary>
/// A bundled icon set. Shortcodes start with <see cref="ShortcodePrefix"/>, followed by the icon name,
/// following the naming of Material for MkDocs, for example <c>material-</c> + <c>account-circle</c>.
/// </summary>
public sealed class IconSet
{
    private readonly Lazy<IconSetData> _data;

    internal IconSet(string shortcodePrefix, string resourceName, string website)
    {
        ShortcodePrefix = shortcodePrefix;
        Website = website;
        _data = new Lazy<IconSetData>(() => IconSetData.Load(resourceName));
    }

    internal IconSet(string shortcodePrefix, IconSetData data, string website)
    {
        ShortcodePrefix = shortcodePrefix;
        Website = website;
        _data = new Lazy<IconSetData>(data);
        IsCustom = true;
    }

    /// <summary>True for a set of the documentation's own SVG files; see <see cref="IconCatalog.UseCustomIcons"/>.</summary>
    public bool IsCustom { get; }

    public string ShortcodePrefix { get; }
    public string Website { get; }
    public string Name => _data.Value.Name;
    public string Version => _data.Value.Version;
    public string Author => _data.Value.Author;
    public string License => _data.Value.License;
    public string LicenseUrl => _data.Value.LicenseUrl;

    /// <summary>All icons shown in the icon search; hidden icons and aliases are left out but can still be used.</summary>
    public IEnumerable<Icon> VisibleIcons =>
        _data.Value.Icons
            .Where(entry => !entry.Value.Hidden && !(ShortcodePrefix == "dashboard-" && entry.Key == "docker"))
            .Select(entry => CreateIcon(entry.Key, entry.Value));

    internal bool TryGetIcon(string name, out Icon icon)
    {
        var data = _data.Value;
        if (!data.Icons.TryGetValue(name, out var iconData)
            && !(data.Aliases.TryGetValue(name, out var parent) && data.Icons.TryGetValue(parent, out iconData)))
        {
            icon = null!;
            return false;
        }

        icon = CreateIcon(name, iconData);
        return true;
    }

    private Icon CreateIcon(string name, IconData iconData) => new(
        ShortcodePrefix + name,
        CreateGlyph(iconData),
        iconData.Dark is null ? null : CreateGlyph(iconData.Dark));

    private IconGlyph CreateGlyph(IconData iconData) => new(
        iconData.Body,
        iconData.Left,
        iconData.Top,
        iconData.Width ?? _data.Value.Width,
        iconData.Height ?? _data.Value.Height);
}
