using Microsoft.Extensions.DependencyInjection;
using Server;

class Program
{
    static async Task Main(string[] args)
    {
        var startup = new Startup();
        var serviceProvider = startup.ConfigureServices();
        
        var server = serviceProvider.GetRequiredService<SimpleServer>();
        server.Start();
        
        Console.WriteLine("Press Ctrl+C to stop the server...");
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("Ctrl+C detected. Stopping server...");
            cts.Cancel();
            server.Stop();
        };
        
        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("Server shutdown initiated by cancellation.");
        }
        Console.WriteLine("Main method exiting.");
    }
}