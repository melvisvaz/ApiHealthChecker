using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace APIHealthCheck
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ApiHealthChecker _checker = new();
        private readonly IConfiguration _configuration;
        private bool _isInitializing = false;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            // Build configuration to read environments
            _configuration = new ConfigurationBuilder()
                .SetBasePath(System.IO.Directory.GetCurrentDirectory())
                .AddJsonFile("appSettings.json", optional: true)
                .Build();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            // Load environments from configuration (if present)
            var envsSection = _configuration.GetSection("Environments");
            string[]? envs = null;
            if (envsSection.Exists())
            {
                envs = envsSection.Get<string[]>();
            }
            else
            {
                // Discover top-level environment sections that contain ApiEndpoints
                envs = _configuration.GetChildren()
                    .Where(s => s.GetSection("ApiEndpoints").Exists())
                    .Select(s => s.Key)
                    .ToArray();
            }

            if (envs == null || envs.Length == 0)
            {
                envs = new string[] { "dev", "UAT" };
            }

            EnvCombo.ItemsSource = envs;

            // Determine default environment: first from config 'Environment', otherwise first in list
            var envFromConfig = _configuration["Environment"];
            string defaultEnv = string.Empty;
            if (!string.IsNullOrEmpty(envFromConfig)) defaultEnv = envFromConfig;
            else if (envs != null && envs.Length > 0) defaultEnv = envs[0];

            // Case-insensitive match for selection
            if (!string.IsNullOrEmpty(defaultEnv) && envs != null && envs.Any(e => string.Equals(e, defaultEnv, System.StringComparison.OrdinalIgnoreCase)))
            {
                var matched = envs.First(e => string.Equals(e, defaultEnv, System.StringComparison.OrdinalIgnoreCase));
                EnvCombo.SelectedItem = matched;
            }

            // Initial load (suppress selection-changed during this setup)
            await ReloadApisForSelectedEnvironment();
            ApiGrid.DataContext = _checker.ApiStatuses;

            _isInitializing = false;
        }

        private async Task ReloadApisForSelectedEnvironment()
        {
            var sel = EnvCombo.SelectedItem as string ?? string.Empty;
            _checker.SetEnvironment(sel);
            await _checker.LoadAndCheckApisAsync();
            // Refresh binding
            ApiGrid.ItemsSource = null;
            ApiGrid.ItemsSource = _checker.ApiStatuses;
        }

        private async void EnvCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return; // avoid firing before window finished loading
            if (_isInitializing) return; // suppressed during initial setup
            await ReloadApisForSelectedEnvironment();
        }

    }
}