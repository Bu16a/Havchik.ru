using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace GeminiServer;

public class GeminiApi
{
    private readonly string _geminiApiKey;

    public GeminiApi(string geminiApiKey)
    {
        if (string.IsNullOrWhiteSpace(geminiApiKey))
            throw new ArgumentException("Gemini API key cannot be null or empty", nameof(geminiApiKey));

        _geminiApiKey = geminiApiKey;
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
            $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash-thinking-exp:generateContent?key={_geminiApiKey}";

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