namespace Markdown.Renderer;

/// <summary>Reports PDF exclusion markers that do not form matching pairs.</summary>
public sealed class PdfHiddenContentException(string message) : Exception(message);
