# Resume Scorer

An AI-powered tool that scores how well a resume fits a job description, explains why, and suggests what to fix. Upload a resume (PDF, DOCX or TXT), paste a job description, and get a match score with evidence for every requirement.

![Resume Scorer screenshot](docs/screenshot.png)

## Features

- Score from 0 to 100, blended from requirement match (70%) and keyword coverage (30%)
- Requirement-by-requirement breakdown: Strong, Partial or Missing, with the supporting phrase from your resume and the gap
- Keyword check showing which technologies from the job description appear in your resume
- Concrete suggestions to improve the resume for that role
- Friendly validation: empty files, unsupported types, scanned PDFs, oversized files, AI rate limits

## How the scoring works

The AI proposes, the code verifies. The final score is computed in C# and never by the language model.

1. **Extract**: read text from the resume (PdfPig for PDF, OpenXML for DOCX).
2. **Analyse the job description**: one LLM call returns the key requirements (marked Required or Preferred) and a list of technology keywords, as JSON.
3. **Chunk and embed**: split the resume into chunks and create embeddings for the chunks and for each requirement.
4. **Retrieve**: for each requirement, find the three most similar resume chunks using cosine similarity.
5. **Judge**: a second LLM call rates each requirement as Strong, Partial or Missing, and must quote exact evidence from the retrieved chunks.
6. **Verify**: if the quoted evidence does not appear in the resume text, the match is downgraded. This catches overly generous judgments.
7. **Score**: weighted by importance (Required counts double) for the requirement score, plus exact-match keyword coverage, blended 70/30.

## Tech stack

| Layer | Technology |
| --- | --- |
| Frontend | React, TypeScript, Vite |
| Backend | ASP.NET Core Web API (.NET 10), C# |
| AI abstraction | Microsoft.Extensions.AI (`IChatClient`, `IEmbeddingGenerator`) |
| Model | Google Gemini free tier, through its OpenAI-compatible endpoint |
| Parsing | PdfPig, DocumentFormat.OpenXml |
| Vector math | System.Numerics.Tensors |

Because the code depends only on the Microsoft.Extensions.AI interfaces, the model is a configuration detail. Swapping to Azure OpenAI, OpenAI, or a local Ollama model means changing the client registration in `Program.cs`, with no changes to the scoring logic.

## Project structure

```
ResumeScorer/
├── ResumeScorer.Api/          ASP.NET Core Web API
│   ├── Controllers/           ScoreController (upload and scoring endpoint)
│   ├── Services/              TextExtractor, Chunker, ScoringService
│   └── Models/                ScoreResult and related types
└── resume-scorer-ui/          React + Vite frontend
```

## Getting started

### Prerequisites

- .NET SDK 10 (or the version in `ResumeScorer.Api.csproj`)
- Node.js 20 or newer
- A free Gemini API key from [Google AI Studio](https://aistudio.google.com)

### 1. Run the API

```bash
cd ResumeScorer.Api
dotnet user-secrets init
dotnet user-secrets set "Gemini:ApiKey" "YOUR_KEY"
dotnet run
```

The API settings live in `appsettings.json`:

```json
"Gemini": {
  "Endpoint": "https://generativelanguage.googleapis.com/v1beta/openai/",
  "ChatModel": "gemini-3.1-flash-lite",
  "EmbeddingModel": "gemini-embedding-001"
}
```

Model names change over time, so check Google AI Studio for the current ones. The API key is stored with .NET user-secrets and is never committed to the repository.

### 2. Run the frontend

```bash
cd resume-scorer-ui
npm install
```

Create `resume-scorer-ui/.env` with the address your API listens on (see `launchSettings.json`):

```
VITE_API_URL=http://localhost:5256
```

Then start it:

```bash
npm run dev
```

Open the address Vite prints, usually `http://localhost:5173`.

## API

`POST /api/score` (multipart form data)

Returns the overall score, the requirement and keyword sub-scores, per-requirement matches with evidence and gaps, matched and missing keywords, and suggestions.

## Limitations

- Scanned or image-only PDFs are not supported, because text is read directly rather than through OCR.
- On the Gemini free tier, Google may use inputs to improve its models, and rate limits apply. Use sample resumes for testing.
- The score is a guide for improving your resume, not a prediction of any employer's screening result.

## Roadmap

- Save analysis history with EF Core and SQL Server
- Docker setup and cloud deployment
- Unit tests for the scoring service and text extraction

## License

MIT
