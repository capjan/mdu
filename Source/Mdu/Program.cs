using Markdown.Document;
using Markdown.Document.Impl;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Renderer;
using Mdu.Service.Visitors;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

var registrations = new ServiceCollection();
registrations.AddScoped<ILinterErrorReceiver, ConsoleLintErrorReceiver>();
registrations.AddScoped<LinterVisitor>();
registrations.AddScoped<IMarkdownDocumentReader, MarkdownDocumentReader>();
registrations.AddScoped<DataPreparationVisitor>();
registrations.AddScoped<PrintTreeVisitor>();
registrations.AddScoped<LintResultRenderer>();
registrations.AddScoped<GraphBuilder>();
registrations.AddScoped<GitTreeSourceResolver>();
registrations.AddScoped<IGitTreeSourceResolver>(provider => provider.GetRequiredService<GitTreeSourceResolver>());
registrations.AddScoped<FixCommand>();
registrations.AddScoped<CountNodesVisitor<MarkdownFile>>();
registrations.AddScoped<HtmlRenderer>();
registrations.AddScoped<PdfRenderer>();
registrations.AddScoped<SearchIndexBuilder>();
registrations.AddScoped<LintCommand>();
registrations.AddScoped<PrintTreeCommand>();
registrations.AddScoped<RenderCommand>();
registrations.AddScoped<PdfCommand>();
registrations.AddScoped<ServeCommand>();
registrations.AddScoped<PageCommand>();

using var services = registrations.BuildServiceProvider();

var rootCommand = new RootCommand("MDU — Markdown Documentation Utility for linting, rendering, and managing Markdown documentation.");

var newCommand = new Command("new", "Creates a starter MDU documentation project.");
var newDirectory = new Argument<string?>("directory")
{
    Arity = ArgumentArity.ZeroOrOne,
    Description = "Directory to initialize. Defaults to the current directory."
};
newCommand.Arguments.Add(newDirectory);
newCommand.SetAction(parseResult => NewCommand.Execute(parseResult.GetValue(newDirectory)));
rootCommand.Subcommands.Add(newCommand);

var lintCommand = new Command("lint", "Runs the linter and prints the results.");
lintCommand.Aliases.Add("l");
var (lintPattern, lintHidden) = AddVisitorOptions(lintCommand);
var lintPath = AddRootPathArgument(lintCommand, "Root folder containing Markdown files. Defaults to ./docs.");
var lintPretty = new Option<bool>("--pretty")
{
    Description = "Group lint results by file and use relative paths."
};
var lintColor = new Option<bool>("--color")
{
    Description = "Colorize lint results."
};
var lintFormat = new Option<string>("--format")
{
    Description = "Output format: text, json for scripts and tools, or github for annotations in GitHub Actions.",
    DefaultValueFactory = _ => "text"
};
lintFormat.AcceptOnlyFromAmong("text", "json", "github");
lintCommand.Options.Add(lintPretty);
lintCommand.Options.Add(lintColor);
lintCommand.Options.Add(lintFormat);
lintCommand.SetAction(parseResult =>
    services.GetRequiredService<LintCommand>().Execute(
        GetVisitorSettings(parseResult, lintPath, lintPattern, lintHidden),
        parseResult.GetValue(lintPretty),
        parseResult.GetValue(lintColor),
        Enum.Parse<LintFormat>(parseResult.GetValue(lintFormat) ?? "text", ignoreCase: true)));
rootCommand.Subcommands.Add(lintCommand);

var fixCommand = new Command("fix", "Migrates MkDocs-specific Markdown to supported Markdown and applies automatic fixes.");
var (fixPattern, fixHidden) = AddVisitorOptions(fixCommand);
var fixPath = AddRootPathArgument(fixCommand, "Root folder containing Markdown files. Defaults to ./docs.");
fixCommand.SetAction(parseResult =>
    services.GetRequiredService<FixCommand>().Execute(
        GetVisitorSettings(parseResult, fixPath, fixPattern, fixHidden)));
rootCommand.Subcommands.Add(fixCommand);

var treeCommand = new Command("tree", "Prints the detected Markdown files as a tree.");
treeCommand.Aliases.Add("print");
treeCommand.Aliases.Add("print-tree");
var (treePattern, treeHidden) = AddVisitorOptions(treeCommand);
var treePath = AddRootPathArgument(treeCommand, "Root folder containing Markdown files. Defaults to ./docs.");
treeCommand.SetAction(parseResult =>
    services.GetRequiredService<PrintTreeCommand>().Execute(
        GetVisitorSettings(parseResult, treePath, treePattern, treeHidden)));

