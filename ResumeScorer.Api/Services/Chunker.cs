using System.Text;

namespace ResumeScorer.Api.Services;

public static class Chunker
{
    public static List<string> Split(string text, int maxChars = 500)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            if (current.Length + line.Length > maxChars && current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }
            current.AppendLine(line);
        }
        if (current.Length > 0) chunks.Add(current.ToString());
        return chunks;
    }
}