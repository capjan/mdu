using Markdig;
using Markdig.Extensions.Abbreviations;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;

namespace Markdown.Renderer.MarkdigExtension;

internal sealed class SharedAbbreviationsExtension : IMarkdownExtension
{
    private const string ProbeLabel = "__MDU_SHARED_ABBREVIATION_PROBE__";
    private static readonly object InjectionKey = new();
    private SharedAbbreviation[] _definitions = [];
    private AbbreviationParser? _abbreviationParser;

    public void SetDefinitions(IEnumerable<SharedAbbreviation> definitions) =>
        _definitions = definitions.ToArray();

    public static SharedAbbreviationsExtension FromMarkdown(string markdown)
    {
        var document = Markdig.Markdown.Parse(markdown, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
        var definitions = document.GetAbbreviations()?.Select(pair => new SharedAbbreviation(
            pair.Key, pair.Value.Label ?? pair.Key, pair.Value.Text.ToString())).ToArray() ?? [];
        if (definitions.Length == 0)
            throw new InvalidOperationException("The configured glossary does not contain any abbreviation definitions.");
        var extension = new SharedAbbreviationsExtension();
        extension.SetDefinitions(definitions);
        return extension;
    }

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        _abbreviationParser = pipeline.BlockParsers.OfType<AbbreviationParser>().FirstOrDefault();
        foreach (var parser in pipeline.BlockParsers)
            parser.Closed += AddDefinitions;
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    private void AddDefinitions(BlockProcessor processor, Block block)
    {
        var definitions = _definitions;
        var document = processor.Document;
        if (definitions.Length == 0 || document.GetData(InjectionKey) is true) return;
        document.SetData(InjectionKey, true);

        var localDefinitions = document.GetAbbreviations();
        if (localDefinitions is null)
        {
            RegisterAbbreviationProcessor(document, processor);
            localDefinitions = document.GetAbbreviations();
            localDefinitions?.Remove(ProbeLabel);
        }

        foreach (var definition in definitions)
        {
            if (localDefinitions is not null && localDefinitions.ContainsKey(definition.Key)) continue;

            document.AddAbbreviation(
                definition.Key,
                new Abbreviation(_abbreviationParser!)
                {
                    Label = definition.Label,
                    Text = new StringSlice(definition.Text)
                });
        }
    }

    private void RegisterAbbreviationProcessor(MarkdownDocument document, BlockProcessor processor)
    {
        if (_abbreviationParser is null) return;

        // Markdig registers its abbreviation matcher when it parses a definition block. Invoke the same
        // parser with a temporary definition so the stock matcher also runs for glossary-only documents.
        var probe = new BlockProcessor(document, processor.Parsers, processor.Context)
        {
            Line = new StringSlice($"*[{ProbeLabel}]: probe"),
            Column = 0,
            LineIndex = 0
        };
        probe.RestartIndent();
        _abbreviationParser.TryOpen(probe);
    }
}

internal sealed record SharedAbbreviation(string Key, string Label, string Text);