var renderCommand = new Command("render", "Renders Markdown files to a static HTML site.");
renderCommand.Aliases.Add("r");
var (renderPattern, renderHidden) = AddVisitorOptions(renderCommand);
var renderRootPath = new Argument<string?>("path")
{
    Arity = ArgumentArity.ZeroOrOne,
    Description = "Root folder containing Markdown files. Defaults to ./docs."
};
var outputPath = new Argument<string?>("outpath")
{
    Arity = ArgumentArity.ZeroOrOne,
    Description = "Output folder for rendered files. Defaults to ./site."
};
var editPageRoot = new Option<string>("--editPageRoot")
{
    Description = "Root GitHub or GitLab edit URL for Markdown files. Adds per-page edit links and a repository button.",
    DefaultValueFactory = _ => string.Empty
};
renderCommand.Arguments.Add(renderRootPath);
renderCommand.Arguments.Add(outputPath);
renderCommand.Options.Add(editPageRoot);
renderCommand.SetAction(parseResult =>
    services.GetRequiredService<RenderCommand>().Execute(new RenderCommandSettings
    {
        RootPath = parseResult.GetValue(renderRootPath),
        OutputPath = parseResult.GetValue(outputPath),
        SearchPattern = parseResult.GetValue(renderPattern),
        IncludeHidden = parseResult.GetValue(renderHidden),
        EditPageRoot = parseResult.GetValue(editPageRoot) ?? string.Empty
    }));
rootCommand.Subcommands.Add(renderCommand);

var pdfCommand = new Command("pdf", "Renders Markdown documentation to one local PDF file.");
var (pdfPattern, pdfHidden) = AddVisitorOptions(pdfCommand);
var pdfRootPath = new Argument<string?>("path")
{
    Arity = ArgumentArity.ZeroOrOne,
    Description = "Root folder containing Markdown files or one Markdown file. Defaults to ./docs."
};
var pdfOutputPath = new Argument<string?>("outpath")
{
    Arity = ArgumentArity.ZeroOrOne,
    Description = "PDF file to create. Defaults to ./documentation.pdf for a folder, or a PDF named after the selected Markdown file."
};
pdfCommand.Arguments.Add(pdfRootPath);
pdfCommand.Arguments.Add(pdfOutputPath);
var pdfPage = new Option<string?>("--page")
{
    Description = "Export this Markdown page and all its navigation descendants. Path is relative to the documentation root."
};
pdfCommand.Options.Add(pdfPage);
pdfCommand.SetAction(parseResult =>
    services.GetRequiredService<PdfCommand>().Execute(new RenderCommandSettings
    {
        RootPath = parseResult.GetValue(pdfRootPath),
        OutputPath = parseResult.GetValue(pdfOutputPath),
        PdfRootPage = parseResult.GetValue(pdfPage),
        SearchPattern = parseResult.GetValue(pdfPattern),
        IncludeHidden = parseResult.GetValue(pdfHidden)
    }));
rootCommand.Subcommands.Add(pdfCommand);

var serveCommand = new Command("serve", "Renders, serves, and watches a Markdown site.");
var (servePattern, serveHidden) = AddVisitorOptions(serveCommand);
var serveRootPath = new Argument<string?>("path")
{
    Arity = ArgumentArity.ZeroOrOne,
    Description = "Root folder containing the Markdown files. Defaults to ./docs."
};
var serveOutputPath = new Argument<string?>("outpath")
{
    Arity = ArgumentArity.ZeroOrOne,
    Description = "Output folder for the rendered site. Defaults to ./site."
};
var servePort = new Option<int>("--port")
{
    Description = "Local preview server port.",
    DefaultValueFactory = _ => 4173
};
var noBrowser = new Option<bool>("--no-browser")
{
    Description = "Do not open the preview in a browser."
};
var serveEditPageRoot = new Option<string>("--editPageRoot")
{
    Description = "Root GitHub or GitLab edit URL for Markdown files. Adds per-page edit links and a repository button.",
    DefaultValueFactory = _ => string.Empty
};
serveCommand.Arguments.Add(serveRootPath);
serveCommand.Arguments.Add(serveOutputPath);
serveCommand.Options.Add(servePort);
serveCommand.Options.Add(noBrowser);
serveCommand.Options.Add(serveEditPageRoot);
serveCommand.SetAction(parseResult =>
    services.GetRequiredService<ServeCommand>().Execute(
        new RenderCommandSettings
        {
            RootPath = parseResult.GetValue(serveRootPath),
            OutputPath = parseResult.GetValue(serveOutputPath),
            SearchPattern = parseResult.GetValue(servePattern),
            IncludeHidden = parseResult.GetValue(serveHidden),
            EditPageRoot = parseResult.GetValue(serveEditPageRoot) ?? string.Empty
        },
        parseResult.GetValue(servePort),
        parseResult.GetValue(noBrowser)));
