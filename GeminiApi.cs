using System;
using System.Net;
using System.Text;
using System.IO;
using System.Net.Http;
using Newtonsoft.Json;

namespace GeminiServer;

class GeminiApi
{
    private readonly string _geminiApiKey;

    public GeminiApi(string geminiApiKey)
    {
        _geminiApiKey = geminiApiKey;
    }

    public (HttpListenerResponse, string, HttpStatusCode) ProcessGeminiRequest(string request, HttpListenerResponse response)
    {
        if (string.IsNullOrWhiteSpace(request))
        {
            return (response, "Prompt should exist", HttpStatusCode.BadRequest);
        }

        string geminiResponse = CallGeminiApi(request);

        var responseData = new
        {
            generated_text = geminiResponse,
            timestamp = DateTime.UtcNow
        };

        return (response, JsonConvert.SerializeObject(responseData), HttpStatusCode.OK);
    }

    private string CallGeminiApi(string prompt)
    {
        Console.WriteLine($"Executing request to Gemini API with prompt: {prompt}");

        using var httpClient = new HttpClient();

        // Verify the API key is not null or empty
        if (string.IsNullOrWhiteSpace(_geminiApiKey))
        {
            throw new Exception("Gemini API key is not configured");
        }

        // Correct URL format for Gemini API
        string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-pro:generateContent?key={_geminiApiKey}";

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

        var jsonContent = new StringContent(
            JsonConvert.SerializeObject(requestBody),
            Encoding.UTF8,
            "application/json");

        try
        {
            var httpResponse = httpClient.PostAsync(apiUrl, jsonContent).Result;
            var responseBody = httpResponse.Content.ReadAsStringAsync().Result;

            if (!httpResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"API Error: {httpResponse.StatusCode} - {responseBody}");
                return $"API Error: {httpResponse.StatusCode} - {responseBody}";
            }

            dynamic geminiResponse = JsonConvert.DeserializeObject(responseBody);
            return geminiResponse?.candidates?[0]?.content?.parts?[0]?.text ?? "No response from Gemini API";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calling Gemini API: {ex.Message}");
            return $"Error: {ex.Message}";
        }
    }
}