using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace my_cyber_cafe
{
    public partial class DeviceSelector : Window
    {
        private const string DevicesApiUrl = "http://ec2-3-110-191-79.ap-south-1.compute.amazonaws.com/api/collections/devices/records";
        private DeviceItem _selectedDevice;
        private Border _selectedBorder;
        private List<DeviceItem> _devices;

        public DeviceSelector()
        {
            InitializeComponent();
            Loaded += DeviceSelector_Loaded;
            RefreshBtn.Click += RefreshBtn_Click;
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await LoadDevicesAsync();
        }

        private async void DeviceSelector_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDevicesAsync();
        }

        private async Task LoadDevicesAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var response = await client.GetStringAsync(DevicesApiUrl);
                    var doc = JsonDocument.Parse(response);
                    _devices = new List<DeviceItem>();
                    foreach (var record in doc.RootElement.GetProperty("items").EnumerateArray())
                    {
                        _devices.Add(new DeviceItem
                        {
                            Id = record.GetProperty("id").GetString(),
                            Name = record.GetProperty("name").GetString()
                        });
                    }
                    DeviceCards.ItemsSource = _devices;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to fetch device list: {ex.Message}");
                Close();
            }
        }

        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is StackPanel panel && panel.DataContext is DeviceItem device)
            {
                // Find the Border parent
                var border = FindParent<Border>(panel);
                if (border == null) return;

                // Remove highlight from previous
                if (_selectedBorder != null)
                    _selectedBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 51, 51));

                // Highlight current
                border.BorderBrush = Brushes.DeepSkyBlue;
                _selectedBorder = border;
                _selectedDevice = device;

                // Save selection and open Login window
                File.WriteAllText("selectedDevice.json", JsonSerializer.Serialize(_selectedDevice));
                var login = new Login();
                login.Show();
                this.Close();
            }
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
                parent = VisualTreeHelper.GetParent(parent);
            return parent as T;
        }

        private class DeviceItem
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }
    }
} 