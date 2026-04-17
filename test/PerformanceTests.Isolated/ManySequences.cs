// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace PerformanceTests.Isolated
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using Microsoft.Azure.Functions.Worker;
    using Microsoft.Azure.Functions.Worker.Http;
    using Microsoft.DurableTask;
    using Microsoft.DurableTask.Client;
    using Microsoft.Extensions.Logging;

    public static class ManySequences
    {
        [Function(nameof(StartManySequences))]
        public static async Task<HttpResponseData> StartManySequences(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req,
            [DurableClient] DurableTaskClient starter,
            FunctionContext executionContext)
        {
            var log = executionContext.GetLogger(nameof(StartManySequences));
            log.LogInformation("C# HTTP trigger function processed a request.");

            if (!int.TryParse(req.Query["count"], out int count) || count < 1)
            {
                var response = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await response.WriteAsJsonAsync(new { message = "A 'count' query string parameter is required and it must contain a positive number." });
                return response;
            }

            string initialPrefix = req.Query["prefix"] ?? string.Empty;

            string finalPrefix = await Common.ScheduleManyInstances(starter, log, nameof(HelloCities), count, initialPrefix);
            var okResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
            await okResponse.WriteAsJsonAsync(new { message = $"Scheduled {count} orchestrations prefixed with '{finalPrefix}'. ActivityId: {Activity.Current?.Id}" });
            return okResponse;
        }

        [Function(nameof(HelloCities))]
        public static async Task<List<string>> HelloCities(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger(nameof(HelloCities));
            logger.LogInformation("Starting '{name}' orchestration with ID = '{id}'", context.Name, context.InstanceId);

            var outputs = new List<string>
            {
                await context.CallActivityAsync<string>(nameof(Common.SayHello), "Tokyo"),
                await context.CallActivityAsync<string>(nameof(Common.SayHello), "Seattle"),
                await context.CallActivityAsync<string>(nameof(Common.SayHello), "London"),
                await context.CallActivityAsync<string>(nameof(Common.SayHello), "Amsterdam"),
                await context.CallActivityAsync<string>(nameof(Common.SayHello), "Mumbai")
            };

            logger.LogInformation("Finished '{name}' orchestration with ID = '{id}'", context.Name, context.InstanceId);
            return outputs;
        }
    }
}
