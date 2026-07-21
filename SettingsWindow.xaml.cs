using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace LocalAIChatWPF
{
    public partial class SettingsWindow : Window
    {
        public string ServerUrl { get; private set; }
        public string AiName { get; private set; }
        public string SystemPrompt { get; private set; }

        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private string _serverUrl;

        public SettingsWindow(string serverUrl, string aiName, string systemPrompt)
        {
            InitializeComponent();
            var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            var dark = 1;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));

            _serverUrl = serverUrl;
            ServerUrl = serverUrl;
            AiName = aiName;
            SystemPrompt = systemPrompt;
            ServerUrlBox.Text = serverUrl;
            AiNameBox.Text = aiName;
            SystemPromptBox.Text = systemPrompt;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            var url = ServerUrlBox.Text.Trim().TrimEnd('/');
            try
            {
                var resp = await _http.GetStringAsync($"{url}/api/models");
                var doc = JsonDocument.Parse(resp);
                var count = doc.RootElement.GetProperty("models").GetArrayLength();
                TestResultLabel.Text = $"Connected. Found {count} model(s).";
            }
            catch (Exception ex)
            {
                TestResultLabel.Text = $"Failed: {ex.Message}";
            }
        }

        private async void MemoryTab_GotFocus(object sender, RoutedEventArgs e)
        {
            await RefreshMemory();
        }

        private async System.Threading.Tasks.Task RefreshMemory()
        {
            MemoryListBox.Items.Clear();
            try
            {
                var resp = await _http.GetStringAsync($"{_serverUrl}/api/memory");
                var doc = JsonDocument.Parse(resp);
                foreach (var fact in doc.RootElement.GetProperty("facts").EnumerateArray())
                {
                    var item = new MemoryItem(
                        fact.GetProperty("id").GetInt32(),
                        fact.GetProperty("fact").GetString() ?? ""
                    );
                    MemoryListBox.Items.Add(item);
                }
            }
            catch
            {
                MemoryListBox.Items.Add(new MemoryItem(-1, "[Could not load memory]"));
            }
        }

        private async void DeleteFact_Click(object sender, RoutedEventArgs e)
        {
            if (MemoryListBox.SelectedItem is not MemoryItem item || item.Id < 0) return;
            try
            {
                await _http.DeleteAsync($"{_serverUrl}/api/memory/{item.Id}");
                await RefreshMemory();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not delete fact:\n{ex.Message}", "Error");
            }
        }

        private async void AddFact_Click(object sender, RoutedEventArgs e)
        {
            var fact = NewFactBox.Text.Trim();
            if (string.IsNullOrEmpty(fact)) return;
            try
            {
                var body = JsonSerializer.Serialize(new { fact });
                await _http.PostAsync($"{_serverUrl}/api/memory",
                    new StringContent(body, Encoding.UTF8, "application/json"));
                NewFactBox.Clear();
                await RefreshMemory();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not add fact:\n{ex.Message}", "Error");
            }
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            ServerUrl = ServerUrlBox.Text.Trim().TrimEnd('/');
            AiName = string.IsNullOrWhiteSpace(AiNameBox.Text) ? "AI" : AiNameBox.Text.Trim();
            SystemPrompt = SystemPromptBox.Text.Trim();
            DialogResult = true;
            Close();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class MemoryItem
    {
        public int Id { get; }
        public string Fact { get; }
        public MemoryItem(int id, string fact) { Id = id; Fact = fact; }
        public override string ToString() => Fact;
    }
}