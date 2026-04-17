// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace PerformanceTests.Isolated
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Functions.Worker;
    using Microsoft.Azure.Functions.Worker.Http;
    using Microsoft.DurableTask;
    using Microsoft.DurableTask.Client;
    using Microsoft.Extensions.Logging;

    public class LongHaul
    {
        [Function(nameof(StartLongHaul))]
        public static async Task<HttpResponseData> StartLongHaul(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req,
            [DurableClient] DurableTaskClient starter,
            FunctionContext executionContext)
        {
            var log = executionContext.GetLogger(nameof(StartLongHaul));
            string input = string.Empty;
            if (req.Body.CanRead)
            {
                using var reader = new System.IO.StreamReader(req.Body);
                input = await reader.ReadToEndAsync();
            }

            LongHaulOptions options = null;
            if (!string.IsNullOrEmpty(input))
            {
                try
                {
                    options = JsonSerializer.Deserialize<LongHaulOptions>(input);
                }
                catch (JsonException e)
                {
                    log.LogWarning(e, "Received bad JSON input");
                }
            }

            if (options == null || !options.IsValid())
            {
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new
                {
                    error = "Required request content is missing or invalid",
                    usage = new SortedDictionary<string, string>
                    {
                        [nameof(LongHaulOptions.TotalHours)] = "The total length of time the test should run. Example: '72' for 72 hours.",
                        [nameof(LongHaulOptions.OrchestrationsPerInterval)] = "The number of orchestrations to schedule per interval. Example: '1000' to schedule 1,000 every interval.",
                        [nameof(LongHaulOptions.Interval)] = "The frequency for scheduling orchestration batches. Example: '00:05:00' for 5 minutes.",
                    },
                });
                return errorResponse;
            }

            string instanceId = $"longhaul_{DateTime.UtcNow:yyyyMMddHHmmss}_{options.TotalHours}_{options.OrchestrationsPerInterval}_{(int)options.Interval.TotalSeconds}";
            var state = new LongHaulState 
            { 
                Options = options,
                Deadline = DateTime.UtcNow.AddHours(options.TotalHours),
            };
            // Schedule with orchestration name and input
            // Note: InstanceId specification may require different API in isolated worker
            string scheduledId = await starter.ScheduleNewOrchestrationInstanceAsync(
                nameof(LongHaulOrchestrator),
                state);
            log.LogWarning("Scheduled long-haul orchestrator, got ID: {scheduledId}, requested: {instanceId}", scheduledId, instanceId);
            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { statusQueryGetUri = $"http://localhost:7071/runtime/webhooks/durabletask/instances/{instanceId}" });
            return response;
        }

        [Function(nameof(LongHaulOrchestrator))]
        public static async Task LongHaulOrchestrator(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            LongHaulState state = context.GetInput<LongHaulState>();
            if (context.CurrentUtcDateTime > state.Deadline)
            {
                return;
            }

            state.Iteration++;
            int currentTotal = state.TotalOrchestrationsCompleted;

            List<Task> tasks = Enumerable
                .Range(0, state.Options.OrchestrationsPerInterval)
                .Select(i =>
                {
                    int suffix = state.TotalOrchestrationsCompleted + i;
                    string subInstanceId = $"{context.InstanceId}_{suffix:X8}";
                    return context.CallSubOrchestratorAsync(
                        nameof(ManySequences.HelloCities),
                        subInstanceId);
                })
                .ToList();

            context.SetCustomStatus(state);

            await Task.WhenAll(tasks);

            state.TotalOrchestrationsCompleted += tasks.Count;
            context.SetCustomStatus(state);

            DateTime nextRunTime = context.CurrentUtcDateTime.Add(state.Options.Interval);
            await context.CreateTimer(nextRunTime, CancellationToken.None);

            context.ContinueAsNew(state);
        }

        class LongHaulOptions
        {
            public int TotalHours { get; set; }
            public int OrchestrationsPerInterval { get; set; }
            public TimeSpan Interval { get; set; }

            public bool IsValid() => 
                this.TotalHours > 0 &&
                this.OrchestrationsPerInterval > 0 &&
                this.Interval > TimeSpan.Zero;
        }

        class LongHaulState
        {
            public LongHaulOptions Options { get; set; }
            public DateTime Deadline { get; set; }
            public int TotalOrchestrationsCompleted { get; set; }
            public int Iteration { get; set; }
        }
    }
}
