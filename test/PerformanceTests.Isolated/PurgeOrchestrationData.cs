// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace PerformanceTests.Isolated
{
    using System;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using Microsoft.Azure.Functions.Worker;
    using Microsoft.Azure.Functions.Worker.Http;
    using Microsoft.DurableTask.Client;
    using Microsoft.Extensions.Logging;

    public static class PurgeOrchestrationData
    {
        [Function("PurgeOrchestrationData")]
        public static async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestData req,
            [DurableClient] DurableTaskClient client,
            FunctionContext executionContext)
        {
            var log = executionContext.GetLogger(nameof(PurgeOrchestrationData));
            log.LogWarning("Purging all orchestration data from the database");

            int totalDeleted = 0;
            bool finished = false;

            // Note: Isolated worker's purge APIs differ from in-process
            // For now, log that purge is not fully supported in isolated mode
            log.LogWarning("Purge operation is not fully implemented for isolated worker model yet.");

            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                totalDeleted,
                finished,
                message = "Purge operation not yet implemented for isolated worker",
            });
            return response;
        }
    }
}
