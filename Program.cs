using System.Net;
using System.Text;
using GeminiServer;
using DotNetEnv;
using HavalNeGovno;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Server;

class SimpleServer
{
    private readonly HttpListener _listener;
    private readonly string _url;
    private readonly GeminiApi _geminiApi;
    private readonly UrlParser _parser;

    public SimpleServer(string url)
    {
        Env.TraversePath().Load();
        _url = url;
        _parser = new UrlParser(url);
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

        if (request.Url is null)
            return;

        try
        {
            var endPoint = _parser.GetCheckpoint(request.Url.AbsolutePath);
            switch (request.HttpMethod.ToUpper())
            {
                case "GET":
                    ProcessGet(endPoint, request, response);
                    break;

                case "POST":
                    ProcessPost(endPoint, request, response);
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

    private void ProcessGet(string endPoint, HttpListenerRequest request, HttpListenerResponse response) // Done
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
        var apiCall = _geminiApi.ProcessGeminiRequest(prompt, response).Result;
        SendResponse(apiCall.Item1, apiCall.Item2, apiCall.Item3);
    }

    private void ProcessPost(string endPoint, HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            switch (endPoint)
            {
                case "/api/data":
                    ProcessPostApiData(request, response);
                    break;
            }
        }

        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected Error: {ex.Message}");
            SendResponse(response, $"Unexpected Error: {ex.Message}", HttpStatusCode.InternalServerError);
        }
    }


    private void ProcessPostApiData(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (!IsValidJsonContentType(request))
        {
            SendResponse(response, "Content-Type: application/json needed",
                HttpStatusCode.UnsupportedMediaType);
            return;
        }

        string requestBody = ReadRequestBody(request);

        if (!TryParseJson(requestBody, out JObject jsonData, out string errorMessage))
        {
            Console.WriteLine($"JSON Parsing Error: {errorMessage}");
            SendResponse(response, "Wrong JSON format", HttpStatusCode.BadRequest);
            return;
        }

        if (!TryGetPrompt(jsonData, out string prompt, out errorMessage))
        {
            SendResponse(response, errorMessage, HttpStatusCode.BadRequest);
            return;
        }

        var apiResult = _geminiApi.ProcessGeminiRequest(prompt, response).Result;
        SendResponse(apiResult.Item1, apiResult.Item2, apiResult.Item3);
    }

    private bool IsValidJsonContentType(HttpListenerRequest request)
    {
        return request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private string ReadRequestBody(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private bool TryParseJson(string json, out JObject jsonData, out string errorMessage)
    {
        try
        {
            jsonData = JObject.Parse(json);
            errorMessage = null;
            return true;
        }
        catch (JsonReaderException ex)
        {
            jsonData = null;
            errorMessage = ex.Message;
            return false;
        }
    }

    private bool TryGetPrompt(JObject jsonData, out string prompt, out string errorMessage)
    {
        prompt = jsonData["prompt"]?.ToString();

        if (string.IsNullOrWhiteSpace(prompt))
        {
            errorMessage = "No prompt sent";
            return false;
        }

        errorMessage = null;
        return true;
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