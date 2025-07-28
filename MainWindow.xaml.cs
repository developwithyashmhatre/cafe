using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using Newtonsoft.Json.Linq;

namespace my_cyber_cafe
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient client = new HttpClient();
        private const string BASE_URL = "http://ec2-3-110-191-79.ap-south-1.compute.amazonaws.com"; // Change to your PocketBase URL
        private string sessionId;
        private string deviceId = "device-001"; // TODO: Load this dynamically as needed
        private DateTime sessionStart;
        private int remainingSeconds;
        private DispatcherTimer countdownTimer;

        public MainWindow()
        {
            InitializeComponent();
            StartSessionAsync(deviceId).ConfigureAwait(false);
        }

        public async Task<string> StartSessionAsync(string deviceId)
        {
            var sessionData = new
            {
                in_time = DateTime.UtcNow.ToString("o"),
                device = deviceId,
                status = "Active",
                payment_type = "Pre-paid",
                payment_mode = "Cash",
                duration = 60
            };

            var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(sessionData), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{BASE_URL}/api/collections/sessions/records", content);
            var json = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var obj = JObject.Parse(json);
                sessionId = obj["id"].ToString();
                sessionStart = DateTime.UtcNow;
                remainingSeconds = 60 * 60;
                await UpdateDeviceStatus(deviceId, "Occupied");
                StartCountdown();
                return sessionId;
            }
            else
            {
                MessageBox.Show("Failed to start session.");
            }
            return null;
        }

        void StartCountdown()
        {
            if (countdownTimer != null)
            {
                countdownTimer.Stop();
            }
            countdownTimer = new DispatcherTimer();
            countdownTimer.Interval = TimeSpan.FromSeconds(1);
            countdownTimer.Tick += async (s, e) =>
            {
                remainingSeconds--;
                DashboardTimerText.Text = $"Session Remaining: {TimeSpan.FromSeconds(remainingSeconds):hh\\:mm\\:ss}";

                if (remainingSeconds % 10 == 0)
                {
                    // Optionally update time played in backend
                }

                if (remainingSeconds <= 0)
                {
                    countdownTimer.Stop();
                    ShowSessionEndPopup();
                }
            };
            countdownTimer.Start();
        }

        void ShowSessionEndPopup()
        {
            var result = MessageBox.Show("Session time over. Extend session?", "Session Ended", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                var choice = MessageBox.Show("Choose duration:\nYes = 30m, No = 1h, Cancel = 2h", "Extend Session", MessageBoxButton.YesNoCancel);
                int extendMinutes = choice switch
                {
                    MessageBoxResult.Yes => 30,
                    MessageBoxResult.No => 60,
                    MessageBoxResult.Cancel => 120,
                    _ => 0
                };
                if (extendMinutes > 0)
                {
                    ExtendSession(extendMinutes);
                }
            }
            else
            {
                EndSession().ConfigureAwait(false);
            }
        }

        async void ExtendSession(int minutes)
        {
            remainingSeconds = minutes * 60;
            var patch = new
            {
                duration = (int)(DateTime.UtcNow - sessionStart).TotalMinutes + minutes
            };
            var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(patch), Encoding.UTF8, "application/json");
            await client.PatchAsync($"{BASE_URL}/api/collections/sessions/records/{sessionId}", content);
            StartCountdown();
        }

        public async Task EndSession()
        {
            if (countdownTimer != null)
                countdownTimer.Stop();
            var payload = new
            {
                out_time = DateTime.UtcNow.ToString("o"),
                status = "Ended",
                time_played = (int)(DateTime.UtcNow - sessionStart).TotalSeconds
            };
            var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            await client.PatchAsync($"{BASE_URL}/api/collections/sessions/records/{sessionId}", content);
            await UpdateDeviceStatus(deviceId, "Available");
            MessageBox.Show("Session ended.");
        }

        public static async Task UpdateDeviceStatus(string deviceId, string status)
        {
            var payload = new { status = status };
            var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            await client.PatchAsync($"{BASE_URL}/api/collections/devices/records/{deviceId}", content);
        }

        private async void EndSession_Click(object sender, RoutedEventArgs e)
        {
            await EndSession();
        }
    }
}
