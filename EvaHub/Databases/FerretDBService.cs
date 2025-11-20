using EvaHub.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace EvaHub.Databases;

public class FerretDBService(IOptions<DatabaseSettings> dbSettings)
{
    private readonly IMongoCollection<UserFcmToken>? _pushNotificationCollection = InitializeCollection(dbSettings);

    private static IMongoCollection<UserFcmToken>? InitializeCollection(IOptions<DatabaseSettings> dbSettings)
    {
        try
        {
            var client = new MongoClient(dbSettings.Value.ConnectionString);
            var database = client.GetDatabase(dbSettings.Value.DatabaseName);
            return database.GetCollection<UserFcmToken>(dbSettings.Value.CollectionName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Verbinden zur MongoDB: {ex.Message}");
            return null;
        }
    }

    private async Task InsertDocumentAsync(UserFcmToken userFcmData)
    {
        if (_pushNotificationCollection == null)
            throw new InvalidOperationException("FerretDB collection is not initialized");
            
        await _pushNotificationCollection.InsertOneAsync(userFcmData);
    }

    public async Task UpdateInsertToken(UserFcmToken userFcmData)
    {
        if (_pushNotificationCollection == null)
            throw new InvalidOperationException("FerretDB collection is not initialized");

        var document = await _pushNotificationCollection
            .Find(doc => doc.FcmToken.Equals(userFcmData.FcmToken))
            .FirstOrDefaultAsync();

        if (document == null)
        {
            await InsertDocumentAsync(userFcmData);
        }
        else if (!document.UserId.Equals(userFcmData.UserId))
        {
            var update = Builders<UserFcmToken>.Update
                .Set(doc => doc.UserId, userFcmData.UserId)
                .Set(doc => doc.LastUpdated, userFcmData.LastUpdated);

            await _pushNotificationCollection.UpdateOneAsync(
                doc => doc.FcmToken.Equals(userFcmData.FcmToken), 
                update);
        }
        else
        {
            var update = Builders<UserFcmToken>.Update
                .Set(doc => doc.LastUpdated, userFcmData.LastUpdated);
                
            await _pushNotificationCollection.UpdateOneAsync(
                doc => doc.FcmToken.Equals(userFcmData.FcmToken), 
                update);
        }
    }

    public async Task<List<string>> GetAllTokenFromUser(string userId)
    {
        if (_pushNotificationCollection == null)
            return new List<string>();

        var filter = Builders<UserFcmToken>.Filter.Eq(doc => doc.UserId, userId);

        return await _pushNotificationCollection
            .Find(filter)
            .Project(d => d.FcmToken)
            .ToListAsync();
    }

    public async Task DeleteAllInvalidTokens(List<string> fcmInvalidTokens)
    {
        if (_pushNotificationCollection == null || fcmInvalidTokens.Count == 0)
            return;

        var removeFilter = Builders<UserFcmToken>.Filter.In(doc => doc.FcmToken, fcmInvalidTokens);
        await _pushNotificationCollection.DeleteManyAsync(removeFilter);
    }
}