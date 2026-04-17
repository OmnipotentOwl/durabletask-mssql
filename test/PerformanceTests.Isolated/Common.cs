// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace PerformanceTests.Isolated
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Functions.Worker;
    using Microsoft.DurableTask.Client;
    using Microsoft.Extensions.Logging;

    static class Common
    {
        public static async Task<string> ScheduleManyInstances(
            DurableTaskClient client,
            ILogger log,
            string orchestrationName,
            int count,
            string prefix)
        {
            DateTime utcNow = DateTime.UtcNow;
            prefix += utcNow.ToString("yyyyMMdd-hhmmss");

            log.LogWarning($"Scheduling {count} orchestration(s) with a prefix of '{prefix}'...");

            await Enumerable.Range(0, count).ParallelForEachAsync(200, i =>
            {
                string instanceId = $"{prefix}-{i:X16}";
                return client.ScheduleNewOrchestrationInstanceAsync(orchestrationName, instanceId);
            });

            log.LogWarning($"All {count} orchestrations were scheduled successfully!");
            return prefix;
        }

        [Function(nameof(SayHello))]
        public static string SayHello([ActivityTrigger] string name, string instanceId, FunctionContext executionContext)
        {
            var logger = executionContext.GetLogger(nameof(SayHello));
            logger.LogInformation("Hello from {city} - {id}", name, instanceId);
            return $"Hello {name}!";
        }

        public static async Task ParallelForEachAsync<T>(this IEnumerable<T> items, int maxConcurrency, Func<T, Task> action)
        {
            List<Task> tasks;
            if (items is ICollection<T> itemCollection)
            {
                tasks = new List<Task>(itemCollection.Count);
            }
            else
            {
                tasks = new List<Task>();
            }

            using var semaphore = new SemaphoreSlim(maxConcurrency);
            foreach (T item in items)
            {
                tasks.Add(InvokeThrottledAction(item, action, semaphore));
            }

            await Task.WhenAll(tasks);
        }

        static async Task InvokeThrottledAction<T>(T item, Func<T, Task> action, SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            try
            {
                await action(item);
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
