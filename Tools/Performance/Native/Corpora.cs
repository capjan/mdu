using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Mdu.Performance;

internal static class Corpora
{
    internal static JsonObject Generate(Options options)
    {
        var corpora = Path.Combine(options.Work, "corpora");
        if (Directory.Exists(corpora)) Directory.Delete(corpora, recursive: true);
        var small = Path.Combine(corpora, "small");
        Copy(Path.Combine(options.Repo, "Docs/Architecture"), small);
        Write(Path.Combine(small, "mdu.yml"), "site_name: Baseline\n");
        Fixtures(small);
        var docs = Path.Combine(corpora, "docs");
        Copy(Path.Combine(options.Repo, "Docs"), docs);
        foreach (var file in Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories))
            File.WriteAllText(file, File.ReadAllText(file).Replace("mdu:pdf", "https://example.invalid/pdf-disabled", StringComparison.Ordinal));
        Fixtures(docs);
        var large = Path.Combine(corpora, "large");
        Write(Path.Combine(large, "mdu.yml"), "site_name: Baseline\n");
        Write(Path.Combine(large, "README.md"), "# Mixed synthetic documentation\n\n" +
            string.Join('\n', Enumerable.Range(0, 10).Select(i => $"[Section {i}](section-{i}/README.md)")));
        for (var group = 0; group < 10; group++)
        {
            var folder = Path.Combine(large, $"section-{group}");
            Write(Path.Combine(folder, "README.md"), $"# Section {group}\n");
            Write(Path.Combine(folder, "api.yaml"), ApiSpec());
            for (var i = 0; i < 49; i++)
            {
                var body = (i % 5) switch
                {
                    0 => "## Reference\n\n" + string.Concat(Enumerable.Repeat("Portable documentation with a [home link](../README.md).\n\n", 15)),
                    1 => "```plantuml\n" + Diagram(12) + "\n```",
                    2 => "```openapi\n./api.yaml\n```",
                    3 => "```csharp\n" + string.Join('\n', Enumerable.Range(0, 80).Select(n => $"public sealed record Item{n}(int Id, string Name);")) + "\n```",
                    _ => "## Operations\n\n:material-home: :fontawesome-solid-book: :simple-github: :octicons-mark-github-16:\n\n" +
                         string.Concat(Enumerable.Repeat("| Key | Value |\n| --- | --- |\n| a | b |\n\n", 8))
                };
                Write(Path.Combine(folder, $"page-{i:00}.md"), $"# Topic {group}-{i}\n\n{body}\n");
            }
        }
        Fixtures(large);
        foreach (var size in new[] { 6, 18, 36 })
            Write(Path.Combine(corpora, $"diagram-{size}", "README.md"), "# Component complexity\n\n```plantuml\n" + Diagram(size) + "\n```\n");
        var manifest = new JsonObject();
        foreach (var folder in Directory.EnumerateDirectories(corpora).Order(StringComparer.Ordinal))
        {
            var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var file in files)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/') + "\0"));
                hash.AppendData(File.ReadAllBytes(file));
            }
            manifest[Path.GetFileName(folder)] = new JsonObject
            {
                ["pages"] = files.Count(file => file.EndsWith(".md", StringComparison.Ordinal)),
                ["bytes"] = files.Sum(file => new FileInfo(file).Length),
                ["sha256"] = Convert.ToHexStringLower(hash.GetHashAndReset())
            };
        }
        File.WriteAllText(Path.Combine(options.Work, "corpora.json"), manifest.ToJsonString(Program.Json));
        return manifest;
    }

    internal static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var folder in Directory.EnumerateDirectories(source))
            if (Path.GetFileName(folder) is not ("bin" or "obj") && !folder.EndsWith(".Test", StringComparison.Ordinal))
                Copy(folder, Path.Combine(destination, Path.GetFileName(folder)));
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void Fixtures(string root)
    {
        Write(Path.Combine(root, "api.yaml"), ApiSpec());
        Write(Path.Combine(root, "measure.md"), "# Measurement fixture\n\n## Stable anchor\n\nbaseline-search-sentinel :material-home: :simple-github:\n\n[Home](README.md) · [Architecture](diagram.md)\n\n[Consent video](https://youtu.be/hyeCMRQhVOI)\n\n```openapi\n./api.yaml\n```\n\n```csharp\npublic sealed record Item(int Id, string Name);\n```\n");
        Write(Path.Combine(root, "diagram.md"), "# Diagram fixture\n\n```plantuml\n" + Diagram(12) + "\n```\n");
        File.AppendAllText(Path.Combine(root, "README.md"), "\n\n[Measurement fixture](measure.md)\n\nbaseline-content-sentinel\n");
    }

    private static string ApiSpec() => "openapi: 3.0.3\ninfo:\n  title: Baseline API\n  version: 1.0.0\npaths:\n" +
        string.Concat(Enumerable.Range(0, 8).Select(i => $"  /items/{i}:\n    get:\n      summary: Baseline operation {i}\n      responses:\n        '200':\n          description: Success\n          content:\n            application/json:\n              schema:\n                type: object\n                properties:\n                  id: {{ type: integer }}\n                  name: {{ type: string }}\n"));

    private static string Diagram(int nodes)
    {
        var lines = new List<string> { "@startuml", "left to right direction" };
        for (var group = 0; group < 3; group++)
        {
            lines.Add($"package \"Group {group}\" {{");
            lines.AddRange(Enumerable.Range(0, nodes).Where(i => i % 3 == group).Select(i => $"  component \"Service {i}\" as N{i}"));
            lines.Add("}");
        }
        lines.AddRange(Enumerable.Range(0, nodes - 1).Select(i => $"N{i} --> N{i + 1} : Load"));
        lines.AddRange(Enumerable.Range(0, nodes - 3).Where(i => i % 3 == 0).Select(i => $"N{i} --> N{i + 3}"));
        lines.Add("@enduml");
        return string.Join('\n', lines);
    }
}
