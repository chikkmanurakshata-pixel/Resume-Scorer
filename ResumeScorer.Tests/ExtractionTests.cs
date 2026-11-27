using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Http;
using ResumeScorer.Api.Services;
using Xunit;

namespace ResumeScorer.Tests;

public class ChunkerTests
{
    [Fact]
    public void EmptyText_ReturnsNoChunks()
    {
        Assert.Empty(Chunker.Split(""));
    }

    [Fact]
    public void ShortLines_AreGroupedIntoOneChunk()
    {
        var chunks = Chunker.Split("line one\nline two\nline three");

        var chunk = Assert.Single(chunks);
        Assert.Contains("line one", chunk);
        Assert.Contains("line three", chunk);
    }

    [Fact]
    public void LongText_IsSplitIntoSeveralChunks_AndNothingIsLost()
    {
        var lines = new[] { new string('a', 300), new string('b', 300), new string('c', 300) };

        var chunks = Chunker.Split(string.Join("\n", lines), maxChars: 500);

        Assert.Equal(3, chunks.Count);
        foreach (var line in lines)
            Assert.Contains(chunks, c => c.Contains(line));
    }
}

public class TextExtractorTests
{
    private static IFormFile MakeFile(string name, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, stream.Length, "resume", name);
    }

    private static IFormFile MakeDocx(params string[] paragraphs)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                paragraphs.Select(p => new Paragraph(new Run(new Text(p))))
                          .Cast<OpenXmlElement>().ToArray()));
        }
        ms.Position = 0;
        return new FormFile(ms, 0, ms.Length, "resume", "cv.docx");
    }

    [Fact]
    public void TxtFile_ReturnsItsText()
    {
        var file = MakeFile("cv.txt", Encoding.UTF8.GetBytes("Hello resume"));

        Assert.Equal("Hello resume", new TextExtractor().Extract(file));
    }

    [Fact]
    public void UnsupportedExtension_ThrowsNotSupportedException()
    {
        var file = MakeFile("cv.doc", new byte[] { 1, 2, 3 });

        Assert.Throws<NotSupportedException>(() => new TextExtractor().Extract(file));
    }

    [Fact]
    public void DocxFile_ReturnsAllParagraphs()
    {
        var file = MakeDocx("Akshata", "C# developer", "SQL Server");

        var text = new TextExtractor().Extract(file);

        Assert.Contains("Akshata", text);
        Assert.Contains("C# developer", text);
        Assert.Contains("SQL Server", text);
    }
}