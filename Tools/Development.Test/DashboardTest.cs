using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mdu.IconSets;
using Xunit;

namespace Development.Test;

public sealed class DashboardTest
{
    private const string Shape = "<path d=\"M1 1h8v8H1z\"/>";

    [Fact]
    public void PreservesThemePairsAndAdaptsOnlyUnpairedMonochromeIcons()
    {
        var icons = Convert(new Dictionary<string, string>
        {
            ["reversed-light"] = "<g style=\"fill:black\">" + Shape + "</g>",
            ["reversed-dark"] = "<g fill=\"white\">" + Shape + "</g>",
            ["normal-light"] = "<g fill=\"#fff\">" + Shape + "</g>",
            ["normal-dark"] = Shape,
            ["partial"] = "<g fill=\"white\">" + Shape + "</g>",
            ["partial-light"] = Shape,
            ["orphan-dark"] = "<g fill=\"white\">" + Shape + "</g>",
            ["plain"] = Shape,
            ["white"] = "<g fill=\"white\">" + Shape + "</g>",
            ["near-white"] = "<g fill=\"#fefefe\">" + Shape + "</g>",
            ["red-short"] = "<g fill=\"#e00\">" + Shape + "</g>",
            ["styled"] = "<g style=\"fill:#000;fill-rule:evenodd\">" + Shape + "</g>",
            ["duplicate-paint"] = "<g style=\"fill:red;fill:#000\">" + Shape + "</g>",
            ["outlined"] = "<path fill=\"none\" stroke=\"#000\" d=\"M1 1h8v8\"/>",
            ["transparent"] = "<path fill=\"none\" d=\"M0 0h10v10H0z\"/>" + Shape,
            ["colored"] = "<g fill=\"black\"><g fill=\"#2396ed\">" + Shape + "</g></g>",
            ["multicolor"] = Shape + "<circle fill=\"#fff\" cx=\"5\" cy=\"5\" r=\"2\"/>",
            ["colored-pair-dark"] = "<g fill=\"white\">" + Shape + "</g>",
            ["colored-pair-light"] = "<g fill=\"#2396ed\">" + Shape + "</g>",
            ["current-color-pair-dark"] = "<g fill=\"white\">" + Shape + "</g>",
            ["current-color-pair-light"] = "<g fill=\"currentColor\">" + Shape + "</g>",
            ["bright-pair-dark"] = "<g fill=\"#e57cd8\">" + Shape + "</g>",
            ["bright-pair-light"] = "<g fill=\"#2c1138\">" + Shape + "</g>",
            ["linear-light"] = "<g fill=\"#fff\">" + Shape + "</g>",
            ["linear-dark"] = "<g fill=\"#222326\">" + Shape + "</g>"
        });
        foreach (var name in new[] { "reversed", "normal", "partial", "colored-pair", "current-color-pair" })
        {
            var lightSource = name is "reversed" or "partial" or "colored-pair" or "current-color-pair" ? name + "-light" : name + "-dark";
            var darkSource = name == "partial" ? name : name is "reversed" or "colored-pair" or "current-color-pair" ? name + "-dark" : name + "-light";
            Assert.Equal(icons[lightSource]!["body"]!.GetValue<string>(), icons[name]!["body"]!.GetValue<string>());
            if (name != "partial") Assert.Equal(icons[darkSource]!["body"]!.GetValue<string>(), icons[name]!["dark"]!["body"]!.GetValue<string>());
        }
        Assert.Null(icons["orphan-dark"]!["dark"]);
        foreach (var name in new[] { "plain", "white", "near-white", "styled", "duplicate-paint", "outlined", "transparent" })
        {
            Assert.Contains("currentColor", icons[name]!["body"]!.GetValue<string>());
            Assert.Null(icons[name]!["dark"]);
        }
        foreach (var name in new[] { "colored", "multicolor", "red-short" }) Assert.DoesNotContain("currentColor", icons[name]!["body"]!.GetValue<string>());
        Assert.Equal(icons["bright-pair-light"]!["body"]!.GetValue<string>(), icons["bright-pair"]!["body"]!.GetValue<string>());
        Assert.Equal(icons["linear-dark"]!["body"]!.GetValue<string>(), icons["linear"]!["body"]!.GetValue<string>());
        Assert.True(icons["linear-light"]!["hidden"]!.GetValue<bool>());
    }

    [Fact]
    public void IsolatesReferencesAndPreservesComplexPaintsWithoutExecutingOrEmbeddingExternalContent()
    {
        const string gradient = "<defs><linearGradient id=\"a\"><stop stop-color=\"#000\"/><stop offset=\"1\" stop-color=\"#fff\"/></linearGradient></defs><path fill=\"url(#a)\" d=\"M1 1h8v8H1z\"/>";
        var icons = Convert(new Dictionary<string, string>
        {
            ["gradient"] = gradient, ["other-gradient"] = gradient,
            ["scripted"] = "<script>alert(1)</script><path onclick=\"alert(2)\" d=\"M0 0h1v1z\"/>",
            ["embedded-image"] = "<image href=\"data:image/png;base64,AAAA\"/>",
            ["foreign"] = "<foreignObject><div>external</div></foreignObject>",
            ["link"] = "<a href=\"https://example.com\">" + Shape + "</a>",
            ["styled"] = "<style>.a{fill:#123456}</style><path class=\"a\" d=\"M0 0h1v1z\"/>"
        });
        Assert.Null(icons["embedded-image"]);
        Assert.Null(icons["foreign"]);
        Assert.Null(icons["link"]);
        Assert.DoesNotContain("<script", icons["scripted"]!["body"]!.GetValue<string>());
        Assert.DoesNotContain("onclick", icons["scripted"]!["body"]!.GetValue<string>());
        Assert.DoesNotContain("currentColor", icons["gradient"]!["body"]!.GetValue<string>());
        var first = icons["gradient"]!["body"]!.GetValue<string>();
        var second = icons["other-gradient"]!["body"]!.GetValue<string>();
        var firstId = Regex.Match(first, "id=\"([^\"]+)\"").Groups[1].Value;
        var secondId = Regex.Match(second, "id=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(firstId);
        Assert.NotEqual(firstId, secondId);
        Assert.Contains("url(#" + firstId + ")", first);
        Assert.Contains("di-styled--scope", icons["styled"]!["body"]!.GetValue<string>());
    }

    [Fact]
    public void AcceptsLegacyNamespaceDeclarationsAndDiscardsHiddenEditorImages()
    {
        var icons = Convert(new Dictionary<string, string>
        {
            ["legacy"] = "<!DOCTYPE svg [<!ENTITY adobe 'urn:adobe:test'>]><svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:a=\"&adobe;\" viewBox=\"0 0 10 10\">" + Shape + "</svg>",
            ["no-namespace"] = "<svg width=\"10\" height=\"10\">" + Shape + "</svg>",
            ["editor-layer"] = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><image display=\"none\" href=\"data:image/png;base64,AAAA\"/>" + Shape + "</svg>"
        }, documents: true);
        foreach (var name in new[] { "legacy", "no-namespace", "editor-layer" }) Assert.NotNull(icons[name]);
        Assert.DoesNotContain("<image", icons["editor-layer"]!["body"]!.GetValue<string>());
    }

    private static JsonObject Convert(Dictionary<string, string> fixtures, bool documents = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mdu-icon-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "svg"));
        try
        {
            foreach (var (name, body) in fixtures)
                File.WriteAllText(Path.Combine(directory, "svg", name + ".svg"), documents ? body : "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\">" + body + "</svg>");
            return Dashboard.Convert(directory, "fixture")["icons"]!.AsObject();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
