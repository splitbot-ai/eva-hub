using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;


namespace EvaHub.Models
{
    
    public class UserFcmToken
    {
        public ObjectId Id { get; set; }

        [BsonElement("userId")]
        public required string UserId { get; init; }

        [BsonElement("fcmToken")]
        public required string FcmToken { get; init; }

        [BsonElement("LastUpdated")]
        public long LastUpdated { get; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

}
