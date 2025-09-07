using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace PowerShell.MCP.Services;

/// <summary>
/// Named Pipe サーバー - PowerShell.MCP.Proxy.exe との通信を担当
/// </summary>
public class NamedPipeServer : IDisposable
{
    private const string PipeName = "PowerShell.MCP.Communication";
    //private const string PipeName = "PowerShell.MCP.Communication-debug";
    private const int MaxConcurrentConnections = 1;
    private readonly MCPProvider _provider;
    private readonly CancellationTokenSource _internalCancellation = new();
    private readonly List<Task> _serverTasks = new();
    private bool _disposed = false;

    public NamedPipeServer(MCPProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Named Pipe サーバーを開始します
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _internalCancellation.Token);

        try
        {
            // 複数の同時接続を許可するため、複数のサーバーインスタンスを起動
            for (int i = 0; i < MaxConcurrentConnections; i++)
            {
                var task = RunServerInstanceAsync(combinedCts.Token);
                _serverTasks.Add(task);
            }

            // すべてのサーバーインスタンスの完了を待機
            await Task.WhenAll(_serverTasks);
        }
        catch (OperationCanceledException)
        {
            // キャンセルは正常な終了
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Named Pipe Server error: {ex.Message}");
        }
    }

    /// <summary>
    /// 単一のサーバーインスタンスを実行します
    /// </summary>
    private async Task RunServerInstanceAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = CreateNamedPipeServer();
                
                // クライアントの接続を待機
                await pipeServer.WaitForConnectionAsync(cancellationToken);
                
                // 接続されたクライアントとの通信を処理
                await HandleClientAsync(pipeServer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Named Pipe Server instance error: {ex.Message}");
                
                // エラー発生時は少し待機してから再試行
                try
                {
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Named Pipeサーバーを作成します
    /// </summary>
    private static NamedPipeServerStream CreateNamedPipeServer()
    {
        return new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    /// <summary>
    /// クライアントとの通信を処理します
    /// </summary>
    private static async Task HandleClientAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        try
        {
            // リクエストを受信
            var requestJson = await ReceiveMessageAsync(pipeServer, cancellationToken);
            
            // JSON-RPCリクエストを解析
            using var requestDoc = JsonDocument.Parse(requestJson);
            var requestRoot = requestDoc.RootElement;
            
            var method = requestRoot.GetProperty("method").GetString();
            double id = 0;
            if (requestRoot.TryGetProperty("id", out var idElement))
            {
                id = idElement.GetDouble();
            }
            var parameters = requestRoot.TryGetProperty("parameters", out var paramsElement) ? paramsElement : new JsonElement();

            // ツールを実行
            var result = await Task.Run(() => ExecuteTool(method!, parameters));
            
            // レスポンスを構築
            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = result
            };

            var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // レスポンスを送信
            await SendMessageAsync(pipeServer, responseJson, cancellationToken);
        }
        catch (Exception ex)
        {
            // エラーレスポンスを送信
            var errorResponse = new
            {
                jsonrpc = "2.0",
                id = (string?)null,
                error = new
                {
                    code = -32603,
                    message = "Internal error",
                    data = ex.Message
                }
            };

            var errorJson = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            try
            {
                await SendMessageAsync(pipeServer, errorJson, cancellationToken);
            }
            catch
            {
                // エラーレスポンス送信に失敗した場合は諦める
            }
        }
    }

    /// <summary>
    /// ツールを実行します
    /// </summary>
    private static string ExecuteTool(string method, JsonElement parameters)
    {
        return method switch
        {
            "getCurrentLocation" => MCPProvider.GetCurrentLocation(),
            "invokeExpression" => ExecuteInvokeExpression(parameters),
            _ => throw new ArgumentException($"Unknown method: {method}")
        };
    }

    /// <summary>
    /// invokeExpressionツールを実行します
    /// </summary>
    private static string ExecuteInvokeExpression(JsonElement parameters)
    {
        var pipeline = parameters.GetProperty("pipeline").GetString() ?? "";
        var executeImmediately = parameters.TryGetProperty("executeImmediately", out var execElement) 
            ? execElement.GetBoolean() 
            : true;

        return MCPProvider.ExecuteCommand(pipeline, executeImmediately);
    }

    /// <summary>
    /// Named Pipeからメッセージを受信します
    /// </summary>
    private static async Task<string> ReceiveMessageAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        // メッセージの長さを受信（4バイト）
        var lengthBytes = new byte[4];
        await ReadExactAsync(pipeServer, lengthBytes, cancellationToken);
        var messageLength = BitConverter.ToInt32(lengthBytes, 0);

        if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) // 10MB上限
        {
            throw new InvalidOperationException($"Invalid message length: {messageLength}");
        }

        // メッセージ本体を受信
        var messageBytes = new byte[messageLength];
        await ReadExactAsync(pipeServer, messageBytes, cancellationToken);

        return Encoding.UTF8.GetString(messageBytes);
    }

    /// <summary>
    /// Named Pipeにメッセージを送信します
    /// </summary>
    private static async Task SendMessageAsync(NamedPipeServerStream pipeServer, string message, CancellationToken cancellationToken)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

        // メッセージの長さを送信（4バイト）
        await pipeServer.WriteAsync(lengthBytes, 0, 4, cancellationToken);
        
        // メッセージ本体を送信
        await pipeServer.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
        await pipeServer.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// 指定したバイト数を確実に読み取ります
    /// </summary>
    private static async Task ReadExactAsync(NamedPipeServerStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalBytesRead = 0;
        while (totalBytesRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer, totalBytesRead, buffer.Length - totalBytesRead, cancellationToken);
            if (bytesRead == 0)
            {
                throw new IOException("Connection closed while reading data");
            }
            totalBytesRead += bytesRead;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _internalCancellation.Cancel();
            _internalCancellation.Dispose();
            _disposed = true;
        }
    }
}
