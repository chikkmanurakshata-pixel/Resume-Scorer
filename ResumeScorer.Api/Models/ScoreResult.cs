namespace ResumeScorer.Api.Models;

public class RequirementMatch
{
    public string Requirement { get; set; } = "";
    public string Importance { get; set; } = "Required";
    public string MatchLevel { get; set; } = "";   // Strong, Partial, Missing
    public string Evidence { get; set; } = "";
    public string Gap { get; set; } = "";
}

public class JdRequirement
{
    public string Text { get; set; } = "";
    public string Importance { get; set; } = "Required"; // Required or Preferred
}

public class AnalysisItem
{
    public int Index { get; set; }
    public string MatchLevel { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Gap { get; set; } = "";
}

public class AnalysisResponse
{
    public List<AnalysisItem> Matches { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
}
public class ScoreResult
{
    public int OverallScore { get; set; }
    public int SemanticScore { get; set; }
    public int KeywordScore { get; set; }
    public List<RequirementMatch> Matches { get; set; } = new();
    public List<string> MatchedSkills { get; set; } = new();
    public List<string> MissingSkills { get; set; } = new();
    public List<string> MatchedKeywords { get; set; } = new();
    public List<string> MissingKeywords { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
}

public class JdExtraction
{
    public List<JdRequirement> Requirements { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
}