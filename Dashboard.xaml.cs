using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.ObjectModel;

namespace my_cyber_cafe
{
    /// <summary>
    /// Interaction logic for Dashboard.xaml
    /// </summary>
    public partial class Dashboard : Window
    {
        private DispatcherTimer sessionTimer;
        private DispatcherTimer clockTimer;
        private TimeSpan sessionDuration; // Store the intended session duration if needed
        private DateTime sessionStartTime;
        private bool sessionActive = false;
        private const string BASE_URL = "http://ec2-3-110-191-79.ap-south-1.compute.amazonaws.com"; // Change as needed
        // private const decimal RATE_PER_HOUR = 30m; // ₹30 per hour
        private decimal ratePerHour = 30m; // Default, will be updated dynamically
        private string chatSessionId = null;
        private ObservableCollection<string> chatMessages = new ObservableCollection<string>();
        private HashSet<string> displayedMessageIds = new HashSet<string>();
        private DispatcherTimer chatPollTimer;
        private bool chatActive = false;

        public Dashboard()
        {
            InitializeComponent();
            InitTimers();

            // --- New: Set ratePerHour dynamically ---
            string deviceType = "PC"; // default
            if (File.Exists("selectedDevice.json"))
            {
                var deviceJson = File.ReadAllText("selectedDevice.json");
                dynamic deviceObj = JsonConvert.DeserializeObject(deviceJson);
                deviceType = deviceObj?.type ?? "PC";
            }
            // Fire and forget, or await if you want to block UI until loaded
            _ = SetRatePerHourAsync(deviceType);

            StartSession(TimeSpan.FromHours(1)); // Default 1 hour session
            // Chat UI setup
            ChatMessagesListBox.ItemsSource = chatMessages;
            SendChatMessageButton.IsEnabled = false;
            // Sync chat session status on startup
            _ = SyncChatSessionFileWithBackend();
        }

        private void InitTimers()
        {
            // Timer for current time
            clockTimer = new DispatcherTimer();
            clockTimer.Interval = TimeSpan.FromSeconds(1);
            clockTimer.Tick += (s, e) =>
            {
                UpdateCurrentTime();
            };
            clockTimer.Start();

            // Timer for session elapsed time (count up)
            sessionTimer = new DispatcherTimer();
            sessionTimer.Interval = TimeSpan.FromSeconds(1);
            sessionTimer.Tick += (s, e) =>
            {
                if (sessionActive)
                {
                    UpdateElapsedTime();
                    // Optionally, end session after sessionDuration
                    if (sessionDuration != TimeSpan.Zero && (DateTime.Now - sessionStartTime) >= sessionDuration)
                    {
                        _ = EndSession();
                    }
                }
            };
        }

        private void StartSession(TimeSpan duration)
        {
            sessionStartTime = DateTime.Now;
            sessionDuration = duration; // Store the intended session duration
            sessionActive = true;
            sessionTimer.Start();
            UpdateElapsedTime();
        }

        private void UpdateCurrentTime()
        {
            if (CurrentTimeText != null)
                CurrentTimeText.Text = $"Current Time: {DateTime.Now:HH:mm:ss}";
        }

        private async Task<decimal> GetRateForDeviceTypeAsync(string deviceType)
        {
            var client = new HttpClient();
            string token = File.Exists("authToken.txt") ? File.ReadAllText("authToken.txt").Trim() : null;
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // Filter: type = deviceType
            string filter = System.Web.HttpUtility.UrlEncode($"type=\"{deviceType}\"");
            var url = $"{BASE_URL}/api/collections/groups/records?filter={filter}";
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var respJson = await response.Content.ReadAsStringAsync();
                dynamic respObj = JsonConvert.DeserializeObject(respJson);
                if (respObj.items != null && respObj.items.Count > 0)
                {
                    decimal price = respObj.items[0].price;
                    return price;
                }
            }
            return 30m; // fallback default
        }

        private async Task SetRatePerHourAsync(string deviceType)
        {
            ratePerHour = await GetRateForDeviceTypeAsync(deviceType);
        }

        private decimal CalculateCost(DateTime start, DateTime end, decimal _)
        {
            var minutes = (decimal)(end - start).TotalMinutes;
            var hours = minutes / 60m;
            return Math.Round(ratePerHour * hours, 2);
        }

