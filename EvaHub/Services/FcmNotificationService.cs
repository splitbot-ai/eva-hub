using FirebaseAdmin.Messaging; 
using EvaHub.Databases;
namespace EvaHub.Services;

public class FcmNotificationService(ILogger<FcmNotificationService> logger, FerretDBService mongoDb, FirebaseMessaging firebaseMessaging)
{
    private const int MaxConcurrency = 10;
    
    public async Task SendAsync(List<string> fcmTokenList, string title, string body, bool dryRun = false)
    {
        if (fcmTokenList.Count == 0)
        {
            logger.LogWarning("No FCM tokens provided for notification");
            return;
        }

        logger.LogDebug("Sending Firebase notifications via Firebase Admin SDK.");

        var results = new List<FcmSendResult>();
        using var semaphore = new SemaphoreSlim(MaxConcurrency);

        var tasks = fcmTokenList.Select(async token =>
        {
            await semaphore.WaitAsync();
            try
            {
                var result = await SendSingleNotificationAsync(token, title, body, dryRun);
                lock (results)
                {
                    results.Add(result);
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        await RemoveInvalidTokensAsync(results);

        logger.LogInformation("Sent {SuccessCount}/{TotalCount} notifications successfully", 
            results.Count(r => r.Success), results.Count);
    }

    private async Task<FcmSendResult> SendSingleNotificationAsync(string token, string title, string body, bool dryRun)
    {
        var message = new Message()
        {
            Token = token,
            Notification = new Notification
            {
                Title = title,
                Body = body
            },
        };

        try
        {
            var messageId = await firebaseMessaging.SendAsync(message, dryRun);

            logger.LogDebug("FCM notification sent successfully to token {Token} with message ID {MessageId}", 
                token[..8] + "...", messageId);
            
            return new FcmSendResult(true, token, "OK", messageId, null);
        }
        catch (FirebaseMessagingException fcmEx)
        {
            logger.LogWarning(fcmEx, "FCM notification failed for token {Token}: ErrorCode={ErrorCode}, Message={ErrorMessage}", 
                token[..8] + "...", fcmEx.ErrorCode, fcmEx.Message);
            
            return new FcmSendResult(false, token, fcmEx.ErrorCode.ToString(), null, fcmEx.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception occurred while sending FCM notification to token {Token}", 
                token[..8] + "...");
            
            return new FcmSendResult(false, token, "Exception", null, ex.Message);
        }
    }

    private async Task RemoveInvalidTokensAsync(IReadOnlyList<FcmSendResult> results)
    {
        var invalidTokens = results
            .Where(r => !r.Success && IsInvalidToken(r.StatusCode))
            .Select(r => r.Token)
            .ToList();

        if (invalidTokens.Count == 0)
            return;

        logger.LogInformation("Removing {Count} invalid FCM tokens", invalidTokens.Count);
        await mongoDb.DeleteAllInvalidTokens(invalidTokens);
    }

    private static bool IsInvalidToken(string errorCode)
    {
        return errorCode switch
        {
            "INVALID_ARGUMENT" => true, 
            "NOT_FOUND" => true,        
            "UNREGISTERED" => true,     
            "SENDER_ID_MISMATCH" => true, 
            _ => false
        };
    }
}

public record FcmSendResult(
    bool Success,
    string Token,
    string StatusCode,
    string? Name,      
    string? ErrorMessage);
