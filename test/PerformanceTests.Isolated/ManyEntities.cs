// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace PerformanceTests.Isolated
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.Azure.Functions.Worker;
    using Microsoft.Azure.Functions.Worker.Http;
    using Microsoft.DurableTask;
    using Microsoft.DurableTask.Client;
    using Microsoft.DurableTask.Entities;
    using Microsoft.Extensions.Logging;

    class ManyEntities
    {
        const string EntityName = "Counter";

        public int CurrentValue { get; set; }

        public void Add(int amount) => this.CurrentValue += amount;

        public void Reset() => this.CurrentValue = 0;

        public int Get() => this.CurrentValue;

        [Function(EntityName)]
        public static Task Run([EntityTrigger] TaskEntityDispatcher dispatcher)
        {
            return dispatcher.DispatchAsync<ManyEntities>();
        }

        [Function(nameof(StartManyEntities))]
        public static async Task<HttpResponseData> StartManyEntities(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req,
            [DurableClient] DurableTaskClient client,
            FunctionContext executionContext)
        {
            var log = executionContext.GetLogger(nameof(StartManyEntities));
            log.LogInformation("C# HTTP trigger function processed a request.");

            if (!req.TryGetPositiveIntQueryStringParam("entities", out int entities) ||
                !req.TryGetPositiveIntQueryStringParam("messages", out int messages))
            {
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new
                {
                    error = "Required query string parameters are missing",
                    usage = new
                    {
                        entities = "The number of entities to create",
                        messages = "The number of messages to send to each entity",
                    },
                });
                return errorResponse;
            }

            DateTime utcNow = DateTime.UtcNow;
            string prefix = utcNow.ToString("yyyyMMdd-hhmmss");

            log.LogWarning($"Sending {messages} events to {entities} entities...");

            var tasks = new List<Task>(messages * entities);
            for (int i = 0; i < messages; i++)
            {
                for (int j = 0; j < entities; j++)
                {
                    var entityId = new EntityInstanceId(EntityName, $"{prefix}-{j:X16}");
                    tasks.Add(client.Entities.SignalEntityAsync(entityId, "Add", 1));
                }
            }

            await Task.WhenAll(tasks);

            var okResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
            await okResponse.WriteAsJsonAsync(new { message = $"Sent {messages} events to {entities} {EntityName} entities prefixed with '{prefix}'." });
            return okResponse;
        }
    }

    static class Extensions
    {
        public static bool TryGetPositiveIntQueryStringParam(this HttpRequestData req, string name, out int value)
        {
            return int.TryParse(req.Query[name], out value) && value > 0;
        }
    }
}
