using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using GenerativeAI.Types;
using Newtonsoft.Json;
using static GenerativeAI.VertexAIModels;

namespace GeminiServer;

public class GeminiApi
{
    private readonly string _geminiApiKey;
    private string _model;
    private readonly string[] _models =
    {
        "models/chat-bison-001",
        "models/text-bison-001",
        "models/gemini-1.5-pro-latest",
        "models/gemini-1.5-pro-001",
        "models/gemini-1.5-pro-002",
        "models/gemini-1.5-pro",
        "models/gemini-1.5-flash-latest",
        "models/gemini-1.5-flash-001",
        "models/gemini-1.5-flash",
        "models/gemini-1.5-flash-002",
        "models/gemini-1.5-flash-8b",
        "models/gemini-1.5-flash-8b-001",
        "models/gemini-1.5-flash-8b-latest",
        "models/gemini-2.5-pro-exp-03-25",
        "models/gemini-2.0-flash",
        "models/gemini-2.0-flash-001",
        "models/gemini-2.0-flash-lite-001",
        "models/gemini-2.0-flash-lite",
        "models/gemma-3-4b-it",
        "models/gemma-3-12b-it",
        "models/gemma-3-27b-it"
    };

    public GeminiApi(string geminiApiKey, string model = "gemini-2.0-flash-thinking-exp")
    {
        if (string.IsNullOrWhiteSpace(geminiApiKey))
            throw new ArgumentException("Gemini API key cannot be null or empty", nameof(geminiApiKey));

        _geminiApiKey = geminiApiKey;
        _model = model;
    }

    public void ChangeModel(string name)
    {
        if (!_models.Contains(name))
            _model = "models/gemini-2.0-flash";
        else
            _model = name;
        Console.WriteLine($"Model changed to {_model}");
    }

    public async Task<(HttpListenerResponse response, string responseBody, HttpStatusCode statusCode)>
        ProcessGeminiRequest(
            string prompt,
            HttpListenerResponse response)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return (response, "Prompt is required", HttpStatusCode.BadRequest);

        try
        {
            string geminiResponse = await CallGeminiApi(prompt);

            var responseData = new
            {
                generated_text = geminiResponse,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            return (response, JsonConvert.SerializeObject(responseData), HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            return (response, $"Internal Server Error: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    private async Task<string> CallGeminiApi(string prompt)
    {
        using var httpClient = new HttpClient();

        string apiUrl =
            $"https://generativelanguage.googleapis.com/v1beta/{_model}:generateContent?key={_geminiApiKey}";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            }
        };

        string jsonRequestBody = JsonConvert.SerializeObject(requestBody);

        var jsonContent = new StringContent(jsonRequestBody, Encoding.UTF8, "application/json");
        HttpResponseMessage httpResponse = await httpClient.PostAsync(apiUrl, jsonContent);

        string responseBody = await httpResponse.Content.ReadAsStringAsync();

        if (!httpResponse.IsSuccessStatusCode)
            throw new Exception($"API Error: {httpResponse.StatusCode} - {responseBody}");

        dynamic geminiResponse = JsonConvert.DeserializeObject(responseBody);
        string generatedText = geminiResponse?.candidates?[0]?.content?.parts?[0]?.text;

        if (string.IsNullOrEmpty(generatedText))
            throw new Exception("No valid response from Gemini API");

        return generatedText;
    }
}