        private void UpdateElapsedTime()
        {
            var elapsed = DateTime.Now - sessionStartTime;
            if (SessionTimeText != null)
                SessionTimeText.Text = elapsed.ToString(@"hh\:mm\:ss");
            if (TopSessionTimeText != null)
                TopSessionTimeText.Text = elapsed.ToString(@"hh\:mm\:ss");
            // Update session cost
            if (SessionCostText != null)
            {
                decimal cost = CalculateCost(sessionStartTime, DateTime.Now, ratePerHour);
                SessionCostText.Text = $"₹{cost}";
            }
        }

        private async Task EndSession()
        {
            sessionActive = false;
            sessionTimer.Stop();
            // Calculate and show final cost
            decimal finalCost = CalculateCost(sessionStartTime, DateTime.Now, ratePerHour);
            if (SessionCostText != null)
                SessionCostText.Text = $"₹{finalCost}";

            // --- Begin new code ---
            var client = new HttpClient();
            // Read admin or user token from file or hardcode for testing
            string token = File.Exists("authToken.txt") ? File.ReadAllText("authToken.txt").Trim() : null;
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            string sessionId = null;
            string deviceId = null;
            // Get sessionId
            if (File.Exists("currentSessionId.txt"))
                sessionId = File.ReadAllText("currentSessionId.txt").Trim();
            // Get deviceId
            if (File.Exists("selectedDevice.json"))
            {
                var deviceJson = File.ReadAllText("selectedDevice.json");
                dynamic deviceObj = JsonConvert.DeserializeObject(deviceJson);
                deviceId = deviceObj?.id ?? deviceObj?.Id;
            }

            // Calculate duration and time played
            var now = DateTime.UtcNow;
            var duration = (int)(now - sessionStartTime.ToUniversalTime()).TotalMinutes;
            var timePlayed = (int)(now - sessionStartTime.ToUniversalTime()).TotalSeconds;

            // 1. Update session record
            if (!string.IsNullOrEmpty(sessionId))
            {
                // Calculate values for the payload
                string inTime = sessionStartTime.ToUniversalTime().ToString("o");
                string outTime = now.ToString("o");
                int snacksTotal = 0; // No snacks logic yet
                decimal sessionTotal = CalculateCost(sessionStartTime, DateTime.Now, ratePerHour); // Session cost
                decimal totalAmount = sessionTotal; // No snacks, so same as session
                decimal amountPaid = sessionTotal; // Assume paid in full (adjust as needed)
                string paymentType = "Pre-paid"; // Default
                int membership = 0; // No membership logic yet
                int adjustedDeduction = 0; // No deduction logic yet

                var payload = new
                {
                    in_time = inTime,
                    out_time = outTime,
                    snacks_total = snacksTotal,
                    session_total = sessionTotal,
                    total_amount = totalAmount,
                    amount_paid = amountPaid,
                    duration = duration,
                    status = "Closed",
                    payment_type = paymentType,
                    Membership = membership,
                    device = deviceId,
                    time_played = timePlayed,
                    adjusted_deduction = adjustedDeduction
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
            // --- End new code ---

            MessageBox.Show("Session Ended.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            // Optionally, redirect to Login window
            var login = new Login();
            login.Show();
            this.Close();
        }

        private void ExtendSession(TimeSpan extraTime)
        {
            sessionDuration = sessionDuration.Add(extraTime); // Extend the intended session duration
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

        // Button event handlers (wire these up in XAML or code-behind)
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private async void StartChatSessionButton_Click(object sender, RoutedEventArgs e)
        {
            if (chatActive)
            {
                MessageBox.Show("Chat session already active.");
                return;
            }
            // Get current device ID
            string deviceId = null;
            if (File.Exists("selectedDevice.json"))
            {
                var deviceJson = File.ReadAllText("selectedDevice.json");
                dynamic deviceObj = JsonConvert.DeserializeObject(deviceJson);
                deviceId = deviceObj?.id ?? deviceObj?.Id;
            }
            if (string.IsNullOrEmpty(deviceId))
            {
                MessageBox.Show("Device not selected.");
                return;
            }
            // Prepare payload
            var payload = new
            {
                device = deviceId,
                started_at = DateTime.UtcNow.ToString("o"),
                active = true,
                created_by = "client"
            };
            var client = new HttpClient();
            string token = File.Exists("authToken.txt") ? File.ReadAllText("authToken.txt").Trim() : null;
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{BASE_URL}/api/collections/chat_sessions/records", content);
            if (response.IsSuccessStatusCode)
            {
                var respJson = await response.Content.ReadAsStringAsync();
                dynamic respObj = JsonConvert.DeserializeObject(respJson);
                chatSessionId = respObj?.id;
                File.WriteAllText("currentChatSessionId.txt", chatSessionId);
                chatActive = true;
                SendChatMessageButton.IsEnabled = true;
                MessageBox.Show("Chat session started!");
                StartChatPolling();
            }
            else
            {
                MessageBox.Show("Failed to start chat session: " + await response.Content.ReadAsStringAsync());
            }
        }

        // Send chat message
        private async void SendChatMessageButton_Click(object sender, RoutedEventArgs e)
        {
            if (!chatActive || string.IsNullOrEmpty(chatSessionId))
            {
                MessageBox.Show("No active chat session.");
                return;
            }
            string message = ChatMessageTextBox.Text.Trim();
            if (string.IsNullOrEmpty(message)) return;
            // Get device ID
            string deviceId = null;
            if (File.Exists("selectedDevice.json"))
            {
                var deviceJson = File.ReadAllText("selectedDevice.json");
                dynamic deviceObj = JsonConvert.DeserializeObject(deviceJson);
                deviceId = deviceObj?.id ?? deviceObj?.Id;
            }
            var payload = new
            {
                chat_session = chatSessionId,
                sender_device = deviceId,
                message = message,
                is_admin = false
            };
            var client = new HttpClient();
            string token = File.Exists("authToken.txt") ? File.ReadAllText("authToken.txt").Trim() : null;
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{BASE_URL}/api/collections/chat_messages/records", content);
            if (response.IsSuccessStatusCode)
            {
                ChatMessageTextBox.Text = "";
                // Do not add to chatMessages here; let polling handle it
            }
            else
            {
                MessageBox.Show("Failed to send message: " + await response.Content.ReadAsStringAsync());
            }
        }

        // Poll for new chat messages (simple polling, replace with websocket for real-time)
        private void StartChatPolling()
        {
            if (chatPollTimer != null)
            {
                chatPollTimer.Stop();
                chatPollTimer = null;
            }
            chatPollTimer = new DispatcherTimer();
            chatPollTimer.Interval = TimeSpan.FromSeconds(2);
            chatPollTimer.Tick += async (s, e) => {
                await FetchChatMessages();
                await SyncChatSessionFileWithBackend(); // Periodically sync file and backend
            };
            chatPollTimer.Start();
        }

        private DateTime lastMessageTimestamp = DateTime.MinValue;
        private async Task FetchChatMessages()
        {
            if (string.IsNullOrEmpty(chatSessionId)) return;
            var client = new HttpClient();
            string token = File.Exists("authToken.txt") ? File.ReadAllText("authToken.txt").Trim() : null;
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            // Only fetch messages for this session, after lastMessageTimestamp
            string filter = System.Web.HttpUtility.UrlEncode($"chat_session='{chatSessionId}'");
            var url = $"{BASE_URL}/api/collections/chat_messages/records?filter={filter}&sort=+timestamp";
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var respJson = await response.Content.ReadAsStringAsync();
                dynamic respObj = JsonConvert.DeserializeObject(respJson);
                foreach (var item in respObj.items)
                {
                    string msgId = item.id;
                    if (displayedMessageIds.Contains(msgId)) continue;
                    string msg = item.message;
                    bool isAdmin = item.is_admin == true;
                    string sender = isAdmin ? "Admin" : "You";
                    DateTime ts = item.timestamp != null ? DateTime.Parse((string)item.timestamp) : DateTime.MinValue;
                    chatMessages.Add($"{sender}: {msg}");
                    displayedMessageIds.Add(msgId);
                    if (ts > lastMessageTimestamp) lastMessageTimestamp = ts;
                }
            }
        }

        // End chat session (call when session ends or user logs out)
        private async Task EndChatSession()
        {
            if (!chatActive || string.IsNullOrEmpty(chatSessionId))
            {
                // Remove file if exists
                if (File.Exists("currentChatSessionId.txt"))
                    File.Delete("currentChatSessionId.txt");
                return;
            }
            var client = new HttpClient();
            string token = File.Exists("authToken.txt") ? File.ReadAllText("authToken.txt").Trim() : null;
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var payload = new
            {
                ended_at = DateTime.UtcNow.ToString("o"),
                active = false
            };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = await client.PatchAsync($"{BASE_URL}/api/collections/chat_sessions/records/{chatSessionId}", content);
            if (response.IsSuccessStatusCode)
            {
                chatActive = false;
                chatSessionId = null;
                if (File.Exists("currentChatSessionId.txt"))
                    File.Delete("currentChatSessionId.txt");
                chatMessages.Clear();
                SendChatMessageButton.IsEnabled = false;
                if (chatPollTimer != null) chatPollTimer.Stop();
            }
            else
            {
                // Even if backend fails, remove file for safety
                if (File.Exists("currentChatSessionId.txt"))
                    File.Delete("currentChatSessionId.txt");
            }
        }

        // --- New method to sync chat session file and backend status ---
        private async Task SyncChatSessionFileWithBackend()
        {
            string fileSessionId = null;
            if (File.Exists("currentChatSessionId.txt"))
                fileSessionId = File.ReadAllText("currentChatSessionId.txt").Trim();
            if (!string.IsNullOrEmpty(fileSessionId))
            {
                // Check backend if session is still active
                var client = new HttpClient();
                string token = File.Exists("authToken.txt") ? File.ReadAllText("authToken.txt").Trim() : null;
                if (!string.IsNullOrEmpty(token))
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                var url = $"{BASE_URL}/api/collections/chat_sessions/records/{fileSessionId}";
                try
                {
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var respJson = await response.Content.ReadAsStringAsync();
                        dynamic respObj = JsonConvert.DeserializeObject(respJson);
                        bool isActive = respObj?.active == true;
                        if (isActive)
                        {
                            // Session is active, ensure UI and memory are in sync
                            chatSessionId = fileSessionId;
                            chatActive = true;
                            SendChatMessageButton.IsEnabled = true;
                            if (chatPollTimer == null || !chatPollTimer.IsEnabled)
                                StartChatPolling();
                            // If file missing, recreate (shouldn't happen here)
                        }
                        else
                        {
                            // Session not active, remove file
                            chatSessionId = null;
                            chatActive = false;
                            SendChatMessageButton.IsEnabled = false;
                            if (File.Exists("currentChatSessionId.txt"))
                                File.Delete("currentChatSessionId.txt");
                        }
                    }
                    else
                    {
                        // Session not found, remove file
                        chatSessionId = null;
                        chatActive = false;
                        SendChatMessageButton.IsEnabled = false;
                        if (File.Exists("currentChatSessionId.txt"))
                            File.Delete("currentChatSessionId.txt");
                    }
                }
                catch
                {
                    // On error, remove file
                    chatSessionId = null;
                    chatActive = false;
                    SendChatMessageButton.IsEnabled = false;
                    if (File.Exists("currentChatSessionId.txt"))
                        File.Delete("currentChatSessionId.txt");
                }
            }
            else if (!string.IsNullOrEmpty(chatSessionId))
            {
                // If chatSessionId is set but file is missing, create the file
                File.WriteAllText("currentChatSessionId.txt", chatSessionId);
            }
        }

        // Call EndChatSession when session ends or user logs out
        private async void EndSession_Click(object sender, RoutedEventArgs e)
        {
            await EndChatSession();
            await EndSession();
        }

        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            await EndChatSession();
            sessionActive = false;
            sessionTimer.Stop();
            var login = new Login();
            login.Show();
            this.Close();
        }

        private void ExtendHour_Click(object sender, RoutedEventArgs e)
        {
            ExtendSession(TimeSpan.FromHours(1));
        }

        private async void ChangeDevice_Click(object sender, RoutedEventArgs e)
        {
            await EndChatSession();
            await EndSession();
            try { if (System.IO.File.Exists("sessionInfo.json")) System.IO.File.Delete("sessionInfo.json"); } catch { }
            try { if (System.IO.File.Exists("loginInfo.json")) System.IO.File.Delete("loginInfo.json"); } catch { }
            try { if (System.IO.File.Exists("currentSessionId.txt")) System.IO.File.Delete("currentSessionId.txt"); } catch { }
            try { if (System.IO.File.Exists("selectedDevice.json")) System.IO.File.Delete("selectedDevice.json"); } catch { }
            var selector = new DeviceSelector();
            selector.Show();
            this.Close();
        }
    }
}
