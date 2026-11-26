using System.Numerics.Tensors;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using ResumeScorer.Api.Models;

namespace ResumeScorer.Api.Services;

public class ScoringService(IChatClient chat, IEmbeddingGenerator<string, Embedding<float>> embedder)
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public async Task<ScoreResult> ScoreAsync(string resumeText, string jdText)
    {
        var resumeNorm = Norm(resumeText);

        // 1. Extract requirements AND keywords from the JD (one AI call)
        var reqPrompt = $$"""
            Analyse this job description. Return ONLY JSON, no markdown, in this shape:
            {"requirements":[{"text":"...","importance":"Required"}],"keywords":["C#",".NET Core"]}
            - requirements: at most 15 key requirements (skills, experience, responsibilities).
            - importance must be "Required" or "Preferred".
            - keywords: at most 25 specific technologies, tools, frameworks, certifications or
              methodologies named in the JD. No soft skills or generic words. Write each as it
              appears in the JD.

            JOB DESCRIPTION:
            {{jdText}}
            """;
        var extraction = await AskAsync<JdExtraction>(reqPrompt);
        var reqs = extraction.Requirements.Where(r => !string.IsNullOrWhiteSpace(r.Text)).ToList();
        if (reqs.Count == 0)
            throw new InvalidOperationException("Could not find requirements in the job description. Please paste the full JD text.");

        // 2. Chunk the resume and create embeddings
        var chunks = Chunker.Split(resumeText);
        var chunkVecs = await embedder.GenerateAsync(chunks);
        var reqVecs = await embedder.GenerateAsync(reqs.Select(r => r.Text).ToList());

        // 3. Retrieve the top 3 resume chunks per requirement
        var sb = new StringBuilder();
        for (int i = 0; i < reqs.Count; i++)
        {
            var top = Enumerable.Range(0, chunks.Count)
                .Select(c => (c, score: TensorPrimitives.CosineSimilarity(
                    reqVecs[i].Vector.Span, chunkVecs[c].Vector.Span)))
                .OrderByDescending(x => x.score)
                .Take(3)
                .Select(x => chunks[x.c]);

            sb.AppendLine($"[{i}] REQUIREMENT: {reqs[i].Text}");
            sb.AppendLine("RESUME EXCERPTS:");
            foreach (var t in top) sb.AppendLine("- " + t.Replace("\n", " "));
            sb.AppendLine();
        }

        // 4. Ask the LLM to judge each match (strict)
        var analysisPrompt = $$"""
            You are a strict recruiter. For each numbered requirement, judge how well the
            resume excerpts satisfy it. Use ONLY the excerpts as evidence.
            Rules:
            - "evidence" must be an exact phrase copied from the excerpts.
            - If there is no direct evidence, use "Missing".
            - Soft skills (communication, reasoning) are at most "Partial" unless explicitly stated.
            - Team size or leadership alone is not evidence of a technical skill.
            Return ONLY JSON, no markdown, in this shape:
            {"matches":[{"index":0,"matchLevel":"Strong","evidence":"exact phrase","gap":"what is missing, or empty"}],
             "suggestions":["3 to 5 concrete resume improvements"]}
            matchLevel must be "Strong", "Partial" or "Missing". Include every index.

            {{sb}}
            """;
        var analysis = await AskAsync<AnalysisResponse>(analysisPrompt);

        // 5. Semantic score, computed in C# with evidence verification
        var result = new ScoreResult { Suggestions = analysis.Suggestions };
        double earned = 0, total = 0;

        foreach (var m in analysis.Matches.Where(m => m.Index >= 0 && m.Index < reqs.Count))
        {
            var req = reqs[m.Index];

            var level = m.MatchLevel;
            if (level != "Missing" && !EvidenceFound(m.Evidence, resumeNorm))
                level = level == "Strong" ? "Partial" : "Missing";

            var levelWeight = level switch { "Strong" => 1.0, "Partial" => 0.5, _ => 0.0 };
            var importanceWeight = req.Importance == "Required" ? 2.0 : 1.0;
            earned += levelWeight * importanceWeight;
            total += importanceWeight;

            result.Matches.Add(new RequirementMatch
            {
                Requirement = req.Text,
                Importance = req.Importance,
                MatchLevel = level,
                Evidence = m.Evidence,
                Gap = m.Gap
            });
            if (level == "Strong") result.MatchedSkills.Add(req.Text);
            if (level == "Missing") result.MissingSkills.Add(req.Text);
        }
        result.SemanticScore = total == 0 ? 0 : (int)Math.Round(100 * earned / total);

        // 6. Keyword coverage score (exact matching, no AI)
        var keywords = extraction.Keywords
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25).ToList();

        foreach (var k in keywords)
            (ContainsKeyword(resumeNorm, k) ? result.MatchedKeywords : result.MissingKeywords).Add(k);

        result.KeywordScore = keywords.Count == 0
            ? result.SemanticScore
            : (int)Math.Round(100.0 * result.MatchedKeywords.Count / keywords.Count);

        // 7. Final blended score: 70% semantic, 30% keywords
        result.OverallScore = (int)Math.Round(0.7 * result.SemanticScore + 0.3 * result.KeywordScore);
        return result;
    }

    private async Task<T> AskAsync<T>(string prompt) where T : new()
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return JsonSerializer.Deserialize<T>(await AskJsonAsync(prompt), Opts) ?? new T(); }
            catch (JsonException) when (attempt < 2) { /* retry once */ }
        }
    }

    private async Task<string> AskJsonAsync(string prompt)
    {
        var res = await chat.GetResponseAsync(prompt);
        var t = res.Text.Trim();
        if (t.StartsWith("```"))
        {
            t = t[(t.IndexOf('\n') + 1)..];
            var end = t.LastIndexOf("```");
            if (end >= 0) t = t[..end];
        }
        return t.Trim();
    }

    private static string Norm(string s) =>
        Regex.Replace(s.ToLowerInvariant(), @"\s+", " ").Trim();

    private static bool EvidenceFound(string evidence, string resumeNorm)
    {
        var parts = evidence.Split(new[] { "...", "…" },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Any(p => p.Length >= 12 && resumeNorm.Contains(Norm(p)));
    }

    private static bool ContainsKeyword(string resumeNorm, string keyword)
    {
        var k = Norm(keyword);
        if (k.Length == 0) return false;
        return Regex.IsMatch(resumeNorm, $@"(?<![a-z0-9]){Regex.Escape(k)}(?![a-z0-9])");
    }
}