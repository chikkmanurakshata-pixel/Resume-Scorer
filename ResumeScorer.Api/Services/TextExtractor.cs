using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace ResumeScorer.Api.Services;

public class TextExtractor
{
    public string Extract(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        using var stream = file.OpenReadStream();

        return ext switch
        {
            ".pdf" => ReadPdf(stream),
            ".docx" => ReadDocx(stream),
            ".txt" => new StreamReader(stream).ReadToEnd(),
            _ => throw new NotSupportedException("Only PDF, DOCX and TXT are supported.")
        };
    }

    private static string ReadPdf(Stream stream)
    {
        var sb = new StringBuilder();
        using var pdf = PdfDocument.Open(stream);
        foreach (var page in pdf.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }

    private static string ReadDocx(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var paragraphs = doc.MainDocumentPart?.Document.Body?
            .Descendants<Paragraph>().Select(p => p.InnerText) ?? Enumerable.Empty<string>();
        return string.Join(Environment.NewLine, paragraphs);
    }
}