using System; // Добавлено для Task, List, Dictionary, Exception
using System.Collections.Generic; // Добавлено для List, Dictionary
using System.IO; // Добавлено для StreamReader
using System.Net;
using System.Text;
using System.Threading; // Убрано, т.к. ThreadPool не используется напрямую в Listen
using System.Threading.Tasks; // Добавлено для Task, async/await
using GeminiServer;
using DotNetEnv;
using HavalNeGovno; // Предполагается, что здесь MyMemoryTranslator
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using NpgsqlTypes; // Добавлено для указания типа параметра БД
                   // using static Google.Cloud.AIPlatform.V1.NearestNeighborQuery.Types; // Не используется в показанном коде
                   // using static Google.Rpc.Context.AttributeContext.Types; // Не используется в показанном коде

namespace Server;

class SimpleServer
{
    private readonly HttpListener _listener;
    private readonly string _url;
    private readonly GeminiApi _geminiApi;
    private readonly UrlParser _parser;
    private readonly string _connectionString;
    private Task? _listenerTask;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();


    public SimpleServer(string url)
    {
        Env.TraversePath().Load();
        _url = url;
        _parser = new UrlParser(url);
        _listener = new HttpListener();
        _listener.Prefixes.Add(url);
        _geminiApi = new GeminiApi(Env.GetString("KEY"));
        _connectionString = $"Host={Env.GetString("DB_HOST")};" +
                            $"Port={Env.GetString("DB_PORT", "5432")};" +
                            $"Username={Env.GetString("DB_USER")};" +
                            $"Password={Env.GetString("DB_PASS")};" +
                            $"Database={Env.GetString("DB_NAME")}";
    }

    public void Start()
    {
        _listener.Start();
        Console.WriteLine($"Server started and listens to: {_url}");
        _listenerTask = Listen(_cancellationTokenSource.Token);
        _listenerTask.ContinueWith(t => {
            Console.WriteLine($"Listener task terminated with exception: {t.Exception}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public void Stop()
    {
        Console.WriteLine("Stopping server...");
        _cancellationTokenSource.Cancel(); 
        _listener.Stop(); 

        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Listener task cancelled successfully.");
        }
        catch (AggregateException ae) when (ae.InnerExceptions.Count == 1 && ae.InnerExceptions[0] is OperationCanceledException)
        {
            Console.WriteLine("Listener task cancelled successfully via aggregate exception.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception during listener task wait: {ex.Message}");
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
        Console.WriteLine("Server stopped");
    }

    private async Task Listen(CancellationToken cancellationToken)
    {
        while (_listener.IsListening && !cancellationToken.IsCancellationRequested)
        {
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
                        Console.WriteLine($"Error processing request task: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
                        if (context.Response.OutputStream.CanWrite)
                        {
                            try
                            {
                                SendResponse(context.Response, "Internal Server Error during request processing.", HttpStatusCode.InternalServerError);
                            }
                            catch 
                            {
                            }
                        }
                    }
                    finally
                    {
                        context.Response.OutputStream.Close();
                    }
                }, cancellationToken);

            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 995 && cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("Listener stopped receiving requests due to cancellation.");
                break; 
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("Listener has been disposed, likely during shutdown.");
                break; 
            }
            catch (Exception ex) 
            {
                Console.WriteLine($"Unexpected error in listener loop: {ex.Message}");
                await Task.Delay(1000, cancellationToken);
            }
        }
        Console.WriteLine("Listen loop finished.");
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
            Console.WriteLine($"Error processing GET request: {ex.Message}");
            SendResponse(response, "Internal Server Error", HttpStatusCode.InternalServerError);
        }
    }

