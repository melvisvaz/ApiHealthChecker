using APIHealthCheck;
using Microsoft.Extensions.Configuration;
using RestSharp;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace APIHealthCheck
{
    public class ApiHealthChecker
    {
        public string Environment { get; private set; } = string.Empty;

        public void SetEnvironment(string env)
        {
            Environment = env ?? string.Empty;
        }

        public ObservableCollection<ApiStatus> ApiStatuses { get; set; } = new();

        public record ApiProgress(ApiStatus Status, int Completed, int Total);

        public async Task LoadAndCheckApisAsync(IProgress<ApiProgress>? progress = null)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                // The repository uses appSettings.json (capital S) so read that file
                .AddJsonFile("appSettings.json", optional: true)
                .Build();

            var endpoints = new List<ApiStatus>();

            if (!string.IsNullOrEmpty(Environment))
            {
                var envSection = config.GetSection(Environment);
                if (envSection.Exists())
                {
                    var endpointsSection = envSection.GetSection("ApiEndpoints");
                    if (endpointsSection.Exists())
                    {
                        ConfigurationBinder.Bind(endpointsSection, endpoints);
                    }
                }

                if (endpoints.Count == 0)
                {
                    var legacyKey = $"ApiEndpointsByEnvironment:{Environment}";
                    var legacySection = config.GetSection(legacyKey);
                    if (legacySection.Exists())
                    {
                        ConfigurationBinder.Bind(legacySection, endpoints);
                    }
                }
            }

            // If still empty, fall back to top-level ApiEndpoints array (legacy)
            if (endpoints.Count == 0)
            {
                var topLevel = config.GetSection("ApiEndpoints");
                if (topLevel.Exists())
                {
                    ConfigurationBinder.Bind(topLevel, endpoints);
                }
                else
                {
                    // As a last resort, find the first top-level env section that contains ApiEndpoints
                    var firstEnv = config.GetChildren().FirstOrDefault(c => c.GetSection("ApiEndpoints").Exists());
                    if (firstEnv != null)
                    {
                        ConfigurationBinder.Bind(firstEnv.GetSection("ApiEndpoints"), endpoints);
                    }
                }
            }

            // If Environment hasn't been set programmatically, read it from configuration
            if (string.IsNullOrEmpty(Environment))
            {
                var envFromConfig = config["Environment"];
                if (!string.IsNullOrEmpty(envFromConfig))
                {
                    Environment = envFromConfig;
                }
            }

            // We'll perform checks in parallel and report results as they complete.
            // Do not mutate ApiStatuses from background threads; the caller should
            // bind to ApiStatuses and add items when progress reports them.

            var checkTasks = new List<Task<ApiStatus>>();
            foreach (var api in endpoints)
            {
                // Capture local copy to avoid closure issues
                var apiCopy = new ApiStatus { Name = api.Name, Url = api.Url };
                checkTasks.Add(Task.Run(async () =>
                {
                    var client = new RestClient(apiCopy.Url);
                    var request = new RestRequest();
                    try
                    {
                        var response = await client.ExecuteAsync(request);
                        apiCopy.Status = response.IsSuccessful ? "Healthy" : "Unhealthy";
                    }
                    catch
                    {
                        apiCopy.Status = "Unhealthy";
                    }
                    return apiCopy;
                }));
            }

            // Process tasks as they complete and report each result via IProgress
            var total = checkTasks.Count;
            var completed = 0;
            while (checkTasks.Count > 0)
            {
                var finished = await Task.WhenAny(checkTasks);
                checkTasks.Remove(finished);
                var result = await finished;
                completed++;
                progress?.Report(new ApiProgress(result, completed, total));
            }
        }
    }
}