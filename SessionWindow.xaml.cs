using System;
using System.Windows;
using System.Windows.Threading;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.IO;
using System.Threading.Tasks;

namespace my_cyber_cafe
{
    public partial class SessionWindow : Window
    {
        private readonly DateTime _sessionStart;
        private readonly DispatcherTimer _timer;

        public SessionWindow(DateTime sessionStart)
        {
            InitializeComponent();
            _sessionStart = sessionStart;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        public SessionWindow() : this(DateTime.Now) { }

        private void Timer_Tick(object sender, EventArgs e)
        {
            var duration = DateTime.Now - _sessionStart;
            SessionDurationText.Text = $"Session Duration: {duration:hh\\:mm\\:ss}";
        }

        private async void EndSession_Click(object sender, RoutedEventArgs e)
        {
            await EndSessionAsync();
            var login = new Login();
            login.Show();
            this.Close();
        }

        private async Task EndSessionAsync()
        {
            _timer.Stop();
            string sessionId = null;
            string deviceId = null;
            const string BASE_URL = "http://ec2-3-110-191-79.ap-south-1.compute.amazonaws.com";
            var client = new HttpClient();
            // Read admin or user token from file or hardcode for testing
            string token = File.Exists("authToken.txt") ? File.ReadAllText("authToken.txt").Trim() : null;
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            // Get sessionId
            if (File.Exists("currentSessionId.txt"))
                sessionId = File.ReadAllText("currentSessionId.txt").Trim();
            // Get deviceId
            if (File.Exists("selectedDevice.json"))
            {
                var deviceJson = File.ReadAllText("selectedDevice.json");
                dynamic deviceObj = JsonConvert.DeserializeObject(deviceJson);
                deviceId = deviceObj?.id;
            }

            // Calculate duration and time played
            var now = DateTime.UtcNow;
            var duration = (int)(now - _sessionStart.ToUniversalTime()).TotalMinutes;
            var timePlayed = (int)(now - _sessionStart.ToUniversalTime()).TotalSeconds;

            // 1. Update session record
            if (!string.IsNullOrEmpty(sessionId))
            {
                var payload = new
                {
                    out_time = now.ToString("o"),
                    status = "Closed",
                    duration = duration,
                    time_played = timePlayed
                };
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await client.PatchAsync($"{BASE_URL}/api/collections/sessions/records/{sessionId}", content);
                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show("Failed to update session: " + await response.Content.ReadAsStringAsync());
                }
            }

            // 2. Mark device as available
            if (!string.IsNullOrEmpty(deviceId))
            {
                await UpdateDeviceStatus(client, BASE_URL, deviceId, "Available");
            }

            // 3. Remove user and session info
            try { if (File.Exists("sessionInfo.json")) File.Delete("sessionInfo.json"); } catch { }
            try { if (File.Exists("loginInfo.json")) File.Delete("loginInfo.json"); } catch { }
            try { if (File.Exists("currentSessionId.txt")) File.Delete("currentSessionId.txt"); } catch { }
        }

        private static async Task UpdateDeviceStatus(HttpClient client, string BASE_URL, string deviceId, string status)
        {
            var content = new StringContent(
                JsonConvert.SerializeObject(new { status = status }),
                Encoding.UTF8,
                "application/json"
            );
            var response = await client.PatchAsync($"{BASE_URL}/api/collections/devices/records/{deviceId}", content);
            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show("Failed to update device: " + await response.Content.ReadAsStringAsync());
            }
        }
    }
} 