    // ProcessGetApiData остается синхронным в этом примере, но вызывает async метод GeminiApi
    // Это не лучший подход, лучше сделать всю цепочку асинхронной.
    // Но для минимальных изменений пока оставим так.
    private void ProcessGetApiData(HttpListenerRequest request, HttpListenerResponse response)
    {
        var parameters = _parser.GetParams(request);
        if (!parameters.ContainsKey("prompt") || string.IsNullOrWhiteSpace(parameters["prompt"]))
        {
            SendResponse(response, "Parameter 'prompt' is required", HttpStatusCode.BadRequest);
            return;
        }

        var prompt = parameters["prompt"];
        Console.WriteLine($"Received prompt: {prompt}");

        if (parameters.ContainsKey("iname"))
            _geminiApi.ChangeModel(parameters["iname"]);

        // ВАЖНО: .Result блокирует поток! Это анти-паттерн в асинхронном коде.
        // В идеале ProcessGetApiData, ProcessGet, ProcessRequestAsync должны быть async Task.
        // Для демонстрации ProcessPostSearchRecipe оставим так, но это нужно исправить.
        var apiCall = _geminiApi.ProcessGeminiRequest(prompt, response).Result;
        SendResponse(response, apiCall.Item2, apiCall.Item3, apiCall.Item1.ContentType); // Передаем ContentType
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
            Console.WriteLine($"Unexpected Error during POST processing: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            SendResponse(response, $"Unexpected Error: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }

    // Реализация асинхронного метода поиска рецептов
    private async Task ProcessPostSearchRecipeAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        // 1. Проверка Content-Type
        if (!IsValidJsonContentType(request))
        {
            SendResponse(response, "Content-Type: application/json needed", HttpStatusCode.UnsupportedMediaType);
            return;
        }

        // 2. Чтение тела запроса
        string requestBody = await ReadRequestBodyAsync(request); // Используем асинхронное чтение

        // 3. Парсинг JSON
        if (!TryParseJson(requestBody, out JObject jsonData, out string jsonErrorMessage))
        {
            Console.WriteLine($"JSON Parsing Error: {jsonErrorMessage}");
            SendResponse(response, $"Wrong JSON format: {jsonErrorMessage}", HttpStatusCode.BadRequest);
            return;
        }

        // 4. Извлечение параметров: списка продуктов (ingredients) и кол-ва рецептов (count)
        if (!TryGetParam<List<string>>(jsonData, "ingredients", out var russianIngredients, out string ingredientsError) || russianIngredients == null || russianIngredients.Count == 0)
        {
            SendResponse(response, ingredientsError ?? "Parameter 'ingredients' (list of strings) is required and cannot be empty.", HttpStatusCode.BadRequest);
            return;
        }

        if (!TryGetParam<int>(jsonData, "count", out int recipeCount, out string countError) || recipeCount <= 0)
        {
            SendResponse(response, countError ?? "Parameter 'count' (positive integer) is required.", HttpStatusCode.BadRequest);
            return;
        }

        // 5. Перевод ингредиентов на английский
        var translatedIngredients = new List<string>(russianIngredients.Count);
        try
        {
            Console.WriteLine($"Translating ingredients: {string.Join(", ", russianIngredients)}");
            foreach (var ingredient in russianIngredients)
            {
                // Предполагаем, что MyMemoryTranslator.TranslateWithMyMemoryAsync существует и работает
                string translated = await MyMemoryTranslator.TranslateWithMyMemoryAsync(ingredient);
                if (!string.IsNullOrWhiteSpace(translated))
                {
                    translatedIngredients.Add(translated.ToLowerInvariant()); 
                }
                else
                {
                    Console.WriteLine($"Warning: Translation failed or returned empty for '{ingredient}'");
                }
            }
            Console.WriteLine($"Translated ingredients: {string.Join(", ", translatedIngredients)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during translation: {ex.Message}");
            SendResponse(response, "Error during ingredient translation.", HttpStatusCode.InternalServerError);
            return;
        }

        if (translatedIngredients.Count == 0)
        {
            SendResponse(response, "No valid ingredients found after translation.", HttpStatusCode.BadRequest);
            return;
        }


        var recipesResult = new Dictionary<string, Dictionary<string, object>>(); // Словарь для JSON ответа
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(); // Асинхронное открытие соединения

            // Запрос ищет рецепты, где поле ner_ingredients (предположительно массив TEXT[])
            // содержит ВСЕ переданные ингредиенты (@> оператор для массивов)
            // И ограничивает количество записей
            // Используем параметры @ingredients и @limit для безопасности
            var query = @"
                SELECT *
                FROM recipes
                WHERE ner_ingredients @> @ingredients::TEXT[]
                ORDER BY id -- Добавляем сортировку для стабильности LIMIT (замените id на релевантное поле)
                LIMIT @limit";

            await using var command = new NpgsqlCommand(query, connection);

            // Добавляем параметры
            // NpgsqlDbType.Array | NpgsqlDbType.Text указывает, что это массив строк
            command.Parameters.AddWithValue("ingredients", NpgsqlDbType.Array | NpgsqlDbType.Text, translatedIngredients);
            command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, recipeCount);

