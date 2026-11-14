namespace StaticCode.Highlighter;

public enum TokenType
{
    Text,
    Keyword,
    String,
    Operator,
    NewLine,
    Whitespace,
    Comment,
    Number,
    Key,
    Command,
    Subcommand,
    Option,
    Argument,
    MarkdownHeading,
    MarkdownSyntax,
    MarkdownLink,
    MarkdownUrl,
    MarkdownCode,
    MarkdownEmphasis,
    MarkdownStrong,
    MarkdownAlertNote,
    MarkdownAlertTip,
    MarkdownAlertImportant,
    MarkdownAlertWarning,
    MarkdownAlertCaution,
    /// <summary>A code annotation marker such as <c>(1)</c>. Its text is the annotation number.</summary>
    Annotation
}