rootCommand.Subcommands.Add(serveCommand);
rootCommand.Subcommands.Add(treeCommand);

var pageCommand = new Command("page", "Moves Markdown pages with their links and shows which pages link to a file.");
var pageMoveCommand = new Command("move", "Moves a page or a folder and updates the links of all pages.");
pageMoveCommand.Aliases.Add("mv");
var pageMoveSource = new Argument<string>("source") { Description = "Page or folder to move." };
var pageMoveTarget = new Argument<string>("target") { Description = "New path, or an existing folder to move into." };
var pageMoveDryRun = new Option<bool>("--dry-run") { Description = "Show what would change without changing any file." };
var (pageMoveRoot, pageMoveHidden) = AddPageOptions(pageMoveCommand);
pageMoveCommand.Arguments.Add(pageMoveSource);
pageMoveCommand.Arguments.Add(pageMoveTarget);
pageMoveCommand.Options.Add(pageMoveDryRun);
pageMoveCommand.SetAction(parseResult =>
    services.GetRequiredService<PageCommand>().Move(
        parseResult.GetValue(pageMoveRoot),
        parseResult.GetValue(pageMoveSource)!,
        parseResult.GetValue(pageMoveTarget)!,
        parseResult.GetValue(pageMoveHidden),
        parseResult.GetValue(pageMoveDryRun)));
pageCommand.Subcommands.Add(pageMoveCommand);

var pageLinksCommand = new Command("links", "Lists the links of all pages that point to a page or another file.");
var pageLinksFile = new Argument<string>("file") { Description = "Page, image, or other file; a folder stands for its README.md." };
var (pageLinksRoot, pageLinksHidden) = AddPageOptions(pageLinksCommand);
pageLinksCommand.Arguments.Add(pageLinksFile);
pageLinksCommand.SetAction(parseResult =>
    services.GetRequiredService<PageCommand>().Links(
        parseResult.GetValue(pageLinksRoot),
        parseResult.GetValue(pageLinksFile)!,
        parseResult.GetValue(pageLinksHidden)));
pageCommand.Subcommands.Add(pageLinksCommand);
rootCommand.Subcommands.Add(pageCommand);

return await rootCommand.Parse(args).InvokeAsync();

static (Option<string?> Pattern, Option<bool> IncludeHidden) AddVisitorOptions(Command command)
{
    var pattern = new Option<string?>("--pattern", "-p")
    {
        Description = "Search pattern for Markdown files.",
        DefaultValueFactory = _ => Default.SearchPattern
    };
    var includeHidden = new Option<bool>("--hidden")
    {
        Description = "Include hidden files and folders.",
        DefaultValueFactory = _ => true
    };

    command.Options.Add(pattern);
    command.Options.Add(includeHidden);
    return (pattern, includeHidden);
}

static VisitorSettings GetVisitorSettings(
    ParseResult parseResult,
    Argument<string?> path,
    Option<string?> pattern,
    Option<bool> includeHidden) => new()
{
    RootPath = parseResult.GetValue(path),
    SearchPattern = parseResult.GetValue(pattern),
    IncludeHidden = parseResult.GetValue(includeHidden)
};

static (Option<string?> Root, Option<bool> IncludeHidden) AddPageOptions(Command command)
{
    var root = new Option<string?>("--root")
    {
        Description = "Documentation folder whose pages are updated or searched. Defaults to ./docs."
    };
    var includeHidden = new Option<bool>("--hidden")
    {
        Description = "Include hidden files and folders.",
        DefaultValueFactory = _ => true
    };
    command.Options.Add(root);
    command.Options.Add(includeHidden);
    return (root, includeHidden);
}

static Argument<string?> AddRootPathArgument(Command command, string description = "Root/home path for the index.")
{
    var path = new Argument<string?>("path")
    {
        Arity = ArgumentArity.ZeroOrOne,
        Description = description
    };
    command.Arguments.Add(path);
    return path;
}
