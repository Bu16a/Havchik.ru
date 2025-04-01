using System.Net;
using System.Text;
using GeminiServer;
using DotNetEnv;

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
        SendResponse(response, "Task received", HttpStatusCode.OK);
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