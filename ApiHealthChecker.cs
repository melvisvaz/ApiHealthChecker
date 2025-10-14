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

        public async Task LoadAndCheckApisAsync()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
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

            // Clear any previous results when reloading for a different environment
            ApiStatuses.Clear();

            foreach (var api in endpoints)
            {
                var client = new RestClient(api.Url);
                var request = new RestRequest();
                try
                {
                    var response = await client.ExecuteAsync(request);
                    api.Status = response.IsSuccessful ? "Healthy" : "Unhealthy";
                }
                catch
                {
                    api.Status = "Unhealthy";
                }

                ApiStatuses.Add(api);
            }
        }
    }
}