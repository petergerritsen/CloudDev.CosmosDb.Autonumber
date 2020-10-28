namespace CloudDev.CosmosDb.Autonumber
{
    using Microsoft.Azure.Cosmos;
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Bogus;
    using Polly;
    using Polly.Contrib.WaitAndRetry;
    using Polly.Retry;
    using Database = Microsoft.Azure.Cosmos.Database;

    class Program
    {
        static SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1,1);

        private static string EndpointUrl = Environment.GetEnvironmentVariable("CosmosAutonumberEndpointUrl");

        private static string PrimaryKey = Environment.GetEnvironmentVariable("CosmosAutonumberPrimaryKey");

        private CosmosClient _cosmosClient;

        private Database _database;

        private Container _container;

        private const string DatabaseId = "TodoDatabase";
        private const string ContainerId = "TodoContainer";
        private const string PartitionKey = "TodoItems";

        private AsyncRetryPolicy waitAndRetryPolicy;

        static async Task Main(string[] args)
        { 
            try
            {
                Console.WriteLine("Beginning operations...\n");
                Program p = new Program();
                await p.GetStartedDemoAsync();
            }
            catch (CosmosException de)
            {
                Exception baseException = de.GetBaseException();
                Console.WriteLine("{0} error occurred: {1}", de.StatusCode, de);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: {0}", e);
            }
            finally
            {
                Console.WriteLine("End of demo, press any key to exit.");
                Console.ReadKey();
            }
        }
        
        private async Task GetStartedDemoAsync()
        {
            // Create a new instance of the Cosmos Client
            _cosmosClient = new CosmosClient(EndpointUrl, PrimaryKey);
            await CreateDatabaseAsync();
            await CreateContainerAsync();
            
            var delay = Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromSeconds(1), 10, fastFirst: true);

            waitAndRetryPolicy = Policy.Handle<TodoCreateException>()
                .WaitAndRetryAsync(delay,
                    (exception, timeSpan, retryCount, context) =>
                    {
                        Console.WriteLine($"CreateReclamation retry attempt {retryCount}, waiting {timeSpan}");
                    });
            
            await this.AddItemsToContainerAsync();
        }

        private async Task CreateDatabaseAsync()
        {
            // Create a new database
            _database = await _cosmosClient.CreateDatabaseIfNotExistsAsync(DatabaseId);
            Console.WriteLine("Created Database: {0}\n", this._database.Id);
        }
        
        private async Task CreateContainerAsync()
        {
            // Create a new container
            _container = await _database.CreateContainerIfNotExistsAsync(ContainerId, "/PartitionKey");
            Console.WriteLine("Created Container: {0}\n", this._container.Id);
        }

        private async Task AddItemsToContainerAsync()
        {
            List<Task<TodoItem>> createTasks = new List<Task<TodoItem>>();
            for (int i = 0; i < 100; i++)
            {
                createTasks.Add(CreateItemWithNewIdWithRetry());
            }

            await Task.WhenAll(createTasks);

            foreach (var task in createTasks)
            {
                Console.WriteLine($"Created Todo: {task.Result.Id}");
            }
        }

        private async Task<TodoItem> CreateItemWithNewIdWithRetry()
        {
            var result = await waitAndRetryPolicy.ExecuteAsync(() =>
                CreateItemWithNewId());

            return result;
        }

        private async Task<TodoItem> CreateItemWithNewId()
        {
            var faker = new Faker("nl");
            
            var latestTodoNumber = await GetAutonumber();
            var todoItem = new TodoItem()
            {
                Id = (++latestTodoNumber.Resource.MaxNumber).ToString(),
                PartitionKey = PartitionKey,
                Title = faker.Name.FirstName()
            };

            var batch = _container.CreateTransactionalBatch(new PartitionKey(PartitionKey))
                .ReplaceItem(TodoAutonumber.TodoAutonumberId, latestTodoNumber.Resource,
                    new TransactionalBatchItemRequestOptions() { IfMatchEtag = latestTodoNumber.ETag })
                .CreateItem(todoItem);
            
            var batchResponse = await batch.ExecuteAsync();
            
            using (batchResponse)
            {
                TransactionalBatchOperationResult<TodoAutonumber> todoAutonumberResult = batchResponse.GetOperationResultAtIndex<TodoAutonumber>(0);
                TransactionalBatchOperationResult<TodoItem> todoItemResult = batchResponse.GetOperationResultAtIndex<TodoItem>(1);
                
                if (batchResponse.IsSuccessStatusCode)
                {
                    return todoItemResult.Resource;
                }

                if (todoAutonumberResult.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    throw new TodoCreateException(TodoCreateErrorType.AutonumberError);
                }
                
                if (todoItemResult.StatusCode == HttpStatusCode.Conflict)
                {
                    throw new TodoCreateException(TodoCreateErrorType.TodoItemError);
                }
            }

            return null;
        }
        
        private async Task<ItemResponse<TodoAutonumber>> GetAutonumber()
        {
            try 
            {
                var listItem = await _container.ReadItemAsync<TodoAutonumber>(TodoAutonumber.TodoAutonumberId, 
                    new PartitionKey(PartitionKey));

                return listItem;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                await _semaphoreSlim.WaitAsync();
                try
                {
                    return await CreateTodoAutonumber();
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            }
        }

        private async Task<ItemResponse<TodoAutonumber>> CreateTodoAutonumber()
        {
            var todoAutonumber = new TodoAutonumber
            {
                MaxNumber = 0,
                PartitionKey = PartitionKey};

            return await _container.CreateItemAsync(todoAutonumber, new PartitionKey(PartitionKey));
        }
    }
}