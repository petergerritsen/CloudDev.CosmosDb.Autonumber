namespace CloudDev.CosmosDb.Autonumber
{
    using Newtonsoft.Json;

    public class TodoItem
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }

        public string Title { get; set; }

        public string PartitionKey { get; set; }
    }
}