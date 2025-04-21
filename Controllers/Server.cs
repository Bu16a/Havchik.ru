using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using NpgsqlTypes;

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

    public SimpleServer(string url, IUrlParser parser, IGeminiApi geminiApi, IDbService dbService, ITranslator translator, ILogger logger)
    {
        Env.TraversePath().Load();
        _url = url;
        _parser = parser;
        _listener = new HttpListener();
        _listener.Prefixes.Add(_url);
        _geminiApi = geminiApi;
        _dbService = dbService;
        _translator = translator;
        _logger = logger;
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

            var recipesData = await QueryRecipesFromDatabaseAsync(translatedIngredients, recipeCount);

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

    private async Task<List<Dictionary<string, object>>> QueryRecipesFromDatabaseAsync(
        List<string> translatedIngredients, int recipeCount)
    {
        var query = @"
            SELECT *
            FROM recipes
            WHERE ner_ingredients @> @ingredients::TEXT[]
            ORDER BY id
            LIMIT @limit";

        var parameters = new Dictionary<string, (object value, NpgsqlDbType dbType)>
        {
            { "ingredients", (translatedIngredients, NpgsqlDbType.Array | NpgsqlDbType.Text) },
            { "limit", (recipeCount, NpgsqlDbType.Integer) }
        };

        return await _dbService.ExecuteQueryAsync(query, parameters);
    }

    private async Task<object> TranslateValueAsync(object value, string sourceLang, string targetLang)
    {
        if (value is string originalString)
        {
            return await _translator.TranslateWithMyMemoryAsync(originalString, sourceLang, targetLang);
        }
        else if (value is string[] originalArray)
        {
            var translatedList = new List<string>(originalArray.Length);
            foreach (var item in originalArray)
            {
                translatedList.Add(await _translator.TranslateWithMyMemoryAsync(item, sourceLang, targetLang));
            }

            return translatedList.ToArray();
        }
        else if (value is List<string> originalList)
        {
            var translatedList = new List<string>(originalList.Count);
            foreach (var item in originalList)
            {
                translatedList.Add(await _translator.TranslateWithMyMemoryAsync(item, sourceLang, targetLang));
            }

            return translatedList;
        }

        return value;
    }

    private async Task<Dictionary<string, Dictionary<string, object>>> ProcessRecipesAsync(
        List<Dictionary<string, object>> recipesData)
    {
        var recipesResult = new Dictionary<string, Dictionary<string, object>>();
        var keysToTranslate = new HashSet<string> { "title", "ingredients", "directions", "source", "ner_ingredients" };

        int recipeIndex = 1;
        foreach (var recipeData in recipesData)
        {
            var translationTasks = recipeData.Keys
                .Where(key => keysToTranslate.Contains(key) && recipeData[key] != null)
                .Select(async key => { recipeData[key] = await TranslateValueAsync(recipeData[key], "en", "ru"); });

            await Task.WhenAll(translationTasks);

            recipesResult[$"recipe_{recipeIndex++}"] = recipeData;
        }

        _logger.Log($"Найдено и переведено {recipesResult.Count} рецептов.");
        return recipesResult;
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
        catch (JsonException ex)
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