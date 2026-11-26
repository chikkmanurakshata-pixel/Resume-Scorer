using System.ClientModel;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ResumeScorer.Api.Services;

namespace ResumeScorer.Api.Controllers;

[ApiController, Route("api/score")]
public class ScoreController(TextExtractor extractor, ScoringService scoring, ILogger<ScoreController> log) : ControllerBase
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly string[] Allowed = [".pdf", ".docx", ".txt"];

    [HttpPost]
    [RequestSizeLimit(MaxBytes + 1_000_000)]
    public async Task<IActionResult> Score([FromForm] IFormFile? resume, [FromForm] string? jobDescription)
    {
        if (resume is null) return BadRequest("No file was received.");
        if (resume.Length == 0) return BadRequest("The uploaded file is empty. Please choose a valid resume.");
        if (resume.Length > MaxBytes) return BadRequest("The file is too large. The limit is 5 MB.");

        var ext = Path.GetExtension(resume.FileName).ToLowerInvariant();
        if (!Allowed.Contains(ext)) return BadRequest("Unsupported file type. Please upload a PDF, DOCX or TXT file.");

        if (string.IsNullOrWhiteSpace(jobDescription)) return BadRequest("Please paste the job description.");
        if (jobDescription.Length < 100) return BadRequest("The job description looks too short. Please paste the full text.");
        if (jobDescription.Length > 15000) return BadRequest("The job description is too long (limit 15,000 characters).");

        string text;
        try { text = extractor.Extract(resume); }
        catch (NotSupportedException ex) { return BadRequest(ex.Message); }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not read file {Name}", resume.FileName);
            return UnprocessableEntity("We couldn't read this file. It may be corrupted or password-protected.");
        }

        if (text.Trim().Length < 200)
            return UnprocessableEntity("Very little text was found. If this is a scanned PDF, please upload a text-based PDF or a DOCX.");
        if (text.Length > 30000) text = text[..30000];

        try
        {
            return Ok(await scoring.ScoreAsync(text, jobDescription));
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            return StatusCode(429, "The AI service is busy (free-tier limit). Please wait a minute and try again.");
        }
        catch (ClientResultException ex)
        {
            log.LogError(ex, "AI service error");
            return StatusCode(502, "The AI service returned an error. Please try again.");
        }
        catch (JsonException ex)
        {
            log.LogError(ex, "AI returned invalid JSON");
            return StatusCode(502, "The AI returned an unexpected response. Please try again.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unexpected error while scoring");
            return StatusCode(500, "Something went wrong while scoring. Please try again.");
        }
    }
}