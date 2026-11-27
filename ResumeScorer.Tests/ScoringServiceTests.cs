using Microsoft.Extensions.AI;
using Moq;
using ResumeScorer.Api.Models;
using ResumeScorer.Api.Services;
using Xunit;

namespace ResumeScorer.Tests;

public class ScoringServiceTests
{
    // A resume line the "LLM evidence" can quote exactly.
    private const string Resume =
        "Software Engineer with 5 years building enterprise APIs in C# and SQL Server.";
    private const string RealEvidence = "5 years building enterprise APIs in C# and SQL Server";

    private const string ReqsWithKeywords = """
        {"requirements":[
           {"text":"C# and .NET experience","importance":"Required"},
           {"text":"SQL databases","importance":"Preferred"}],
         "keywords":["C#","SQL","Docker"]}
        """;

    private const string AllStrong = """
        {"matches":[
           {"index":0,"matchLevel":"Strong","evidence":"5 years building enterprise APIs in C# and SQL Server","gap":""},
           {"index":1,"matchLevel":"Strong","evidence":"5 years building enterprise APIs in C# and SQL Server","gap":""}],
         "suggestions":["Add Docker experience"]}
        """;

    private const string OneRequired = """
        {"requirements":[{"text":"Azure experience","importance":"Required"}],"keywords":[]}
        """;

    // ---------- helpers ----------

    private static ChatResponse Reply(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text));

    /// <summary>
    /// Builds the service with a fake chat client that returns the given replies in order
    /// (call 1 = requirement extraction, call 2 = match analysis) and a fake embedding
    /// generator that returns the same vector for everything.
    /// </summary>
    private static (ScoringService Service, Mock<IChatClient> Chat) Build(params string[] replies)
    {
        var chat = new Mock<IChatClient>();
        var sequence = chat.SetupSequence(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()));
        foreach (var r in replies) sequence.ReturnsAsync(Reply(r));

        var embedder = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        embedder
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> values, EmbeddingGenerationOptions? opts, CancellationToken ct) =>
                new GeneratedEmbeddings<Embedding<float>>(
                    values.Select(v => new Embedding<float>(new float[] { 1f, 0f, 0f })).ToList()));

        return (new ScoringService(chat.Object, embedder.Object), chat);
    }

    private static Task<ScoreResult> RunAsync(string resume, string reqsJson, string analysisJson) =>
        Build(reqsJson, analysisJson).Service.ScoreAsync(resume, "any job description text");

    // ---------- tests ----------

    [Fact]
    public async Task VerifiedStrongMatches_ScoreHighAndBlendKeywords()
    {
        var result = await RunAsync(Resume, ReqsWithKeywords, AllStrong);

        Assert.Equal(100, result.SemanticScore);            // both requirements Strong
        Assert.Equal(67, result.KeywordScore);              // C# and SQL found, Docker missing = 2 of 3
        Assert.Equal(90, result.OverallScore);              // 70% of 100 + 30% of 67
        Assert.Contains("C#", result.MatchedKeywords);
        Assert.Contains("SQL", result.MatchedKeywords);
        Assert.Contains("Docker", result.MissingKeywords);
        Assert.Equal(2, result.MatchedSkills.Count);
    }

    [Fact]
    public async Task StrongMatchWithFabricatedEvidence_IsDowngradedToPartial()
    {
        const string analysis = """
            {"matches":[{"index":0,"matchLevel":"Strong","evidence":"deployed to Azure Kubernetes at scale","gap":""}],
             "suggestions":[]}
            """;

        var result = await RunAsync(Resume, OneRequired, analysis);

        var match = Assert.Single(result.Matches);
        Assert.Equal("Partial", match.MatchLevel);          // the quote is not in the resume
        Assert.Equal(50, result.SemanticScore);
        Assert.Empty(result.MatchedSkills);
    }

    [Fact]
    public async Task PartialMatchWithFabricatedEvidence_IsDowngradedToMissing()
    {
        const string analysis = """
            {"matches":[{"index":0,"matchLevel":"Partial","evidence":"managed Azure subscriptions for the team","gap":""}],
             "suggestions":[]}
            """;

        var result = await RunAsync(Resume, OneRequired, analysis);

        Assert.Equal("Missing", Assert.Single(result.Matches).MatchLevel);
        Assert.Equal(0, result.SemanticScore);
        Assert.Contains("Azure experience", result.MissingSkills);
    }

    [Fact]
    public async Task RequiredRequirementsCountDoubleComparedToPreferred()
    {
        const string reqs = """
            {"requirements":[
               {"text":"Azure experience","importance":"Required"},
               {"text":"SQL databases","importance":"Preferred"}],
             "keywords":[]}
            """;
        const string analysis = """
            {"matches":[
               {"index":0,"matchLevel":"Missing","evidence":"","gap":"No Azure"},
               {"index":1,"matchLevel":"Strong","evidence":"5 years building enterprise APIs in C# and SQL Server","gap":""}],
             "suggestions":[]}
            """;

        var result = await RunAsync(Resume, reqs, analysis);

        // earned = 0*2 + 1*1 = 1, total = 2 + 1 = 3  ->  33
        Assert.Equal(33, result.SemanticScore);
    }

    [Fact]
    public async Task NoRequirementsFound_ThrowsInvalidOperationException()
    {
        var (service, _) = Build("""{"requirements":[],"keywords":["C#"]}""");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ScoreAsync(Resume, "any job description text"));
    }

    [Fact]
    public async Task InvalidJsonFromAi_IsRetriedOnce()
    {
        var (service, chat) = Build("this is not json", ReqsWithKeywords, AllStrong);

        var result = await service.ScoreAsync(Resume, "any job description text");

        Assert.Equal(100, result.SemanticScore);
        chat.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3)); // failed try, retry, analysis call
    }

    [Fact]
    public async Task JsonWrappedInMarkdownFences_IsStillParsed()
    {
        var fenced = "```json\n" + AllStrong + "\n```";

        var result = await RunAsync(Resume, ReqsWithKeywords, fenced);

        Assert.Equal(100, result.SemanticScore);
    }

    [Fact]
    public async Task KeywordMatching_RespectsWordBoundaries()
    {
        const string reqs = """
            {"requirements":[{"text":"Web development","importance":"Required"}],
             "keywords":["Java","JavaScript"]}
            """;
        const string analysis = """
            {"matches":[{"index":0,"matchLevel":"Missing","evidence":"","gap":"none"}],"suggestions":[]}
            """;

        var result = await RunAsync("Built web apps in JavaScript and React.", reqs, analysis);

        Assert.Contains("JavaScript", result.MatchedKeywords);
        Assert.Contains("Java", result.MissingKeywords);    // "Java" must not match inside "JavaScript"
    }

    [Fact]
    public async Task MatchWithOutOfRangeIndex_IsIgnored()
    {
        const string analysis = """
            {"matches":[{"index":99,"matchLevel":"Strong","evidence":"5 years building enterprise APIs in C# and SQL Server","gap":""}],
             "suggestions":[]}
            """;

        var result = await RunAsync(Resume, OneRequired, analysis);

        Assert.Empty(result.Matches);
        Assert.Equal(0, result.SemanticScore);
    }
}