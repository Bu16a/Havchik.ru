using System;
using System.Net;
using System.Text;
using System.Threading;


namespace Servachok;
class SimpleServer
{
    private readonly HttpListener _listener;
    private readonly string _url;

    public SimpleServer(string url)
    {
        _url = url;
        _listener = new HttpListener();
        _listener.Prefixes.Add(url);
    }

    public void Start()
    {
        _listener.Start();
        Console.WriteLine($"Сервер запущен и слушает {_url}");

        Thread listenerThread = new Thread(Listen);
        listenerThread.Start();
    }

    public void Stop()
    {
        _listener.Stop();
        Console.WriteLine("Сервер остановлен");
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
                        Console.WriteLine($"Ошибка при обработке запроса: {ex.Message}");
                    }
                });
            }
            catch (HttpListenerException ex)
            {
                if (ex.ErrorCode == 995) 
                    Console.WriteLine("Сервер завершает работу...");
                else
                    Console.WriteLine($"Ошибка: {ex.Message}");
            }
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        Console.WriteLine($"Получен запрос: {request.Url}");

        if (request.HttpMethod == "GET")
        {
            string responseString = "да";
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);

            response.ContentType = "text/plain";
            response.ContentLength64 = buffer.Length;

            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        else
        {
            response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            string errorResponse = "Поддерживаются только GET-запросы";
            byte[] buffer = Encoding.UTF8.GetBytes(errorResponse);
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }

        response.Close();
    }
}

class Program
{
    static void Main(string[] args)
    {
        string url = "http://localhost:8080/"; 

        var server = new SimpleServer(url);
        server.Start();

        Console.WriteLine("Нажмите любую клавишу для остановки сервера...");
        Console.ReadKey();

        server.Stop();
    }
}