            Console.WriteLine($"Executing SQL: {command.CommandText} with ingredients: [{string.Join(", ", translatedIngredients)}], limit: {recipeCount}");

            await using var reader = await command.ExecuteReaderAsync(); // Асинхронное выполнение запроса

            int recipeIndex = 1;
            while (await reader.ReadAsync()) // Асинхронное чтение строк
            {
                var recipeData = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var columnName = reader.GetName(i);
                    var columnValue = reader.GetValue(i);
                    // Обработка null значений или специальных типов при необходимости
                    recipeData[columnName] = columnValue == DBNull.Value ? null : columnValue;
                }
                // Добавляем данные рецепта в общий результат с ключом "recipe_1", "recipe_2", ...
                recipesResult[$"recipe_{recipeIndex++}"] = recipeData;
            }

            Console.WriteLine($"Found {recipesResult.Count} recipes.");

            // 7. Сериализация результата в JSON и отправка ответа
            string jsonResponse = JsonConvert.SerializeObject(recipesResult, Formatting.Indented);
            SendResponse(response, jsonResponse, HttpStatusCode.OK, "application/json");

        }
        catch (NpgsqlException ex)
        {
            Console.WriteLine($"Database query error: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            SendResponse(response, "Error querying recipes database.", HttpStatusCode.InternalServerError);
        }
        catch (Exception ex) // Ловим другие возможные ошибки (сериализация и т.д.)
        {
            Console.WriteLine($"Unexpected error in ProcessPostSearchRecipe: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            // Проверяем, не был ли ответ уже отправлен
            SendResponse(response, "Unexpected server error.", HttpStatusCode.InternalServerError);
        }
    }


    // Метод стал async Task
    private async Task ProcessPostApiDataAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (!IsValidJsonContentType(request))
        {
            SendResponse(response, "Content-Type: application/json needed", HttpStatusCode.UnsupportedMediaType);
            return;
        }

        string requestBody = await ReadRequestBodyAsync(request); // Асинхронное чтение

        if (!TryParseJson(requestBody, out JObject jsonData, out string errorMessage))
        {
            Console.WriteLine($"JSON Parsing Error: {errorMessage}");
            SendResponse(response, "Wrong JSON format", HttpStatusCode.BadRequest);
            return;
        }

        if (!TryGetParam<string>(jsonData, "prompt", out string prompt, out errorMessage))
        {
            SendResponse(response, errorMessage, HttpStatusCode.BadRequest);
            return;
        }

        if (TryGetParam<string>(jsonData, "iname", out string iname, out _)) // Игнорируем ошибку, если iname не найден
            _geminiApi.ChangeModel(iname);

        // Вызываем асинхронный метод GeminiApi и ждем результат
        // ProcessGeminiRequest тоже должен быть async Task
        var apiResult = await _geminiApi.ProcessGeminiRequest(prompt, response);
        SendResponse(apiResult.Item1, apiResult.Item2, apiResult.Item3, apiResult.Item1.ContentType); // Используем ContentType из ответа API
    }

    // Вспомогательные методы

    private bool IsValidJsonContentType(HttpListenerRequest request)
    {
        return request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    // Асинхронная версия чтения тела запроса
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
        catch (Exception ex) // Ловим и другие возможные ошибки парсинга
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

        if (token.Type == JTokenType.Null && default(T) != null) // Проверка на null, если T - не nullable тип значения
        {
            errorMessage = $"Parameter '{paramName}' cannot be null.";
            return false;
        }

        if (token.Type == JTokenType.String && string.IsNullOrWhiteSpace(token.ToString()) && typeof(T) == typeof(string))
        {
            // Специальная проверка для строк, если пустая строка невалидна (можно убрать, если пустая строка допустима)
            // errorMessage = $"Parameter '{paramName}' cannot be empty or whitespace.";
            // return false;
        }


        try
        {
            // Пытаемся десериализовать токен в нужный тип T
            // Для простых типов (string, int, bool) ToObject<T>() сработает
            // Для List<string> это тоже должно работать
            value = token.ToObject<T>();
            if (value == null && default(T) != null) // Дополнительная проверка после ToObject
            {
                errorMessage = $"Parameter '{paramName}' could not be converted to the required type or is null.";
                return false;
            }
            return true;
        }
        catch (JsonException ex) // Ловим ошибки конвертации типов Newtonsoft.Json
        {
            errorMessage = $"Invalid format for parameter '{paramName}'. Expected type: {typeof(T).Name}. Error: {ex.Message}";
            return false;
        }
        catch (Exception ex) // Ловим другие неожиданные ошибки
        {
            errorMessage = $"An unexpected error occurred while retrieving parameter '{paramName}': {ex.Message}";
            return false;
        }
    }


    // Модифицирован для приема ContentType
    private void SendResponse(HttpListenerResponse response, string message, HttpStatusCode statusCode, string contentType = "text/plain; charset=utf-8")
    {
        // Проверяем, не был ли ответ уже отправлен или поток закрыт
        if (!response.OutputStream.CanWrite)
        {
            Console.WriteLine($"Warning: Attempted to write response after headers were sent or stream was closed. Status: {statusCode}, Message: {message.Substring(0, Math.Min(message.Length, 100))}");
            return;
        }
        try
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            response.StatusCode = (int)statusCode;
            response.ContentType = contentType;
            response.ContentLength64 = buffer.Length;
            response.ContentEncoding = Encoding.UTF8; // Указываем кодировку

            response.OutputStream.Write(buffer, 0, buffer.Length);
            // response.OutputStream.Close(); // Закрывать здесь НЕ НУЖНО, закрытие происходит в Listen после завершения ProcessRequestAsync
        }
        catch (ObjectDisposedException)
        {
            // Поток мог быть закрыт в другом месте, игнорируем.
            Console.WriteLine("Warning: Response stream was disposed before SendResponse could complete.");
        }
        catch (Exception ex)
        {
            // Логгируем другие ошибки при отправке ответа
            Console.WriteLine($"Error sending response: {ex.Message}");
        }
    }
}

