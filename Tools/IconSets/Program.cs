using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Mdu.IconSets;

internal static class Program
{
    private const string DashboardCommit = "6d31345986eba2067a63eb41c1a5a87d4e8e850a";
    private static readonly (string Name, string Version)[] Sets =
    [
        ("mdi", "1.2.3"), ("fa7-brands", "1.2.4"), ("fa7-regular", "1.2.3"),
        ("fa7-solid", "1.2.5"), ("octicon", "1.2.39"), ("simple-icons", "1.2.100")
    ];

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "verify")
            {
                foreach (var file in Directory.EnumerateFiles(args[1], "*.json.gz").Order(StringComparer.Ordinal))
                {
                    JsonObject Read(string path)
                    {
                        using var input = File.OpenRead(path);
                        using var gzip = new GZipStream(input, CompressionMode.Decompress);
                        return JsonNode.Parse(gzip)!.AsObject();
                    }
                    var old = Read(file)["icons"]!.AsObject();
                    var current = Read(Path.Combine(args[2], Path.GetFileName(file)))["icons"]!.AsObject();
                    var missing = old.Select(p => p.Key).Except(current.Select(p => p.Key), StringComparer.Ordinal).ToArray();
                    if (missing.Length > 0) throw new InvalidOperationException($"Missing {missing.Length} icons from {Path.GetFileName(file)}: {string.Join(", ", missing)}");
                    Console.WriteLine($"Verified {Path.GetFileName(file)}: {old.Count} previous icons retained, {current.Count} generated");
                }
                return 0;
            }
            if (args.Length == 4 && args[0] == "dashboard")
            {
                WriteCompressed(args[2], Dashboard.Convert(args[1], args[3]));
                return 0;
            }
            if (args.Length != 0 && args[0] != "update")
                throw new ArgumentException("Usage: dotnet run --project Tools/IconSets -- update [repository] | dashboard <checkout> <output.json.gz> <revision>");
            var repository = Path.GetFullPath(args.Length > 1 ? args[1] : Directory.GetCurrentDirectory());
            var resources = Path.Combine(repository, "Source/StaticCode.Icons/Res");
            var work = Path.Combine(Path.GetTempPath(), "mdu-icons-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            Directory.CreateDirectory(resources);
            try
            {
                using var client = new HttpClient();
                foreach (var (name, version) in Sets)
                {
                    // Registry content is input data; no npm executable or package scripts run.
                    var metadata = JsonNode.Parse(await client.GetStringAsync($"https://registry.npmjs.org/@iconify-json/{name}/{version}"))!;
                    var compressed = await client.GetByteArrayAsync(metadata["dist"]!["tarball"]!.GetValue<string>());
                    using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
                    using var tar = new TarReader(gzip);
                    var files = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
                    while (await tar.GetNextEntryAsync() is { } entry)
                    {
                        if (entry.Name is not ("package/icons.json" or "package/info.json" or "package/package.json")) continue;
                        files[entry.Name] = (await JsonNode.ParseAsync(entry.DataStream!))!;
                    }
                    var icons = files["package/icons.json"];
                    var info = files["package/info.json"];
                    var reducedIcons = new JsonObject();
                    foreach (var (key, value) in icons["icons"]!.AsObject())
                    {
                        var icon = new JsonObject { ["body"] = value!["body"]!.DeepClone() };
                        foreach (var property in new[] { "width", "height", "hidden" })
                            if (value[property] is { } propertyValue) icon[property] = propertyValue.DeepClone();
                        reducedIcons[key] = icon;
                    }
                    var aliases = new JsonObject();
                    if (icons["aliases"] is JsonObject aliasMap)
                        foreach (var (key, value) in aliasMap) aliases[key] = value!["parent"]!.DeepClone();
                    var reduced = new JsonObject
                    {
                        ["name"] = info["name"]!.DeepClone(),
                        ["version"] = (info["version"] ?? files["package/package.json"]["version"])!.DeepClone(),
                        ["author"] = info["author"]?["name"]?.DeepClone() ?? JsonValue.Create(""),
                        ["license"] = (info["license"]?["spdx"] ?? info["license"]?["title"])?.DeepClone() ?? JsonValue.Create(""),
                        ["licenseUrl"] = info["license"]?["url"]?.DeepClone() ?? JsonValue.Create(""),
                        ["width"] = icons["width"]?.DeepClone() ?? JsonValue.Create(16),
                        ["height"] = icons["height"]?.DeepClone() ?? JsonValue.Create(16),
                        ["icons"] = reducedIcons, ["aliases"] = aliases
                    };
                    WriteCompressed(Path.Combine(resources, name + ".json.gz"), reduced);
                    Console.WriteLine($"Updated {name} ({version})");
                }
                var checkout = Path.Combine(work, "dashboard-icons");
                await Git("init", "-q", checkout);
                await Git("-C", checkout, "remote", "add", "origin", "https://github.com/homarr-labs/dashboard-icons.git");
                await Git("-C", checkout, "sparse-checkout", "set", "--no-cone", "/svg/", "/LICENSE");
                await Git("-C", checkout, "fetch", "-q", "--depth", "1", "--filter=blob:none", "origin", DashboardCommit);
                await Git("-C", checkout, "checkout", "-q", "FETCH_HEAD");
                WriteCompressed(Path.Combine(resources, "dashboard-icons.json.gz"), Dashboard.Convert(checkout, DashboardCommit));
                File.Copy(Path.Combine(checkout, "LICENSE"), Path.Combine(resources, "dashboard-icons.LICENSE"), overwrite: true);
            }
            finally { Directory.Delete(work, recursive: true); }
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static void WriteCompressed(string path, JsonObject data)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        using var writer = new System.Text.Json.Utf8JsonWriter(gzip);
        data.WriteTo(writer);
    }

    private static async Task Git(params string[] args)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output;
        if (process.ExitCode != 0) throw new InvalidOperationException(await error);
    }
}
