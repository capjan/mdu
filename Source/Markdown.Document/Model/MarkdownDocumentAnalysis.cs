namespace Markdown.Document.Model;

public sealed record MarkdownFootnote(string Label, int LineNumber);

public sealed record MarkdownPlantUmlBlock(string Source, int LineNumber);

public sealed record MarkdownDocumentAnalysis(
    IReadOnlyList<IMarkdownLink> Links,
    IReadOnlyList<MarkdownFootnote> UnusedFootnotes,
    IReadOnlyList<MarkdownFootnote> DuplicateFootnotes,
    IReadOnlyList<MarkdownPlantUmlBlock> PlantUmlBlocks);
