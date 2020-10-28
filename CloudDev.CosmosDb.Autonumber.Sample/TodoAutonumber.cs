namespace CloudDev.CosmosDb.Autonumber
{
    using Newtonsoft.Json;

    public class TodoAutonumber
    {
        public static readonly string TodoAutonumberId = nameof(TodoAutonumberId);

        public TodoAutonumber()
        {
            Id = TodoAutonumberId;
        }
        
        [JsonProperty(PropertyName = "id")]
        public string Id { get; }

        public int MaxNumber { get; set; }
        
        public string PartitionKey { get; set; }
    }
}