namespace PowerShell.MCP.Services
{
    /// <summary>
    /// MCP通知を管理するサービス
    /// </summary>
    //public class NotificationService
    //{
    //    private readonly Action<string> _sendNotification;
    //    private readonly Dictionary<string, DateTime> _lastNotificationTimes = new();
    //    private readonly TimeSpan _throttleInterval = TimeSpan.FromMilliseconds(500);
        
    //    public NotificationService(Action<string> sendNotification)
    //    {
    //        _sendNotification = sendNotification;
    //    }

    //    /// <summary>
    //    /// 通知を送信（スロットリング付き）
    //    /// </summary>
    //    public void SendNotification(string notificationType, object data)
    //    {
    //        // スロットリング制御
    //        var now = DateTime.UtcNow;
    //        if (_lastNotificationTimes.TryGetValue(notificationType, out var lastTime) &&
    //            now - lastTime < _throttleInterval)
    //        {
    //            return;
    //        }
            
    //        _lastNotificationTimes[notificationType] = now;

    //        var notification = new
    //        {
    //            jsonrpc = "2.0",
    //            method = "notifications/message",
    //            @params = new
    //            {
    //                level = GetLogLevel(notificationType),
    //                logger = "PowerShell.MCP",
    //                data = new Dictionary<string, object>
    //                {
    //                    ["type"] = notificationType,
    //                    ["timestamp"] = now.ToString("O")
    //                }.Concat(GetDataProperties(data)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
    //            }
    //        };

    //        var json = JsonSerializer.Serialize(notification, new JsonSerializerOptions 
    //        { 
    //            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
    //        });
            
    //        _sendNotification(json);
    //    }

    //    private static string GetLogLevel(string notificationType)
    //    {
    //        return notificationType switch
    //        {
    //            "command_failed" => "error",
    //            "location_changed" => "info",
    //            "command_executed" => "info",
    //            "long_running_command" => "warning",
    //            _ => "info"
    //        };
    //    }

    //    private static IEnumerable<KeyValuePair<string, object>> GetDataProperties(object data)
    //    {
    //        if (data == null) yield break;
            
    //        var properties = data.GetType().GetProperties();
    //        foreach (var prop in properties)
    //        {
    //            var value = prop.GetValue(data);
    //            if (value != null)
    //            {
    //                yield return new KeyValuePair<string, object>(
    //                    char.ToLowerInvariant(prop.Name[0]) + prop.Name.Substring(1), 
    //                    value
    //                );
    //            }
    //        }
    //    }
    //}
}
