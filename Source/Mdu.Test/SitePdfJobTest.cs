using System.Collections;
using Markdown.Document.Impl;
using Markdown.Renderer;
using Mdu.Commands;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

public class SitePdfJobTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SectionFailureTerminatesTheJobAndDiscardsThePreviousDownload(bool unexpectedFailure)
    {
        var output = Path.GetTempFileName();
        try
        {
            File.WriteAllText(output, "Old PDF");
            var renderer = new PdfRenderer(new HtmlRenderer());
            var command = new PdfCommand(new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())), renderer);
            var sections = new FailingSections(unexpectedFailure
                ? new FormatException("Injected section failure")
                : new InvalidOperationException("Injected section failure"));
            var job = new PdfCommand.SitePdfJob(output, sections.ToArray(), renderer.CreateSession("Manual", sections));
            Assert.False(command.AdvanceSitePdfJob(job));
            Assert.True(command.AdvanceSitePdfJob(job));
            Assert.True(job.Failed);
            Assert.False(File.Exists(output));
            Assert.True(command.AdvanceSitePdfJob(job));
            Assert.True(job.Failed);
            Assert.False(File.Exists(output));
        }
        finally { File.Delete(output); }
    }

    // Inject a failure after a successfully rendered section, without depending on a particular parser bug.
    private sealed class FailingSections(Exception failure) : IReadOnlyList<PdfSourceSection>
    {
        private readonly PdfSourceSection[] _sections =
        [new("Home", "index.html", "# Home"), new("Details", "Details.html", "# Details")];
        public int Count => _sections.Length;
        public PdfSourceSection this[int index] => index == 1 ? throw failure : _sections[index];
        public IEnumerator<PdfSourceSection> GetEnumerator() => ((IEnumerable<PdfSourceSection>)_sections).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
