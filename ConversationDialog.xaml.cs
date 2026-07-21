using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LocalAIChatWPF
{
    public partial class ConversationDialog : Window
    {
        public int? SelectedId { get; private set; }

        private readonly List<ConversationInfo> _convos;
        private readonly string _serverUrl;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public ConversationDialog(List<ConversationInfo> convos, string serverUrl)
        {
            InitializeComponent();
            var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            var dark = 1;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));

            _convos = convos;
            _serverUrl = serverUrl;
            Populate();
        }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private void Populate()
        {
            ConvoListBox.Items.Clear();
            foreach (var c in _convos)
            {
                var display = $"{c.Title}   —   {c.UpdatedAt.Replace("T", " ")[..Math.Min(16, c.UpdatedAt.Length)]}";
                var item = new ListBoxItem { Content = display, Tag = c };
                ConvoListBox.Items.Add(item);
            }
        }

        private ConversationInfo? GetSelected()
        {
            return (ConvoListBox.SelectedItem as ListBoxItem)?.Tag as ConversationInfo;
        }

        private void ConvoListBox_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var conv = GetSelected();
            if (conv == null) return;
            SelectedId = conv.Id;
            DialogResult = true;
            Close();
        }

        private void OpenBtn_Click(object sender, RoutedEventArgs e)
        {
            var conv = GetSelected();
            if (conv == null) { MessageBox.Show("Select a conversation first.", "No selection"); return; }
            SelectedId = conv.Id;
            DialogResult = true;
            Close();
        }

        private async void RenameBtn_Click(object sender, RoutedEventArgs e)
        {
            var conv = GetSelected();
            if (conv == null) { MessageBox.Show("Select a conversation first.", "No selection"); return; }
            var newTitle = RenameBox.Text.Trim();
            if (string.IsNullOrEmpty(newTitle)) { MessageBox.Show("Type a new name first.", "No name"); return; }
            try
            {
                var body = JsonSerializer.Serialize(new { title = newTitle });
                await _http.PostAsync($"{_serverUrl}/api/conversations/{conv.Id}/rename",
                    new StringContent(body, Encoding.UTF8, "application/json"));
                RenameBox.Clear();
                // Refresh list from server
                var resp = await _http.GetStringAsync($"{_serverUrl}/api/conversations");
                var doc = JsonDocument.Parse(resp);
                _convos.Clear();
                foreach (var c in doc.RootElement.EnumerateArray())
                    _convos.Add(new ConversationInfo(
                        c.GetProperty("id").GetInt32(),
                        c.GetProperty("title").GetString() ?? "",
                        c.GetProperty("updated_at").GetString() ?? ""
                    ));
                Populate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not rename:\n{ex.Message}", "Error");
            }
        }

        private async void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            var conv = GetSelected();
            if (conv == null) { MessageBox.Show("Select a conversation first.", "No selection"); return; }
            var result = MessageBox.Show(
                $"Delete \"{conv.Title}\"?\n\nMemory facts are not affected.",
                "Delete conversation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            try
            {
                await _http.DeleteAsync($"{_serverUrl}/api/conversations/{conv.Id}");
                var resp = await _http.GetStringAsync($"{_serverUrl}/api/conversations");
                var doc = JsonDocument.Parse(resp);
                _convos.Clear();
                foreach (var c in doc.RootElement.EnumerateArray())
                    _convos.Add(new ConversationInfo(
                        c.GetProperty("id").GetInt32(),
                        c.GetProperty("title").GetString() ?? "",
                        c.GetProperty("updated_at").GetString() ?? ""
                    ));
                Populate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not delete:\n{ex.Message}", "Error");
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}