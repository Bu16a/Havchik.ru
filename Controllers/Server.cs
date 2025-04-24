using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DotNetEnv;
using HavalNeGovno.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using NpgsqlTypes;
using static Google.Rpc.Context.AttributeContext.Types;

namespace Server;

class SimpleServer
{
    private readonly HttpListener _listener;
    private readonly string _url;
    private readonly IGeminiApi _geminiApi;
    private readonly IUrlParser _parser;
    private Task? _listenerTask;
    private readonly IDbService _dbService;
    private readonly ITranslator _translator;
    private CancellationTokenSource _cancellationTokenSource = new();
    private readonly SemaphoreSlim _semaphore = new (10);
    private readonly ILogger _logger;
    private readonly DBPrompts _dbPrompts;

    public SimpleServer(string url, IUrlParser parser, IGeminiApi geminiApi, IDbService dbService, ITranslator translator, ILogger logger)
    {
        Env.TraversePath().Load();
        _url = url;
        _parser = parser;
        _listener = new HttpListener();
        _listener.Prefixes.Add(_url);
        _listener.Prefixes.Add("http://*:8080/");
        _geminiApi = geminiApi;
        _dbService = dbService;
        _translator = translator;
        _logger = logger;
        _dbPrompts = new DBPrompts(_dbService);
    }

