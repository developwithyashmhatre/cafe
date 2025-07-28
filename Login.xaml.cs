using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;

namespace my_cyber_cafe
{
    public partial class Login : Window
    {
        private DispatcherTimer sessionCheckTimer;

        public class AuthRequest
        {
            public string identity { get; set; }
            public string password { get; set; }
        }

        public class AuthRecord
        {
            public string id { get; set; }
            public string email { get; set; }
        }

        public class AuthResponse
        {
            public string token { get; set; }
            public AuthRecord record { get; set; }
        }

        public class SessionRequest
        {
            public string in_time { get; set; }
            public string status { get; set; } = "Active";
            public string payment_type { get; set; } = "Pre-paid";
            public string payment_mode { get; set; } = "Cash";
            public int snacks_total { get; set; } = 0;
            public int session_total { get; set; } = 0;
            public int total_amount { get; set; } = 0;
            public int amount_paid { get; set; } = 0;
            public int discount_amount { get; set; } = 0;
            public int discount_rate { get; set; } = 0;
            public int duration { get; set; } = 0;
            public int Cash { get; set; } = 0;
            public int UPI { get; set; } = 0;
            public int Membership { get; set; } = 0;
            public int time_played { get; set; } = 0;
            public int adjusted_deduction { get; set; } = 0;
            public string device { get; set; }
            public string user { get; set; }
        }

        public class PocketBaseAPI
        {
            private static readonly HttpClient client = new HttpClient();
            private const string BASE_URL = "http://ec2-3-110-191-79.ap-south-1.compute.amazonaws.com";

            public static async Task<AuthResponse> LoginAsync(string username, string password)
            {
                var url = $"{BASE_URL}/api/collections/clients/auth-with-password";
                var authPayload = new AuthRequest { identity = username, password = password };
                var jsonContent = new StringContent(JsonConvert.SerializeObject(authPayload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, jsonContent);

                if (!response.IsSuccessStatusCode)
                    throw new Exception("Login failed: " + response.StatusCode);

                var responseBody = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<AuthResponse>(responseBody);
            }

            public static async Task<bool> HasActiveSessionAsync(string deviceId)
            {
                var filter = Uri.EscapeDataString($"(status='Active') && (device='{deviceId}')");
                var url = $"{BASE_URL}/api/collections/sessions/records?filter={filter}";
                var response = await client.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                dynamic result = JsonConvert.DeserializeObject(body);
                return result.items.Count > 0;
            }

            public static async Task<string> StartSessionAsync(string deviceId, string userId)
            {
                var session = new SessionRequest
                {
                    in_time = DateTime.UtcNow.ToString("o"),
                    device = deviceId,
                    user = userId
                };

                var json = JsonConvert.SerializeObject(session);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var url = $"{BASE_URL}/api/collections/sessions/records";

                var response = await client.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                    return null;

                var responseBody = await response.Content.ReadAsStringAsync();
                dynamic obj = JsonConvert.DeserializeObject(responseBody);
                return obj.id;
            }
        }

        public Login()
        {
            InitializeComponent();
            LoginBtn.Click += LoginBtn_Click;

            sessionCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            sessionCheckTimer.Tick += SessionCheckTimer_Tick;
            sessionCheckTimer.Start();
        }

        private async void SessionCheckTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (File.Exists("selectedDevice.json"))
                {
                    var deviceJson = File.ReadAllText("selectedDevice.json");
                    dynamic deviceObj = JsonConvert.DeserializeObject(deviceJson);
                    string deviceId = deviceObj?.Id;

                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        bool hasSession = await PocketBaseAPI.HasActiveSessionAsync(deviceId);
                        if (hasSession)
                        {
                            sessionCheckTimer.Stop(); // Stop further checks

                            string username = "yash";
                            string password = "yashop8848";

                            var authResult = await PocketBaseAPI.LoginAsync(username, password);
                            File.WriteAllText("loginInfo.json", JsonConvert.SerializeObject(authResult.record));

                            var dashboard = new Dashboard();
                            dashboard.Show();
                            this.Close();
                        }
                    }
                }
            }
            catch
            {
                // Optional: logging
            }
        }

        private async void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            string username = EmailBox.Text.Trim();
            string password = PasswordBox.Password.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ErrorText.Text = "Please enter email and password.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                var authResult = await PocketBaseAPI.LoginAsync(username, password);

                File.WriteAllText("sessionInfo.json", JsonConvert.SerializeObject(new
                {
                    token = authResult.token,
                    user = authResult.record,
                    sessionStart = DateTime.Now
                }));
                File.WriteAllText("loginInfo.json", JsonConvert.SerializeObject(authResult.record));

                string deviceId = null;
                if (File.Exists("selectedDevice.json"))
                {
                    var deviceJson = File.ReadAllText("selectedDevice.json");
                    dynamic deviceObj = JsonConvert.DeserializeObject(deviceJson);
                    deviceId = deviceObj?.Id;
                }

                if (string.IsNullOrEmpty(deviceId))
                {
                    MessageBox.Show("Device not selected. Please restart the app.");
                    return;
                }

                bool alreadyActive = await PocketBaseAPI.HasActiveSessionAsync(deviceId);
                if (alreadyActive)
                {
                    MessageBox.Show("An active session already exists for this device.");
                    return;
                }

                string sessionId = await PocketBaseAPI.StartSessionAsync(deviceId, authResult.record.id);
                if (!string.IsNullOrEmpty(sessionId))
                {
                    File.WriteAllText("currentSessionId.txt", sessionId);
                    sessionCheckTimer?.Stop(); // Stop polling if manual login successful
                    var dashboard = new Dashboard();
                    dashboard.Show();
                    this.Close();
                }
                else
                {
                    MessageBox.Show("Failed to start session.");
                }
            }
            catch (Exception ex)
            {
                ErrorText.Text = "Login failed: " + ex.Message;
                ErrorText.Visibility = Visibility.Visible;
            }
        }
    }
}
