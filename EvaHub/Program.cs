using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using EvaHub.ChatHub;
using EvaHub.Databases;
using EvaHub.Models;
using EvaHub.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel((context, options) =>
{
    options.Configure(context.Configuration.GetSection("Kestrel"));
    options.Limits.MaxRequestBodySize = 2 * 1024 * 1024;
});

// Configuration
builder.Services.Configure<FirebaseSettings>(builder.Configuration.GetSection("Firebase"));
builder.Services.Configure<DatabaseSettings>(builder.Configuration.GetSection("FerretDBSettings"));

var firebaseSettings = builder.Configuration.GetSection("Firebase").Get<FirebaseSettings>();
var notificationsAllow = builder.Configuration.GetValue<bool>("NotificationsAllow");

if (firebaseSettings == null && notificationsAllow)
{
    var loggerForStartup = LoggerFactory.Create(config => config.AddConsole()).CreateLogger<Program>();
    loggerForStartup.LogError("Firebase settings not found in appsettings.json. Application cannot start without Firebase configuration.");
    Environment.Exit(1);
}

if (notificationsAllow)
{
    try
    {
        var credentialJson = JsonSerializer.Serialize(firebaseSettings);

        FirebaseApp.Create(new AppOptions()
        {
            Credential = GoogleCredential.FromJson(credentialJson)
        });
    
        builder.Services.AddSingleton(FirebaseMessaging.DefaultInstance);
        builder.Logging.AddConsole().AddDebug();
        builder.Services.AddLogging();
    }
    catch (Exception ex)
    {
        var loggerForStartup = LoggerFactory.Create(config => config.AddConsole()).CreateLogger<Program>();
        loggerForStartup.LogError(ex, "Failed to initialize Firebase Admin SDK. Please check your appsettings.json and service account key. Application cannot start.");
        Environment.Exit(1); 
    }
}

// Service Registration
builder.Services.AddSingleton<FerretDBService>();
builder.Services.AddSingleton<ConnectedUserService>();
builder.Services.AddScoped<FcmNotificationService>();
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("EvaBackend", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 2 * 1024 * 1024;
});
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["jwt:authority"];
        options.Audience = builder.Configuration["jwt:audience"]; 
        options.SaveToken = true;
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true
        };
        options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/Eva/chatHub"))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        },
        
        OnAuthenticationFailed = context =>
        {
        Console.WriteLine("Authentication failed: " + context.Exception.Message);
        return Task.CompletedTask;
        }
    };
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapHub<ChatHub>("/eva/chatHub");
app.MapPost("/signalR/sendMessage/{userId}", async (string userId, HttpRequest request, IHubContext<ChatHub> hubContext, FerretDBService mongoDb, FcmNotificationService fcmService, ConnectedUserService userTracker, string? notification) =>
{
    using var reader = new StreamReader(request.Body);
    var message = await reader.ReadToEndAsync();
    
    var notificationText = string.IsNullOrWhiteSpace(notification)
        ? "Eine geplante Aufgabe ist fertig"
        : notification;

    if (!userTracker.IsUserConnected(userId) && notificationsAllow)
    {
        var fcmtokens = await mongoDb.GetAllTokenFromUser(userId);
        await fcmService.SendAsync(fcmtokens, "Eva", notificationText);
    }
    else
    {
        await hubContext.Clients.Group(userId).SendAsync("ReceiveEvaMessage", message);
    }

    return Results.Ok("done");
}).AllowAnonymous();

app.MapPost("/signalR/sendTitle/{userId}", async (string userId, HttpRequest request, IHubContext<ChatHub> hubContext) =>
{
    using var reader = new StreamReader(request.Body);
    var message = await reader.ReadToEndAsync();
    await hubContext.Clients.Group(userId).SendAsync("SendRoomTitle", message);
    return Results.Ok("done");
}).AllowAnonymous();
app.Run();