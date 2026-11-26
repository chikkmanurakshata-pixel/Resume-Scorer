using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

var openAi = new OpenAIClient(
    new ApiKeyCredential(cfg["Gemini:ApiKey"]!),
    new OpenAIClientOptions { Endpoint = new Uri(cfg["Gemini:Endpoint"]!) });

builder.Services.AddChatClient(openAi.GetChatClient(cfg["Gemini:ChatModel"]!).AsIChatClient());
builder.Services.AddEmbeddingGenerator(
    openAi.GetEmbeddingClient(cfg["Gemini:EmbeddingModel"]!).AsIEmbeddingGenerator());

builder.Services.AddControllers();
builder.Services.AddSingleton<ResumeScorer.Api.Services.TextExtractor>();
builder.Services.AddSingleton<ResumeScorer.Api.Services.ScoringService>();
builder.Services.AddCors(o => o.AddPolicy("ui", p =>
    p.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors("ui");
app.MapControllers();
app.Run();