    public void Start()
    {
        _listener.Start();
        _logger.Log($"Server started and listens to: {_url}");
        _listenerTask = Listen(_cancellationTokenSource.Token);
        _listenerTask.ContinueWith(
            t => { _logger.Log($"Listener task terminated with exception: {t.Exception}"); },
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public void Stop()
    {
        _logger.Log("Stopping server...");
        _cancellationTokenSource.Cancel();
        _listener.Stop();

        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            _logger.Log("Listener task cancelled successfully.");
        }
        catch (AggregateException ae) when (ae.InnerExceptions.Count == 1 &&
                                            ae.InnerExceptions[0] is OperationCanceledException)
        {
            _logger.Log("Listener task cancelled successfully via aggregate exception.");
        }
        catch (Exception ex)
        {
            _logger.Log($"Exception during listener task wait: {ex.Message}");
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }

        _logger.Log("Server stopped");
    }

    private async Task Listen(CancellationToken cancellationToken)
    {
        while (_listener.IsListening && !cancellationToken.IsCancellationRequested)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                HttpListenerContext context = await _listener.GetContextAsync();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessRequestAsync(context);
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(
                            $"Error processing request task: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
                        if (context.Response.OutputStream.CanWrite)
                            SendResponse(context.Response, "Internal Server Error during request processing.",
                                HttpStatusCode.InternalServerError);
                    }
                    finally
                    {
                        context.Response.OutputStream.Close();
                    }
                }, cancellationToken);
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 995 && cancellationToken.IsCancellationRequested)
            {
                _logger.Log("Listener stopped receiving requests due to cancellation.");
                break;
            }
            catch (ObjectDisposedException)
            {
                _logger.Log("Listener has been disposed, likely during shutdown.");
                break;
            }
            catch (Exception ex)
            {
                _logger.Log($"Unexpected error in listener loop: {ex.Message}");
                await Task.Delay(1000, cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        _logger.Log("Listen loop finished.");
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        if (request.Url is null)
        {
            SendResponse(response, "URL is invalid", HttpStatusCode.BadRequest);
            return;
        }

        var endPoint = _parser.GetCheckpoint(request.Url.AbsolutePath);
        switch (request.HttpMethod.ToUpper())
        {
            case "GET":
                ProcessGet(endPoint, request, response);
                break;

            case "POST":
                await ProcessPostAsync(endPoint, request, response);
                break;

            default:
                SendResponse(response, "Only GET and POST are supported", HttpStatusCode.MethodNotAllowed);
                break;
        }
    }

    private void ProcessGet(string endPoint, HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.Url is null)
        {
            SendResponse(response, "URL is invalid", HttpStatusCode.BadRequest);
            return;
        }

        try
        {
            switch (endPoint)
            {
                case "/api/data":
                    ProcessGetApiData(request, response);
                    break;

                default:
                    SendResponse(response, "Endpoint not found", HttpStatusCode.NotFound);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"Error processing GET request: {ex.Message}");
            SendResponse(response, "Internal Server Error", HttpStatusCode.InternalServerError);
        }
    }

    private async Task ProcessGetApiData(HttpListenerRequest request, HttpListenerResponse response)
    {
        var parameters = _parser.GetParams(request);
        if (!parameters.ContainsKey("prompt") || string.IsNullOrWhiteSpace(parameters["prompt"]))
        {
            SendResponse(response, "Parameter 'prompt' is required", HttpStatusCode.BadRequest);
            return;
        }

        var prompt = parameters["prompt"];
        _logger.Log($"Received prompt: {prompt}");

        if (parameters.ContainsKey("iname"))
            _geminiApi.ChangeModel(parameters["iname"]);

        var apiCall = await _geminiApi.ProcessGeminiRequest(prompt, response);
        SendResponse(response, apiCall.Item2, apiCall.Item3, apiCall.Item1.ContentType);
    }

    private async Task ProcessPostAsync(string endPoint, HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            switch (endPoint)
            {
                case "/api/data":
                    await ProcessPostApiDataAsync(request, response);
                    break;
                case "/searchRecipe/data":
                    await ProcessPostSearchRecipeAsync(request, response);
                    break;
                case "/searchRecipebyid/data":
                    await ProcessPostSearchRecipeByIdAsync(request, response);
                    break;
                default:
                    SendResponse(response, "Endpoint not found", HttpStatusCode.NotFound);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Log(
                $"Unexpected Error during POST processing: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            SendResponse(response, $"Unexpected Error: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    private async Task ProcessPostSearchRecipeAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            var (success, jsonData, russianIngredients, recipeCount) = await ValidateRequestAsync(request, response);
            if (!success)
                return;

            var translatedIngredients = await TranslateIngredientsAsync(response, russianIngredients);
            if (translatedIngredients.Count == 0)
            {
                SendResponse(response, "Нет корректных ингредиентов после перевода.", HttpStatusCode.BadRequest);
                return;
            }

            var recipesData = await _dbPrompts.QueryRecipesFromDatabaseAsync(translatedIngredients, recipeCount);

            // var recipesResult = await ProcessRecipesAsync(recipesData);
            var recipesResult = await ProcessTranslateGemeniRecipesAsync(recipesData, response);

            SendResponse(response, JsonConvert.SerializeObject(recipesResult, Formatting.Indented), HttpStatusCode.OK,
                "application/json");
        }
        catch (NpgsqlException ex)
        {
            _logger.Log($"Ошибка запроса к базе данных: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            SendResponse(response, "Ошибка при запросе рецептов из базы данных.", HttpStatusCode.InternalServerError);
        }
        catch (Exception ex)
        {
            _logger.Log(
                $"Неожиданная ошибка в ProcessPostSearchRecipe: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            SendResponse(response, "Неожиданная ошибка сервера.", HttpStatusCode.InternalServerError);
        }
    }

    private async Task ProcessPostSearchRecipeByIdAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            var (success, jsonData, recipeId) = await ValideId(request, response);
            if (!success)
                return;

            var recipesData = await _dbPrompts.QueryRecipesByIdFromDatabaseAsync(recipeId);

            if (recipesData == null || recipesData.Count == 0)
            {
                SendResponse(response, "Рецепт не найден.", HttpStatusCode.NotFound);
                return;
            }

            var recipe = recipesData.First();
            SendResponse(response, JsonConvert.SerializeObject(recipe, Formatting.Indented),
                HttpStatusCode.OK, "application/json");
        }
        catch (NpgsqlException ex)
        {
            _logger.Log($"Ошибка запроса к базе данных: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            SendResponse(response, "Ошибка при запросе рецептов из базы данных.", HttpStatusCode.InternalServerError);
        }
        catch (Exception ex)
        {
            _logger.Log($"Неожиданная ошибка в ProcessPostSearchRecipe: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            SendResponse(response, "Неожиданная ошибка сервера.", HttpStatusCode.InternalServerError);
        }
    }

    private async Task<(bool, JObject, int)> ValideId(HttpListenerRequest request,
        HttpListenerResponse response)
    {
        JObject jsonData = null;
        int recipeId = 0;

        if (!IsValidJsonContentType(request))
        {
            SendResponse(response, "Требуется Content-Type: application/json", HttpStatusCode.UnsupportedMediaType);
            return (false, jsonData, recipeId);
        }

        var requestBody = await ReadRequestBodyAsync(request);

        if (!TryParseJson(requestBody, out jsonData, out string jsonErrorMessage))
        {
            _logger.Log($"Ошибка разбора JSON: {jsonErrorMessage}");
            SendResponse(response, $"Неверный формат JSON: {jsonErrorMessage}", HttpStatusCode.BadRequest);
            return (false, jsonData, recipeId);
        }

        if (!TryGetParam(jsonData, "id", out recipeId, out string countError) || recipeId <= 0)
        {
            SendResponse(response, countError,
                HttpStatusCode.BadRequest);
            return (false, jsonData, recipeId);
        }

        return (true, jsonData, recipeId);
    }

    private async Task<(bool, JObject, List<string>, int)> ValidateRequestAsync(HttpListenerRequest request,
        HttpListenerResponse response)
    {
        JObject jsonData = null;
        List<string> russianIngredients = null;
        int recipeCount = 0;

        if (!IsValidJsonContentType(request))
        {
            SendResponse(response, "Требуется Content-Type: application/json", HttpStatusCode.UnsupportedMediaType);
            return (false, jsonData, russianIngredients, recipeCount);
        }

        var requestBody = await ReadRequestBodyAsync(request);

        if (!TryParseJson(requestBody, out jsonData, out string jsonErrorMessage))
        {
            _logger.Log($"Ошибка разбора JSON: {jsonErrorMessage}");
            SendResponse(response, $"Неверный формат JSON: {jsonErrorMessage}", HttpStatusCode.BadRequest);
            return (false, jsonData, russianIngredients, recipeCount);
        }

        if (!TryGetParam(jsonData, "ingredients", out russianIngredients, out string ingredientsError) ||
            russianIngredients == null || russianIngredients.Count == 0)
        {
            SendResponse(response,
                ingredientsError,
                HttpStatusCode.BadRequest);
            return (false, jsonData, russianIngredients, recipeCount);
        }

        if (!TryGetParam(jsonData, "count", out recipeCount, out string countError) || recipeCount <= 0)
        {
            SendResponse(response, countError,
                HttpStatusCode.BadRequest);
            return (false, jsonData, russianIngredients, recipeCount);
        }

        return (true, jsonData, russianIngredients, recipeCount);
    }

    private async Task<List<string>> TranslateIngredientsAsync(HttpListenerResponse response,
        List<string> russianIngredients)
    {
        try
        {
            return await _translator.TranslateIngredientsAsync(
                russianIngredients,
                onError: () => SendResponse(response, message: "Ошибка при переводе ингредиентов.",
                    HttpStatusCode.InternalServerError));
        }
        catch (Exception ex)
        {
            _logger.Log($"Ошибка перевода: {ex.Message}");
            throw;
        }
    }

    private async Task<Dictionary<string, Dictionary<string, object>>> ProcessRecipesAsync(
        List<Dictionary<string, object>> recipesData)
    {
        var recipesResult = new Dictionary<string, Dictionary<string, object>>();
        var keysToTranslate = new HashSet<string>();
        // var keysToTranslate = new HashSet<string> { "title", "ingredients", "directions", "source", "ner_ingredients" };

        int recipeIndex = 1;
        foreach (var recipeData in recipesData)
        {
            var translationTasks = recipeData.Keys
                .Where(key => keysToTranslate.Contains(key) && recipeData[key] != null)
                .Select(async key => { recipeData[key] = await _translator.TranslateValueAsync(recipeData[key], "en", "ru"); });

            await Task.WhenAll(translationTasks);

            recipesResult[$"recipe_{recipeIndex++}"] = recipeData;
        }

        _logger.Log($"Найдено и переведено {recipesResult.Count} рецептов.");
        return recipesResult;
    }

    private async Task<Dictionary<string, Dictionary<string, object>>> ProcessTranslateGemeniRecipesAsync(
        List<Dictionary<string, object>> recipesData, HttpListenerResponse response)
    {
        var recipesResult = new Dictionary<string, Dictionary<string, object>>();
        var keysToTranslate = new HashSet<string>();
        // var keysToTranslate = new HashSet<string> { "title", "ingredients", "directions", "source", "ner_ingredients" };
        // var apiResult = await _geminiApi.ProcessGeminiRequest($"Переведи данный текст в том же формате в котором он тебе поступил {recipesResult}", response);

        var dataForGemini = new Dictionary<string, Dictionary<string, object>>();
        for (int i = 0; i < recipesData.Count; i++)
        {
            var recipe = recipesData[i];
            var translatableFields = new Dictionary<string, object>();
            foreach (var key in keysToTranslate)
            {
                if (recipe.TryGetValue(key, out var value) && value != null)
                {
                    if (value is string strValue && !string.IsNullOrWhiteSpace(strValue))
                        translatableFields[key] = strValue;
                    else if (value is IEnumerable<string> listValue)
                    {
                        var nonEmptyList = listValue.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                        if (nonEmptyList.Any())
                            translatableFields[key] = nonEmptyList;
                    }
                    else if (value is JsonElement jsonElement) 
                    {
                        if (jsonElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(jsonElement.GetString()))
                            translatableFields[key] = jsonElement.GetString();
                        else if (jsonElement.ValueKind == JsonValueKind.Array)
                        {
                            var list = jsonElement.EnumerateArray()
                                                .Select(e => e.GetString())
                                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                                .ToList();
                            if (list.Any())
                                translatableFields[key] = list;
                        }
                    }
                }
            }
            if (translatableFields.Any())
                dataForGemini[$"recipe_{i + 1}"] = translatableFields;
        }

        string jsonPayload = System.Text.Json.JsonSerializer.Serialize(dataForGemini, new JsonSerializerOptions { WriteIndented = true });

        string prompt = $"""
        Translate the text values for the keys {string.Join(", ", keysToTranslate.Select(k => $"'{k}'"))} within the following JSON structure from English to Russian.
        Return the response as a JSON object with the exact same structure (including the top-level keys like "recipe_1", "recipe_2", etc.), containing the translations.
        Do not translate the keys themselves. Ensure lists of strings remain lists of strings in the output. Also, if you see extra service characters in the text, remove them.

        Input JSON:
        ```json
        {jsonPayload}
        ```

        Translated JSON Output:
        """;

        string? rawGeminiResponseString = null; 
        string geminiResponseJson; 
        try
        {
            var apiResultTuple = await _geminiApi.ProcessGeminiRequest(prompt, response);
            rawGeminiResponseString = apiResultTuple.Item2;
            using JsonDocument document = JsonDocument.Parse(rawGeminiResponseString);
            document.RootElement.TryGetProperty("generated_text", out var generatedTextElement);
            geminiResponseJson = generatedTextElement.GetString().Trim();
            if (geminiResponseJson.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                geminiResponseJson = geminiResponseJson.Substring(7);
            if (geminiResponseJson.EndsWith("```"))
                geminiResponseJson = geminiResponseJson.Substring(0, geminiResponseJson.Length - 3);
            geminiResponseJson = geminiResponseJson.Trim();
        }
        catch (Exception ex)
        {
            return GenerateErrorResult(recipesData, ex);
        }

        Dictionary<string, Dictionary<string, JsonElement>>? translatedData = null;
        try
        {
            translatedData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(geminiResponseJson);
            if (translatedData == null) 
                throw new System.Text.Json.JsonException("Deserialization of translated data resulted in null.");
        }
        catch (System.Text.Json.JsonException jsonEx)
        {
            return GenerateErrorResult(recipesData, jsonEx);
        }

        var finalRecipesResult = new Dictionary<string, Dictionary<string, object>>();
        for (int i = 0; i < recipesData.Count; i++)
        {
            string recipeKey = $"recipe_{i + 1}";
            var originalRecipe = recipesData[i];
            var processedRecipe = new Dictionary<string, object>(originalRecipe);

            if (translatedData.TryGetValue(recipeKey, out var translations))
                foreach (var key in keysToTranslate)
                    if (translations.TryGetValue(key, out var translatedValueElement) && originalRecipe.ContainsKey(key))
                    {
                        if (translatedValueElement.ValueKind == JsonValueKind.String)
                            processedRecipe[key] = translatedValueElement.GetString();
                        else if (translatedValueElement.ValueKind == JsonValueKind.Array)
                        {
                            processedRecipe[key] = translatedValueElement.EnumerateArray()
                                .Select(e => e.GetString())
                                .ToList();
                        }
                    }

            finalRecipesResult[recipeKey] = processedRecipe;
        }
        _logger.Log($"Найдено и переведено {finalRecipesResult.Count} рецептов.");
        return finalRecipesResult;
    }

    private Dictionary<string, Dictionary<string, object>> GenerateErrorResult(List<Dictionary<string, object>> recipesData, Exception ex)
    {
        _logger.Log($"Ошибка при обработке ответа Gemini: {ex.Message}");
        var errorResult = new Dictionary<string, Dictionary<string, object>>();
        for (int i = 0; i < recipesData.Count; i++)
            errorResult[$"recipe_{i + 1}"] = recipesData[i];
        return errorResult;
    }

    private async Task ProcessPostApiDataAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (!IsValidJsonContentType(request))
        {
            SendResponse(response, "Content-Type: application/json needed", HttpStatusCode.UnsupportedMediaType);
            return;
        }

        string requestBody = await ReadRequestBodyAsync(request);

        if (!TryParseJson(requestBody, out JObject jsonData, out string errorMessage))
        {
            _logger.Log($"JSON Parsing Error: {errorMessage}");
            SendResponse(response, "Wrong JSON format", HttpStatusCode.BadRequest);
            return;
        }

        if (!TryGetParam<string>(jsonData, "prompt", out string prompt, out errorMessage))
        {
            SendResponse(response, errorMessage, HttpStatusCode.BadRequest);
            return;
        }

        if (TryGetParam<string>(jsonData, "iname", out string iname, out _))
            _geminiApi.ChangeModel(iname);

        var apiResult = await _geminiApi.ProcessGeminiRequest(prompt, response);
        SendResponse(apiResult.Item1, apiResult.Item2, apiResult.Item3, apiResult.Item1.ContentType);
    }


    private bool IsValidJsonContentType(HttpListenerRequest request)
    {
        return request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private async Task<string> ReadRequestBodyAsync(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private bool TryParseJson(string json, out JObject jsonData, out string errorMessage)
    {
        jsonData = null;
        errorMessage = null;
        try
        {
            jsonData = JObject.Parse(json);
            return true;
        }
        catch (JsonReaderException ex)
        {
            errorMessage = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"An unexpected error occurred during JSON parsing: {ex.Message}";
            return false;
        }
    }

    private bool TryGetParam<T>(JObject jsonData, string paramName, out T value, out string errorMessage)
    {
        value = default;
        errorMessage = null;

        if (!jsonData.TryGetValue(paramName, StringComparison.OrdinalIgnoreCase, out JToken token))
        {
            errorMessage = $"Parameter '{paramName}' is required.";
            return false;
        }

        if (token.Type == JTokenType.Null && default(T) != null)
        {
            errorMessage = $"Parameter '{paramName}' cannot be null.";
            return false;
        }

        try
        {
            value = token.ToObject<T>();
            if (value is null && default(T) != null)
            {
                errorMessage = $"Parameter '{paramName}' could not be converted to the required type or is null.";
                return false;
            }

            return true;
        }
        catch (System.Text.Json.JsonException ex)
        {
            errorMessage =
                $"Invalid format for parameter '{paramName}'. Expected type: {typeof(T).Name}. Error: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"An unexpected error occurred while retrieving parameter '{paramName}': {ex.Message}";
            return false;
        }
    }


    private void SendResponse(HttpListenerResponse response, string message, HttpStatusCode statusCode,
        string contentType = "text/plain; charset=utf-8")
    {
        if (!response.OutputStream.CanWrite)
        {
            _logger.Log(
                $"Warning: Attempted to write response after headers were sent or stream was closed. Status: {statusCode}, Message: {message.Substring(0, Math.Min(message.Length, 100))}");
            return;
        }

        try
        {
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept");
            response.AddHeader("Access-Control-Max-Age", "86400"); 

            if (statusCode == HttpStatusCode.OK && string.IsNullOrEmpty(message))
            {
                response.StatusCode = (int)HttpStatusCode.NoContent;
                response.Close();
                return;
            }
            var buffer = Encoding.UTF8.GetBytes(message);
            response.StatusCode = (int)statusCode;
            response.ContentType = contentType;
            response.ContentLength64 = buffer.Length;
            response.ContentEncoding = Encoding.UTF8;

            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        catch (ObjectDisposedException)
        {
            _logger.Log("Warning: Response stream was disposed before SendResponse could complete.");
        }
        catch (Exception ex)
        {
            _logger.Log($"Error sending response: {ex.Message}");
        }
    }
}