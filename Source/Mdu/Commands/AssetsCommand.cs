using Mdu.Service;
using Mdu.Service.Assets;

namespace Mdu.Commands;

/// <summary>
/// <c>mdu assets upload</c> (CLI-02 of mdu-assets): uploads images to the asset server of the site and prints one snippet per
/// file on standard output, so the result can be piped or pasted. Messages go to standard error.
/// </summary>
public class AssetsCommand(AssetServerClient client)
{
    public const string StandardInput = "-";

    public static readonly string[] Formats = ["markdown", "url", "html"];

    public int Upload(string? rootPath, IReadOnlyList<string> files, string? project, IReadOnlyList<string> tags, string? alt, string? name,
        string format, Stream? standardInput = null)
    {
        AssetsSettings? settings;
        try
        {
            settings = SiteConfigurationReader.ReadAssets(CommandPathDefaults.ResolveRootPath(rootPath));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or YamlDotNet.Core.YamlException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }

        if (settings is null)
        {
            Console.Error.WriteLine("No asset server configured. Add 'assets.server' to mdu.yml, for example:\n  assets:\n    server: https://assets.example.com\n    public: https://img.example.com\n    project: docs");
            return 1;
        }
        var targetProject = project ?? settings.Project;
        if (string.IsNullOrWhiteSpace(targetProject))
        {
            Console.Error.WriteLine("No project given. Use --project or set 'assets.project' in mdu.yml.");
            return 1;
        }
        if (AssetsSettings.ReadToken() is not { } token)
        {
            Console.Error.WriteLine($"No API token. Create one on the Account page of {settings.Server} and set {AssetsSettings.TokenVariable}.");
            return 1;
        }
        if (files.Count == 0)
        {
            Console.Error.WriteLine("Name at least one image file, or '-' to read one from standard input.");
            return 1;
        }

        var failed = 0;
        foreach (var file in files)
        {
            try
            {
                var (content, fileName) = file == StandardInput
                    ? (ReadAll(standardInput ?? Console.OpenStandardInput()), name ?? "image")
                    : (File.ReadAllBytes(file), name is not null && files.Count == 1 ? name : Path.GetFileName(file));
                var outcome = client.UploadAsync(settings, token, targetProject, fileName, alt, tags, content).GetAwaiter().GetResult();
                if (outcome.AlreadyUploaded)
                    Console.Error.WriteLine($"{Describe(file)} is already in the project as {outcome.Asset.FileName}.");
                Console.WriteLine(format switch
                {
                    "url" => outcome.Asset.Url,
                    "html" => outcome.Asset.Html,
                    _ => outcome.Asset.Markdown
                });
            }
            catch (Exception exception) when (exception is AssetServerException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"{Describe(file)}: {exception.Message}");
                failed++;
            }
        }

        return failed == 0 ? 0 : 1;
    }

    private static string Describe(string file) => file == StandardInput ? "Standard input" : file;

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