class Program
{
    // Main теперь тоже async Task, т.к. вызывает асинхронные методы
    static async Task Main(string[] args)
    {
        // Убираем или комментируем тестовый код из Main, он теперь внутри ProcessPostSearchRecipeAsync
        /*
        var product = new string[] {"докторская колбаса", "рыба", "огурец"};
        // ... остальной тестовый код ...
        */

        // Раскомментируем запуск сервера
        var url = "http://localhost:8080/"; // Или другой адрес/порт

        var server = new SimpleServer(url);
        server.Start(); // Start остается синхронным, он запускает Listen в фоне

        Console.WriteLine("Press Ctrl+C to stop the server...");

        // Настраиваем ожидание завершения по Ctrl+C
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) => {
            e.Cancel = true; // Предотвращаем завершение приложения по умолчанию
            Console.WriteLine("Ctrl+C detected. Stopping server...");
            cts.Cancel(); // Сигнализируем об отмене
            server.Stop(); // Вызываем метод остановки сервера
        };

        // Ожидаем сигнала отмены (можно заменить на другую логику ожидания)
        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (TaskCanceledException)
        {
            // Ожидаемое исключение при отмене через cts.Cancel()
            Console.WriteLine("Server shutdown initiated by cancellation.");
        }

        Console.WriteLine("Main method exiting.");
    }
}