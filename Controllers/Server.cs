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
    private readonly SemaphoreSlim _semaphore = new(10);
    private readonly ILogger _logger;
    private readonly DBPrompts _dbPrompts;
    private readonly IJsonServing _jsonServing;
    private readonly IGoogleImageSearchHelper _googleImageSearchHelper;

    public SimpleServer(string url, IUrlParser parser, IGeminiApi geminiApi, IDbService dbService,
        ITranslator translator,
        ILogger logger, IJsonServing jsonServing, IGoogleImageSearchHelper googleImageSearchHelper)
    {
        Env.TraversePath().Load();
        _url = url;
        _parser = parser;
        _listener = new HttpListener();
        _listener.Prefixes.Add(_url);
        _listener.Prefixes.Add("http://*:5252/");
        _geminiApi = geminiApi;
        _dbService = dbService;
        _translator = translator;
        _logger = logger;
        _dbPrompts = new DBPrompts(_dbService);
        _jsonServing = jsonServing;
        _googleImageSearchHelper = googleImageSearchHelper;
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

        _logger.Log($"Received {request.HttpMethod} request for {request.Url}");

        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept");
        response.Headers.Add("Access-Control-Max-Age", "86400");


        if (request.HttpMethod.ToUpper() == "OPTIONS")
        {
            _logger.Log($"Handling OPTIONS request for {request.Url?.LocalPath}");
            response.StatusCode = (int)HttpStatusCode.NoContent;
            response.Close();
            return;
        }

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
                case "/getRecipesBuyOrNo/data":
                    await ProcessPostSearchRecipesForByeOrNo(request, response);
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

    private async Task ProcessPostSearchRecipesForByeOrNo(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            var (success, jsonData, ingredients, allergens, recipeCount, purchase, sortBy, page) =
                await ValidateRequestRecipesOrNOAsync(request, response);
            if (!success)
                return;

            List<Dictionary<string, object>>? recipesData;
            if (purchase)
                recipesData = await _dbPrompts.QueryRecipesFromDatabaseAsync(ingredients, allergens, recipeCount, sortBy, page);
            else
                recipesData =
                    await _dbPrompts.QueryRecipesOnlyTheseProductsFromDatabaseAsync(ingredients, allergens, recipeCount, sortBy,
                        page);

            // if (recipesData.Count == 0)
            // {
            //     var random = new Random();
            //     recipesData = await _dbPrompts.QueryRecipesByIdFromDatabaseAsync(random.Next(1, 10000));
            // }
            var recipesResult = await ProcessRecipesAsync(recipesData);


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

    private async Task<(bool, JObject, List<string>, List<string>, int, bool, string, int)> ValidateRequestRecipesOrNOAsync(
        HttpListenerRequest request,
        HttpListenerResponse response)
    {
        var jsonData = new JObject();
        var ingredients = new List<string>();
        var allergens = new List<string>();
        var recipeCount = 0;
        var purchase = false;
        var sortBy = "relevance";
        var page = 1;
        

        if (!_jsonServing.IsValidJsonContentType(request))
        {
            SendResponse(response, "Требуется Content-Type: application/json", HttpStatusCode.UnsupportedMediaType);
            return (false, jsonData, ingredients, allergens, recipeCount, purchase, sortBy, page);
        }

        var requestBody = await ReadRequestBodyAsync(request);

        if (!_jsonServing.TryParseJson(requestBody, out jsonData, out string jsonErrorMessage))
        {
            _logger.Log($"Ошибка разбора JSON: {jsonErrorMessage}");
            SendResponse(response, $"Неверный формат JSON: {jsonErrorMessage}", HttpStatusCode.BadRequest);
            return (false, jsonData, ingredients, allergens, recipeCount, purchase, sortBy, page);
        }

        if (!_jsonServing.TryGetParam(jsonData, "ingredients", out ingredients, out string ingredientsError) ||
            ingredients == null)
        {
            SendResponse(response,
                ingredientsError,
                HttpStatusCode.BadRequest);
            return (false, jsonData, ingredients, allergens, recipeCount, purchase, sortBy, page);
        }
        
        if (!_jsonServing.TryGetParam(jsonData, "allergens", out allergens, out string allergensError) ||
            allergens == null)
        {
            SendResponse(response,
                allergensError,
                HttpStatusCode.BadRequest);
            return (false, jsonData, ingredients, allergens, recipeCount, purchase, sortBy, page);
        }

        if (!_jsonServing.TryGetParam(jsonData, "count", out recipeCount, out string countError) || recipeCount <= 0)
        {
            SendResponse(response, countError,
                HttpStatusCode.BadRequest);
            return (false, jsonData, ingredients, allergens, recipeCount, purchase, sortBy, page);
        }

        if (!_jsonServing.TryGetParam(jsonData, "purchase", out purchase, out string purchasetError))
        {
            SendResponse(response, purchasetError,
                HttpStatusCode.BadRequest);
            return (false, jsonData, ingredients, allergens, recipeCount, purchase, sortBy, page);
        }
        
        if (!_jsonServing.TryGetParam(jsonData, "sortBy", out sortBy, out string sortByError))
        {
            SendResponse(response, sortByError,
                HttpStatusCode.BadRequest);
            return (false, jsonData, ingredients, allergens, recipeCount, purchase, sortBy, page);
        }
        
        if (!_jsonServing.TryGetParam(jsonData, "page", out page, out string pageError))
        {
            SendResponse(response, pageError,
                HttpStatusCode.BadRequest);
            return (false, jsonData, ingredients, allergens, recipeCount, purchase, sortBy, page);
        }

        return (true, jsonData, ingredients, allergens, recipeCount, purchase, sortBy, page);
    }

    private async Task ProcessPostSearchRecipeAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            var (success, jsonData, ingredients, allergens, recipeCount) = await ValidateRequestAsync(request, response);
            if (!success)
                return;

            var recipesData = await _dbPrompts.QueryRecipesFromDatabaseAsync(ingredients, allergens, recipeCount);
            var recipesResult = await ProcessRecipesAsync(recipesData);

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
            
            if (recipesData.Count == 1 && recipesData[0]["image"] == null)
            {
                var searchQuery = $"{recipesData[0]["title"]} recipe";
                var imageUrl = await _googleImageSearchHelper.GetFirstImageUrlAsync(searchQuery);
                recipesData[0]["image"] = imageUrl;
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
            _logger.Log(
                $"Неожиданная ошибка в ProcessPostSearchRecipe: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            SendResponse(response, "Неожиданная ошибка сервера.", HttpStatusCode.InternalServerError);
        }
    }

    private async Task<(bool, JObject, int)> ValideId(HttpListenerRequest request,
        HttpListenerResponse response)
    {
        JObject jsonData = null;
        int recipeId = 0;

        if (!_jsonServing.IsValidJsonContentType(request))
        {
            SendResponse(response, "Требуется Content-Type: application/json", HttpStatusCode.UnsupportedMediaType);
            return (false, jsonData, recipeId);
        }

        var requestBody = await ReadRequestBodyAsync(request);

        if (!_jsonServing.TryParseJson(requestBody, out jsonData, out string jsonErrorMessage))
        {
            _logger.Log($"Ошибка разбора JSON: {jsonErrorMessage}");
            SendResponse(response, $"Неверный формат JSON: {jsonErrorMessage}", HttpStatusCode.BadRequest);
            return (false, jsonData, recipeId);
        }

        if (!_jsonServing.TryGetParam(jsonData, "id", out recipeId, out string countError) || recipeId <= 0)
        {
            SendResponse(response, countError,
                HttpStatusCode.BadRequest);
            return (false, jsonData, recipeId);
        }

        return (true, jsonData, recipeId);
    }

    private async Task<(bool, JObject, List<string>, List<string>, int)> ValidateRequestAsync(HttpListenerRequest request,
        HttpListenerResponse response)
    {
        JObject jsonData = null;
        List<string> ingredients = null;
        List<string> allergens = null;
        int recipeCount = 0;

        if (!_jsonServing.IsValidJsonContentType(request))
        {
            SendResponse(response, "Требуется Content-Type: application/json", HttpStatusCode.UnsupportedMediaType);
            return (false, jsonData, ingredients, allergens, recipeCount);
        }

        var requestBody = await ReadRequestBodyAsync(request);

        if (!_jsonServing.TryParseJson(requestBody, out jsonData, out string jsonErrorMessage))
        {
            _logger.Log($"Ошибка разбора JSON: {jsonErrorMessage}");
            SendResponse(response, $"Неверный формат JSON: {jsonErrorMessage}", HttpStatusCode.BadRequest);
            return (false, jsonData, ingredients, allergens, recipeCount);
        }

        if (!_jsonServing.TryGetParam(jsonData, "ingredients", out ingredients, out string ingredientsError) ||
            ingredients == null || ingredients.Count == 0)
        {
            SendResponse(response,
                ingredientsError,
                HttpStatusCode.BadRequest);
            return (false, jsonData, ingredients, allergens, recipeCount);
        }

        if (!_jsonServing.TryGetParam(jsonData, "count", out recipeCount, out string countError) || recipeCount <= 0)
        {
            SendResponse(response, countError,
                HttpStatusCode.BadRequest);
            return (false, jsonData, ingredients, allergens, recipeCount);
        }

        return (true, jsonData, ingredients, allergens, recipeCount);
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
        var recipeIndex = 1;
        foreach (var recipeData in recipesData)
        {
            if (recipeData["image"] == null)
            {
                var searchQuery = $"{recipeData["title"]} recipe";
                var imageUrl = await _googleImageSearchHelper.GetFirstImageUrlAsync(searchQuery);
                recipeData["image"] = imageUrl;
            }
            recipesResult[$"recipe_{recipeIndex++}"] = recipeData;
        }

        _logger.Log($"Найдено и переведено {recipesResult.Count} рецептов.");
        return recipesResult;
    }

    private async Task<Dictionary<string, Dictionary<string, object>>> ProcessTranslateGemeniRecipesAsync(
        List<Dictionary<string, object>> recipesData, HttpListenerResponse response)
    {
        var recipesResult = new Dictionary<string, Dictionary<string, object>>();
        var keysToTranslate = new HashSet<string> { "title", "ingredients", "directions", "source", "ner_ingredients" };
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
                        if (jsonElement.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(jsonElement.GetString()))
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

        string jsonPayload =
            System.Text.Json.JsonSerializer.Serialize(dataForGemini,
                new JsonSerializerOptions { WriteIndented = true });

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
            translatedData =
                System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(
                    geminiResponseJson);
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

            if (processedRecipe.TryGetValue("title", out var dishNameObj) && dishNameObj is string dishName &&
                !string.IsNullOrWhiteSpace(dishName))
            {
                string searchQuery = dishName + " recipe";
                string? imageUrl = await _googleImageSearchHelper.GetFirstImageUrlAsync(searchQuery);

                if (!string.IsNullOrEmpty(imageUrl))
                    processedRecipe["image"] = imageUrl;
                else
                    processedRecipe["image"] = null;
            }

            if (translatedData.TryGetValue(recipeKey, out var translations))
                foreach (var key in keysToTranslate)
                    if (translations.TryGetValue(key, out var translatedValueElement) &&
                        originalRecipe.ContainsKey(key))
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

    private Dictionary<string, Dictionary<string, object>> GenerateErrorResult(
        List<Dictionary<string, object>> recipesData, Exception ex)
    {
        _logger.Log($"Ошибка при обработке ответа Gemini: {ex.Message}");
        var errorResult = new Dictionary<string, Dictionary<string, object>>();
        for (int i = 0; i < recipesData.Count; i++)
            errorResult[$"recipe_{i + 1}"] = recipesData[i];
        return errorResult;
    }

    private async Task ProcessPostApiDataAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (!_jsonServing.IsValidJsonContentType(request))
        {
            SendResponse(response, "Content-Type: application/json needed", HttpStatusCode.UnsupportedMediaType);
            return;
        }

        string requestBody = await ReadRequestBodyAsync(request);

        if (!_jsonServing.TryParseJson(requestBody, out JObject jsonData, out string errorMessage))
        {
            _logger.Log($"JSON Parsing Error: {errorMessage}");
            SendResponse(response, "Wrong JSON format", HttpStatusCode.BadRequest);
            return;
        }

        if (!_jsonServing.TryGetParam<string>(jsonData, "prompt", out string prompt, out errorMessage))
        {
            SendResponse(response, errorMessage, HttpStatusCode.BadRequest);
            return;
        }

        if (_jsonServing.TryGetParam<string>(jsonData, "iname", out string iname, out _))
            _geminiApi.ChangeModel(iname);

        var apiResult = await _geminiApi.ProcessGeminiRequest(prompt, response);
        SendResponse(apiResult.Item1, apiResult.Item2, apiResult.Item3, apiResult.Item1.ContentType);
    }


    private async Task<string> ReadRequestBodyAsync(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private void SendResponse(HttpListenerResponse response, string message, HttpStatusCode statusCode,
        string contentType = "text/plain; charset=utf-8")
    {
        if (!response.OutputStream.CanWrite)
        {
            _logger.Log(
                $"Warning: Attempted to write response after headers were sent or stream was closed. Status: {statusCode}, Message: {message?.Substring(0, Math.Min(message.Length, 100)) ?? "null"}");
            return;
        }

        try
        {
            if (statusCode == HttpStatusCode.OK && string.IsNullOrEmpty(message))
            {
                response.StatusCode = (int)HttpStatusCode.NoContent;
                response.Close();
                return;
            }

            var buffer = Encoding.UTF8.GetBytes(message ?? string.Empty);
            response.StatusCode = (int)statusCode;
            response.ContentType = contentType;
            response.ContentLength64 = buffer.Length;
            response.ContentEncoding = Encoding.UTF8;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }
        catch (ObjectDisposedException)
        {
            _logger.Log("Warning: Response stream was disposed before SendResponse could complete writing.");
        }
        catch (Exception ex)
        {
            _logger.Log($"Error sending response: {ex.Message}");
            try
            {
                response.Close();
            }
            catch
            {
                /* ignore */
            }
        }
    }
}