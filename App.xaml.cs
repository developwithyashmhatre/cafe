using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;

namespace my_cyber_cafe
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly string pocketBaseUrl = "http://ec2-3-110-191-79.ap-south-1.compute.amazonaws.com/api/collections/devices/records/o7jcb97abeh8euc";
        private DispatcherTimer pollingTimer;
        private static bool hasLocked = false;

        [DllImport("user32.dll")]
        public static extern bool LockWorkStation();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

        [DllImport("PowrProf.dll", SetLastError = true)]
        public static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            string deviceFile = "selectedDevice.json";
            if (!File.Exists(deviceFile))
            {
                // Show DeviceSelector window
                var selector = new DeviceSelector();
                bool? result = selector.ShowDialog();
                if (result == true)
                {
                    // After device selection, show Login
                    var login = new Login();
                    login.Show();
                }
                else
                {
                    Shutdown();
                }
            }
            else
            {
                // Device already selected, show Login
                var login = new Login();
                login.Show();
            }
            // Start polling for admin commands
            pollingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            pollingTimer.Tick += PollPocketBaseForCommands;
            pollingTimer.Start();
        }

        private async void PollPocketBaseForCommands(object sender, EventArgs e)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var response = await client.GetStringAsync(pocketBaseUrl);
                    using (JsonDocument doc = JsonDocument.Parse(response))
                    {
                        var root = doc.RootElement;
                        bool isLocked = root.GetProperty("lock").GetBoolean();
                        bool powerOff = root.GetProperty("powerOff").GetBoolean();
                        bool reboot = root.GetProperty("reboot").GetBoolean();
                        bool sleep = root.GetProperty("sleep").GetBoolean();

                        if (isLocked && !hasLocked)
                        {
                            hasLocked = true;
                            LockWorkStation();
                        }
                        else if (!isLocked)
                        {
                            hasLocked = false;
                        }
                        if (powerOff)
                        {
                            await ResetCommandField("powerOff");
                            ShutdownPC();
                        }
                        if (reboot)
                        {
                            await ResetCommandField("reboot");
                            RebootPC();
                        }
                        if (sleep)
                        {
                            await ResetCommandField("sleep");
                            PutToSleep();
                        }
                    }
                }
            }
            catch
            {
                // Optionally log or handle errors
            }
        }

        private async Task ResetCommandField(string fieldName)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var json = $"{{ \"{fieldName}\": false }}";
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PatchAsync(pocketBaseUrl, content);
                    response.EnsureSuccessStatusCode();
                }
            }
            catch
            {
                // Optionally log or handle errors
            }
        }

        private void ShutdownPC()
        {
            System.Diagnostics.Process.Start("shutdown", "/s /t 0");
        }

        private void RebootPC()
        {
            System.Diagnostics.Process.Start("shutdown", "/r /t 0");
        }

        private void PutToSleep()
        {
            SetSuspendState(false, true, true);
        }
    }
}
