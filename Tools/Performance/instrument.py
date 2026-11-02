"""Create an isolated, disposable diagnostic build. Fail when source boundaries move."""
import shutil
import subprocess
import re
from pathlib import Path


def build(repo, work, light=False):
    repo, work = repo.resolve(), work.resolve()
    dest = work / ("memory-source" if light else "diagnostic-source")
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True)
    shutil.copytree(repo / "Source", dest / "Source",
                    ignore=shutil.ignore_patterns("bin", "obj", "*.Test"))
    probe = work / "Probe"
    probe.mkdir(exist_ok=True)
    shutil.copyfile(Path(__file__).with_name("Probe.cs"), probe / "Probe.cs")
    markdig = re.search(r'Include="Markdig" Version="([^"]+)"',
                        (repo / 'Source/Markdown.Document/Markdown.Document.csproj').read_text()).group(1)
    (probe / "Probe.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
<ItemGroup><PackageReference Include="Markdig" Version="{markdig}" /></ItemGroup>
</Project>''')
    (probe / "Parse.cs").write_text('''namespace Mdu.Performance;
public static class MarkdownParse
{
    public static Markdig.Syntax.MarkdownDocument Parse(string text, Markdig.MarkdownPipeline pipeline)
    {
        using var scope = Probe.Begin("markdown.parse");
        return Markdig.Markdown.Parse(text, pipeline);
    }
    public static Markdig.Syntax.MarkdownDocument ParseRaw(string text, Markdig.MarkdownPipeline pipeline)
    {
        using var scope = Probe.Begin("markdown.parse");
        return Markdig.Parsers.MarkdownParser.Parse(text, pipeline);
    }
}''')
    (dest / "Directory.Build.targets").write_text(f'''<Project><ItemGroup>
<ProjectReference Include="{probe / 'Probe.csproj'}" />
</ItemGroup></Project>''')

    def replace(file, old, new):
        path = dest / "Source" / file
        text = path.read_text()
        assert text.count(old) == 1, (file, old, text.count(old))
        path.write_text(text.replace(old, new))

    def scope(file, signature, name):
        path = dest / "Source" / file
        text = path.read_text()
        assert text.count(signature) == 1, (file, signature)
        start = text.index(signature)
        brace = text.index("{", start)
        assert "=>" not in text[start:brace], (file, signature)
        text = text[:brace + 1] + f'\n        using var __perf = Mdu.Performance.Probe.Begin({name});' + text[brace + 1:]
        path.write_text(text)

    stages = [
        ("Mdu/Commands/RenderCommand.cs", "public int Execute(", '"render"'),
        ("Mdu/Commands/RenderCommand.cs", "private static void CopyAssets(", '"assets"'),
        ("Mdu/Commands/ServeCommand.cs", "private void RebuildSite(", '"serve.rebuild"'),
        ("Mdu/Commands/ServeCommand.cs", "private void RebuildDependencyIndex(", '"dependencies"'),
        ("Mdu/Commands/ServeCommand.cs", "private PageDependencies ReadPageDependencies(", '"dependency.page"'),
        ("Mdu/Commands/ServeCommand.cs", "private void ProcessChangeBatch(", '"serve.edit"'),
        ("Mdu/Service/GraphBuilder.cs", "public MarkdownFile[] CreateGraph(", '"graph"'),
        ("Mdu/Service/Visitors/DataPreparationVisitor.cs", "public bool Process(", '"metadata"'),
        ("Mdu/Service/Visitors/LocalAssetsCopy.cs", "protected override bool ProcessFile(", '"asset.page"'),
        ("Mdu/Service/Visitors/MarkdigVisitor.cs", "public MarkdigVisitor(", '"visitor.setup"'),
        ("Mdu/Service/Visitors/MarkdigVisitor.cs", "private IReadOnlyCollection<string> FindSiteMediaProviders(", '"media.scan"'),
        ("Mdu/Service/Visitors/MarkdigVisitor.cs", "protected override bool ProcessFile(", '"page"'),
        ("Mdu/Service/Visitors/MarkdigVisitor.cs", "private IReadOnlyList<NavigationItem> BuildNavigation(", '"navigation"'),
        ("Mdu/Service/Visitors/MarkdigVisitor.cs", "private IReadOnlyList<BreadcrumbItem> BuildBreadcrumbs(", '"breadcrumbs"'),
        ("Mdu/Commands/PdfCommand.cs", "public int WriteSitePdfIfLinked(", '"pdf.scan"'),
        ("Markdown.Renderer/HtmlRenderer.cs", "public void PrepareBasePath(", '"base.assets"'),
        ("Markdown.Renderer/HtmlRenderer.cs", "public async Task WriteHtml(", '"html"'),
        ("Markdown.Renderer/HtmlRenderer.cs", "private static string RenderNavigationItems(", '"html.navigation"'),
        ("Markdown.Renderer/HtmlRenderer.cs", "private static string RenderPageFooter(", '"html.footer"'),
        ("Markdown.Renderer/HtmlRenderer.cs", "public IReadOnlyCollection<string> FindMediaProviders(", '"media.page"'),
        ("Markdown.Renderer/OpenApiMarkdownRenderer.cs", "public static string ExpandCodeBlocks(\n        string markdown,\n        string markdownPath,\n        out", '"openapi.expand"'),
        ("Markdown.Renderer/OpenApiMarkdownRenderer.cs", "private static LoadedSpecification LoadFromFile(", '"openapi.load"'),
        ("Markdown.Renderer/OpenApiMarkdownRenderer.cs", "private static OpenApiDocument ParseOpenApiDocument(", '"openapi.parse"'),
        ("Markdown.Document/Impl/MarkdownDocumentReader.cs", "public MarkdownDocumentAnalysis ReadAnalysis(", '"links.analysis"'),
        ("Markdown.Search/SearchIndexBuilder.cs", "public void AddPage(", '"search.page"'),
        ("Markdown.Search/SearchIndexBuilder.cs", "public void WriteIndex(", '"search.write"'),
        ("StaticCode.Diagrams/DiagramRenderer.cs", "public static bool TryRenderHtml(", '"diagram." + language.ToLowerInvariant()'),
        ("StaticCode.Diagrams/Layout/LayeredLayout.cs", "public LayoutResult Compute(", '"diagram.layout"'),
        ("StaticCode.Highlighter/HtmlFormatter.cs", "public void Write(", '"highlight"'),
        ("StaticCode.Icons/IconSetData.cs", "public static IconSetData Load(", '"icons." + resourceName'),
    ]
    if light:
        stages = [s for s in stages if s[2] in ('"render"', '"serve.edit"')]
    for file, signature, name in stages:
        scope(file, signature, name)
    if not light:
        replace("Markdown.Renderer/HtmlRenderer.cs",
                "var renderedHtml = TemplateTokenRegex.Replace(Resources.HtmlTemplateContent(), match =>\n            values.TryGetValue(match.Groups[\"name\"].Value, out var value) ? value : match.Value);",
                '''string renderedHtml;
        using (Mdu.Performance.Probe.Begin("html.template"))
            renderedHtml = TemplateTokenRegex.Replace(Resources.HtmlTemplateContent(), match =>
                values.TryGetValue(match.Groups["name"].Value, out var value) ? value : match.Value);''')
        replace("Markdown.Renderer/HtmlRenderer.cs",
                "await writer.WriteAsync(renderedHtml).ConfigureAwait(false);",
                '''using (Mdu.Performance.Probe.Begin("html.write"))
            await writer.WriteAsync(renderedHtml).ConfigureAwait(false);''')
        replace("Mdu/Model/MarkdownFile.cs",
            "public string ReadMarkdown() => MarkdownContent ?? File.ReadAllText(FileInfo.FullName);",
            '''public string ReadMarkdown()
    {
        using var scope = Mdu.Performance.Probe.Begin("markdown.read");
        return MarkdownContent ?? File.ReadAllText(FileInfo.FullName);
    }''')
    replace("Mdu/Program.cs", "var registrations = new ServiceCollection();",
            "Mdu.Performance.Probe.Initialize();\nvar registrations = new ServiceCollection();")
    replace("Mdu/Commands/ServeCommand.cs", "var url = server.BaseUrl;",
            'Mdu.Performance.Probe.Snapshot("serve.initial");\n        var url = server.BaseUrl;')
    # Resolve calls through a wrapper, preserving both Markdig APIs and their pipelines.
    for path in ([] if light else (dest / "Source").rglob("*.cs")):
        text = path.read_text()
        text = text.replace("Markdig.Markdown.Parse(", "Mdu.Performance.MarkdownParse.Parse(")
        text = text.replace("Markdig.Parsers.MarkdownParser.Parse(", "Mdu.Performance.MarkdownParse.ParseRaw(")
        path.write_text(text)
    subprocess.run(["dotnet", "build", str(dest / "Source/Mdu/Mdu.csproj"), "-c", "Release", "-p:NuGetAudit=false"], check=True)
    return dest / "Source/Mdu/bin/Release/net10.0/mdu.dll"
