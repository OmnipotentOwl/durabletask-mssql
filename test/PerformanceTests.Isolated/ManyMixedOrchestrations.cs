// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace PerformanceTests.Isolated
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Azure.Functions.Worker;
    using Microsoft.Azure.Functions.Worker.Http;
    using Microsoft.DurableTask;
    using Microsoft.DurableTask.Client;
    using Microsoft.Extensions.Logging;

    class ManyMixedOrchestrations
    {
        [Function(nameof(StartManyMixedOrchestrations))]
        public static async Task<HttpResponseData> StartManyMixedOrchestrations(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req,
            [DurableClient] DurableTaskClient starter,
            FunctionContext executionContext)
        {

            var log = executionContext.GetLogger(nameof(ManyMixedOrchestrations));
            if (!int.TryParse(req.Query["count"], out int count) || count < 1)
            {
                var response = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await response.WriteAsJsonAsync(new { message = "A 'count' query string parameter is required and it must contain a positive number." });
                return response;
            }

            string initialPrefix = req.Query["prefix"] ?? string.Empty;

            string finalPrefix = await Common.ScheduleManyInstances(starter, log, nameof(MixedOrchestration), count, initialPrefix);
            var okResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
            await okResponse.WriteAsJsonAsync(new { message = $"Scheduled {count} orchestrations prefixed with '{finalPrefix}'." });
            return okResponse;
        }

        [Function(nameof(MixedOrchestration))]
        public static async Task MixedOrchestration(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            // Flow:
            // 1. Call an activity function
            // 2. Start a sub-orchestration  
            // 3. Call an activity with a retry policy and catch the exception
            await context.CallActivityAsync(nameof(Common.SayHello), "World");

            string callbackEventName = "CallbackEvent";
            var subInput = (context.InstanceId, callbackEventName);
            var subOrchestratorOptions = new SubOrchestrationOptions
            {
                InstanceId = $"{context.InstanceId}-sub",
            };
            Task subOrchestration = context.CallSubOrchestratorAsync(nameof(CallMeBack), subInput, subOrchestratorOptions);

            Task onCalledBack = context.WaitForExternalEvent<object>(callbackEventName, TimeSpan.FromMinutes(1));

            await Task.WhenAll(subOrchestration, onCalledBack);

            try
            {
                var taskOptions = new TaskOptions
                {
                    Retry = new RetryPolicy(
                        maxNumberOfAttempts: 2,
                        firstRetryInterval: TimeSpan.FromSeconds(5)),
                };
                await context.CallActivityAsync(
                    nameof(Throw),
                    null,
                    taskOptions);
            }
            catch (TaskFailedException)
            {
                // no-op
            }
        }

        [Function(nameof(CallMeBack))]
        public static Task CallMeBack([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            (string callbackInstance, string eventName) = context.GetInput<(string, string)>();
            return context.CallActivityAsync(
                nameof(RaiseEvent),
                input: (callbackInstance, eventName));
        }

        [Function(nameof(RaiseEvent))]
        public static Task RaiseEvent(
            [ActivityTrigger] (string instanceId, string eventName) input,
            [DurableClient] DurableTaskClient client)
        {
            return client.RaiseEventAsync(input.instanceId, input.eventName);
        }

        [Function(nameof(Throw))]
        public static void Throw([ActivityTrigger] TaskActivityContext ctx) => throw new Exception("Kah-BOOOOM!!!");
    }
}
