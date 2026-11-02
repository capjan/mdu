using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Mdu.Performance;

internal static class Instrumentation
{
    internal static async Task<string> Build(Options options)
    {
        var light = options.Mode == "memory";
        var dest = Path.Combine(options.Work, light ? "memory-source" : "diagnostic-source");
        if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
        Corpora.Copy(Path.Combine(options.Repo, "Source"), Path.Combine(dest, "Source"));
        var probe = Path.Combine(options.Work, "Probe");
        Directory.CreateDirectory(probe);
        File.Copy(Path.Combine(options.Repo, "Tools/Performance/Probe.cs"), Path.Combine(probe, "Probe.cs"), overwrite: true);
        var document = XDocument.Load(Path.Combine(options.Repo, "Source/Markdown.Document/Markdown.Document.csproj"));
        var version = document.Descendants("PackageReference").Single(e => (string?)e.Attribute("Include") == "Markdig").Attribute("Version")!.Value;
        File.WriteAllText(Path.Combine(probe, "Probe.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
            <ItemGroup><PackageReference Include="Markdig" Version="{version}" /></ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(probe, "Parse.cs"), """
            namespace Mdu.Performance;
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
            }
            public static class MarkdownFileRead
            {
                public static string Read(string path)
                {
                    using var scope = Probe.Begin("markdown.disk-read");
                    return System.IO.File.ReadAllText(path);
                }
            }
            """);
        var project = new XElement("Project", new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", Path.Combine(probe, "Probe.csproj")))));
        File.WriteAllText(Path.Combine(dest, "Directory.Build.targets"), project.ToString());
        var stages = new (string File, string Signature, string Name)[]
        {
            ("Mdu/Commands/RenderCommand.cs", "public int Execute(", "render"),
            ("Mdu/Commands/RenderCommand.cs", "private static void CopyAssets(", "assets"),
            ("Mdu/Commands/ServeCommand.cs", "private void RebuildSite(", "serve.rebuild"),
            ("Mdu/Commands/ServeCommand.cs", "private void RebuildDependencyIndex(", "dependencies"),
            ("Mdu/Commands/ServeCommand.cs", "private PageDependencies ReadPageDependencies(", "dependency.page"),
            ("Mdu/Commands/ServeCommand.cs", "private void ProcessChangeBatch(", "serve.edit"),
            ("Mdu/Service/GraphBuilder.cs", "public MarkdownFile[] CreateGraph(", "graph"),
            ("Mdu/Service/Visitors/DataPreparationVisitor.cs", "public bool Process(", "metadata"),
            ("Mdu/Service/Visitors/LocalAssetsCopy.cs", "protected override bool ProcessFile(", "asset.page"),
            ("Mdu/Service/Visitors/MarkdigVisitor.cs", "public MarkdigVisitor(", "visitor.setup"),
            ("Mdu/Service/Visitors/MarkdigVisitor.cs", "private IReadOnlyCollection<string> FindSiteMediaProviders(", "media.scan"),
            ("Mdu/Service/Visitors/MarkdigVisitor.cs", "protected override bool ProcessFile(", "page"),
            ("Mdu/Service/Visitors/MarkdigVisitor.cs", "private IReadOnlyList<NavigationItem> BuildNavigation(", "navigation"),
            ("Mdu/Service/Visitors/MarkdigVisitor.cs", "private IReadOnlyList<BreadcrumbItem> BuildBreadcrumbs(", "breadcrumbs"),
            ("Mdu/Commands/PdfCommand.cs", "public int WriteSitePdfIfLinked(", "pdf.scan"),
            ("Markdown.Renderer/HtmlRenderer.cs", "public void PrepareBasePath(", "base.assets"),
            ("Markdown.Renderer/HtmlRenderer.cs", "public async Task WriteHtml(", "html"),
            ("Markdown.Renderer/HtmlRenderer.cs", "private static string RenderNavigationItems(", "html.navigation"),
            ("Markdown.Renderer/HtmlRenderer.cs", "private static string RenderPageFooter(", "html.footer"),
            ("Markdown.Renderer/HtmlRenderer.cs", "public IReadOnlyCollection<string> FindMediaProviders(", "media.page"),
            ("Markdown.Renderer/OpenApiMarkdownRenderer.cs", "public static string ExpandCodeBlocks(\n        string markdown,\n        string markdownPath,\n        out", "openapi.expand"),
            ("Markdown.Renderer/OpenApiMarkdownRenderer.cs", "private static LoadedSpecification LoadFromFile(", "openapi.load"),
            ("Markdown.Renderer/OpenApiMarkdownRenderer.cs", "private static OpenApiDocument ParseOpenApiDocument(", "openapi.parse"),
            ("Markdown.Document/Impl/MarkdownDocumentReader.cs", "public MarkdownDocumentAnalysis ReadAnalysis(", "links.analysis"),
            ("Markdown.Search/SearchIndexBuilder.cs", "public void AddPage(", "search.page"),
            ("Markdown.Search/SearchIndexBuilder.cs", "public void WriteIndex(", "search.write"),
            ("StaticCode.Diagrams/Layout/LayeredLayout.cs", "public LayoutResult Compute(", "diagram.layout"),
            ("StaticCode.Highlighter/HtmlFormatter.cs", "public void Write(", "highlight")
        };
        foreach (var stage in stages.Where(s => !light || s.Name is "render" or "serve.edit"))
            Scope(stage.File, stage.Signature, '"' + stage.Name + '"');
        if (!light)
        {
            Scope("StaticCode.Diagrams/DiagramRenderer.cs", "public static bool TryRenderHtml(", "\"diagram.\" + language.ToLowerInvariant()");
            Scope("StaticCode.Icons/IconSetData.cs", "public static IconSetData Load(", "\"icons.\" + resourceName");
            if (File.ReadAllText(Path.Combine(dest, "Source/Mdu/Model/MarkdownFile.cs")).Contains("public string ReadMarkdown() =>", StringComparison.Ordinal))
                Replace("Mdu/Model/MarkdownFile.cs", "public string ReadMarkdown() => MarkdownContent ?? File.ReadAllText(FileInfo.FullName);", """
                public string ReadMarkdown()
                    {
                        using var scope = Mdu.Performance.Probe.Begin("markdown.read");
                        return MarkdownContent ?? File.ReadAllText(FileInfo.FullName);
                    }
                """);
            else Scope("Mdu/Model/MarkdownFile.cs", "public string ReadMarkdown()", "\"markdown.read\"");
            Replace("Mdu/Model/MarkdownFile.cs", "File.ReadAllText(FileInfo.FullName)", "Mdu.Performance.MarkdownFileRead.Read(FileInfo.FullName)");
            Scope("Markdown.Renderer/HtmlRenderer.cs", "private static async Task WriteTemplate(", "\"html.write\"");
        }
        Replace("Mdu/Program.cs", "var registrations = new ServiceCollection();", "Mdu.Performance.Probe.Initialize();\nvar registrations = new ServiceCollection();");
        Replace("Mdu/Commands/ServeCommand.cs", "var url = server.BaseUrl;", "Mdu.Performance.Probe.Snapshot(\"serve.initial\");\n        var url = server.BaseUrl;");
        if (!light)
            foreach (var path in Directory.EnumerateFiles(Path.Combine(dest, "Source"), "*.cs", SearchOption.AllDirectories))
                File.WriteAllText(path, File.ReadAllText(path).Replace("Markdig.Markdown.Parse(", "Mdu.Performance.MarkdownParse.Parse(", StringComparison.Ordinal)
                    .Replace("Markdig.Parsers.MarkdownParser.Parse(", "Mdu.Performance.MarkdownParse.ParseRaw(", StringComparison.Ordinal));
        Console.WriteLine(await Program.Command("dotnet", ["build", Path.Combine(dest, "Source/Mdu/Mdu.csproj"), "-c", "Release", "-p:NuGetAudit=false"]));
        return Path.Combine(dest, "Source/Mdu/bin/Release/net10.0/mdu.dll");

        void Replace(string file, string old, string replacement)
        {
            var path = Path.Combine(dest, "Source", file);
            var text = File.ReadAllText(path);
            Measurements.Require(Regex.Matches(text, Regex.Escape(old)).Count == 1, "Instrumentation boundary changed: " + file + " " + old);
            File.WriteAllText(path, text.Replace(old, replacement, StringComparison.Ordinal));
        }

        void Scope(string file, string signature, string name)
        {
            var path = Path.Combine(dest, "Source", file);
            var text = File.ReadAllText(path);
            Measurements.Require(Regex.Matches(text, Regex.Escape(signature)).Count == 1, "Instrumentation signature changed: " + file + " " + signature);
            var start = text.IndexOf(signature, StringComparison.Ordinal);
            var brace = text.IndexOf('{', start);
            Measurements.Require(!text[start..brace].Contains("=>", StringComparison.Ordinal), "Expected a method body: " + signature);
            File.WriteAllText(path, text.Insert(brace + 1, $"\n        using var __perf = Mdu.Performance.Probe.Begin({name});"));
        }
    }
}
