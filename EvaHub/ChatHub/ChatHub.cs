using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvaHub.Databases;
using EvaHub.Models;
using EvaHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EvaHub.ChatHub;

[Authorize]
public class ChatHub(IHttpClientFactory httpClientFactory, FerretDBService mongoDb, ConnectedUserService connectedUserService) : Hub
{
    
    public override async Task OnConnectedAsync()
    {
        if (Context.UserIdentifier != null)
        {
            connectedUserService.AddUser(Context.UserIdentifier);
            await Groups.AddToGroupAsync(Context.ConnectionId, Context.UserIdentifier);
            await base.OnConnectedAsync();
        }
        else
        {
            Context.Abort();
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.UserIdentifier != null)
        {
            connectedUserService.RemoveUser(Context.UserIdentifier);
            return Groups.RemoveFromGroupAsync(Context.ConnectionId, Context.UserIdentifier);
        }
        Context.Abort();
        return Task.CompletedTask;
    }

    public async Task ReceiveAppToken(string fmcToken)
    {
        if (Context.UserIdentifier != null)
        {
            var fcmTokenDoc = new UserFcmToken {UserId = Context.UserIdentifier, FcmToken = fmcToken };
            await mongoDb.UpdateInsertToken(fcmTokenDoc);
        }
        else { Context.Abort(); }
    }

    public async IAsyncEnumerable<string> GenerateEvaStreamMessage(string message)
    {
        if (Context.UserIdentifier == null)
        {
            yield break;
        }

        await Clients.Group(Context.UserIdentifier).SendAsync("ReceiveClientMessage", message);
        
        var token = GetBearerToken();

        using var client = httpClientFactory.CreateClient("EvaBackend");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        
        var jsonNode = JsonNode.Parse(message) ?? new JsonObject();
        var vMessage = jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var content = new StringContent(vMessage, Encoding.UTF8, "application/json");
        
        var request = new HttpRequestMessage(HttpMethod.Post, "http://Eva-service:8080/Eva/api/generateEvaStreamMessage")
        {
            Content = content
        };

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead
        );
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = $"Error: {response.StatusCode} - {response.ReasonPhrase}";
            await Clients.Group(Context.UserIdentifier).SendAsync("ErrorOccurred", errorMessage);
            yield break;
        }
        
        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (await reader.ReadLineAsync() is { } chunk)
        {
            yield return chunk;
            await Task.Delay(1);
        }
    }
    
    public async Task GenerateEvaMessage(String message)
    {
        if (Context.UserIdentifier == null)
            return;
        
        await Clients.Group(Context.UserIdentifier).SendAsync("ReceiveClientMessage", message);
        
        var jsonNode = JsonNode.Parse(message) ?? new JsonObject();
       
        var requestUrl = $"http://Eva-service:8080/Eva/api/generateEvaMessage";
        var vMessage = jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var token = GetBearerToken();
        using var client = httpClientFactory.CreateClient("EvaBackend");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        var content = new StringContent(vMessage, Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(requestUrl, content);
            
            var responseAction = response.IsSuccessStatusCode
                ? "ReceiveEvaMessage"
                : "ErrorOccurred";

            var payload = await response.Content.ReadAsStringAsync();

            await Clients.Group(Context.UserIdentifier).SendAsync(responseAction, response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : message);
        }
        catch (HttpRequestException)
        {
            await Clients.Group(Context.UserIdentifier).SendAsync("ErrorOccurred", message);
        }
    }
    
    private string? GetBearerToken()
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext == null)
            return null;

        // Token aus der URL abrufen
        var token = httpContext.Request.Query["access_token"].ToString();
        if (!string.IsNullOrEmpty(token))
            return token;

        // Token aus dem Authorization-Header abrufen
        if (!httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
            return null;

        var authHeaderValue = authHeader.ToString();
        if (!authHeaderValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        token = authHeaderValue["Bearer ".Length..].Trim();
        return string.IsNullOrEmpty(token) ? null : token;
    }
}