using System.Net;
using System.Text;
using GeminiServer;
using DotNetEnv;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Server;
class SimpleServer
{
    private readonly HttpListener _listener;
    private readonly string _url;
    private readonly GeminiApi _geminiApi;

    public SimpleServer(string url)
    {
        Env.Load();
        _url = url;
        _listener = new HttpListener();
        _listener.Prefixes.Add(url);
        _geminiApi = new GeminiApi(Env.GetString("KEY"));
    }
    
    public void Start()
    {
        _listener.Start();
        Console.WriteLine($"Server started and listens to: {_url}");

        Thread listenerThread = new Thread(Listen);
        listenerThread.Start();
    }

    public void Stop()
    {
        _listener.Stop();
        Console.WriteLine("Server stopped");
    }

    private void Listen()
    {
        while (_listener.IsListening)
        {
            try
            {
                HttpListenerContext context = _listener.GetContext();
                ThreadPool.QueueUserWorkItem((state) =>
                {
                    try
                    {
                        ProcessRequest(context);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error proccesing task: {ex.Message}");
                    }
                });
            }
            catch (HttpListenerException ex)
            {
                if (ex.ErrorCode == 995) 
                    Console.WriteLine("Server shutdown...");
                else
                    Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        try
        {
            switch (request.HttpMethod.ToUpper())
            {
                case "GET":
                    ProcessGet(request, response);
                    break;
                
                case "POST":
                    ProcessPost(request, response);
                    break;

                default:
                    SendResponse(response, "Only GET and POST are supported", HttpStatusCode.MethodNotAllowed);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing request: {ex.Message}");
            SendResponse(response, "Internal Server Error", HttpStatusCode.InternalServerError);
        }
        finally
        {
            response.OutputStream.Close();
        }
    }

    private void ProcessGet(HttpListenerRequest request, HttpListenerResponse response)
    {
        var path = Uri.UnescapeDataString(request.Url.AbsolutePath);
        string staticPart = "/api/data=";

        switch (path.StartsWith(staticPart) ? staticPart : null)
        {
            case "/api/data":
                var promt = request.QueryString["promt"];
                Console.WriteLine(promt);
                var apiCall = _geminiApi.ProcessGeminiRequest(promt, response);
                SendResponse(apiCall.Item1, apiCall.Item2, apiCall.Item3);
                break;
        }
    }

    private void ProcessPost(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            // Проверяем Content-Type
            if (!request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? true)
            {
                SendResponse(response, "Требуется Content-Type: application/json", HttpStatusCode.UnsupportedMediaType);
                return;
            }

            // Читаем тело запроса
            string requestBody;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
            {
                requestBody = reader.ReadToEnd();
            }

            // Логируем для отладки
            Console.WriteLine($"Raw JSON: {requestBody}");

            // Парсим JSON как JObject
            JObject jsonData;
            try
            {
                jsonData = JObject.Parse(requestBody);
            }
            catch (JsonReaderException ex)
            {
                Console.WriteLine($"JSON Parsing Error: {ex.Message}");
                SendResponse(response, "Неверный формат JSON", HttpStatusCode.BadRequest);
                return;
            }

            // Вытаскиваем значение по ключу "prompt"
            var prompt = jsonData["prompt"]?.ToString();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                SendResponse(response, "Свойство 'prompt' обязательно", HttpStatusCode.BadRequest);
                return;
            }

            // Логируем успешное чтение данных
            Console.WriteLine($"Прочёл: Prompt = {prompt}");

            // Отправляем данные в Gemini API
            var apiCall = _geminiApi.ProcessGeminiRequest(prompt, response);

            // Отправляем ответ клиенту
            SendResponse(apiCall.Item1, apiCall.Item2, apiCall.Item3);
        }
        catch (Exception ex)
        {
            // Общая обработка ошибок
            Console.WriteLine($"Unexpected Error: {ex.Message}");
            SendResponse(response, $"Произошла ошибка: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }



    private void SendResponse(HttpListenerResponse response, string message, HttpStatusCode statusCode)
    {
        var buffer = Encoding.UTF8.GetBytes(message);

        response.StatusCode = (int)statusCode;
        response.ContentType = "text/plain";
        response.ContentLength64 = buffer.Length;

        response.OutputStream.Write(buffer, 0, buffer.Length);
    }
}

class Program
{
    static void Main(string[] args)
    {
        var url = "http://localhost:8080/"; 

        var server = new SimpleServer(url);
        server.Start();

        Console.WriteLine("Press any key to stop the server...");
        Console.ReadKey();

        server.Stop();
    }
}