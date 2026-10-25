using Core.Resources;

namespace Markdown.Renderer;

public static class Resources
{
    private static readonly ResourceService R = ResourceService.FromType(typeof(Resources));
    public static string CssContent() => R.GetStringByName("Markdown.Renderer.Res.Css.main.css");
    public static string HtmlTemplateContent() => R.GetStringByName("Markdown.Renderer.Res.Html.main.html");
    public static string IconSearchScriptContent() => R.GetStringByName("Markdown.Renderer.Res.Js.icon-search.js");
    public static string HeroCssContent() => R.GetStringByName("Markdown.Renderer.Res.Css.hero.css");
    public static string HeroScriptContent() => R.GetStringByName("Markdown.Renderer.Res.Js.hero-beach.js");
    public static string IsometricDiagramScriptContent() => R.GetStringByName("Markdown.Renderer.Res.Js.isometric-diagram.js");
}
