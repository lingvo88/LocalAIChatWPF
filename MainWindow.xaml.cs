using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MdXaml;

namespace LocalAIChatWPF
{
    public partial class MainWindow : Window
    {
        // ---- config ----
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LocalAIChat");
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "client_config.json");
        private const string DefaultServerUrl = "http://localhost:5000";
        private const string DefaultAiName = "AI";

        private string _serverUrl = DefaultServerUrl;
        private string _aiName = DefaultAiName;
        private double _fontSize = 14;

        // ---- state ----
        private int? _conversationId;
        private readonly List<ChatMessage> _conversation = new();
        private string? _attachedFilePath;
        private string? _attachedFileText;
        private bool _isStreaming;
        private int _lastMessageCount;
        private CancellationTokenSource? _streamCts;

        // ---- networking ----
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(130) };
        private readonly MarkdownScrollViewer _mdViewer = new();

        // ---- poll timer ----
        private readonly DispatcherTimer _pollTimer = new();

        public MainWindow()
        {
            InitializeComponent();
            var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            var dark = 1;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));

            Timeline.DesiredFrameRateProperty.OverrideMetadata(
        typeof(Timeline),
        new FrameworkPropertyMetadata { DefaultValue = 30 }
    );

            Directory.CreateDirectory(ConfigDir);
            LoadConfig();

            Directory.CreateDirectory(ConfigDir);
            LoadConfig();
            AllowDrop = true;
            Drop += Window_Drop;
            DragOver += Window_DragOver;
            _pollTimer.Interval = TimeSpan.FromSeconds(3);
            _pollTimer.Tick += async (s, e) => await PollForUpdates();
            _pollTimer.Start();
            Loaded += async (s, e) =>
            {
                await LoadModels();
                await ResumeLatestConversation();
            };
        }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // ================================================================
        // Config
        // ================================================================
        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var doc = JsonDocument.Parse(json);
                    _serverUrl = doc.RootElement.TryGetProperty("server_url", out var u) ? u.GetString() ?? DefaultServerUrl : DefaultServerUrl;
                    _aiName = doc.RootElement.TryGetProperty("ai_name", out var n) ? n.GetString() ?? DefaultAiName : DefaultAiName;
                }
            }
            catch { }
        }

        private void SaveConfig()
        {
            var json = JsonSerializer.Serialize(new { server_url = _serverUrl, ai_name = _aiName });
            File.WriteAllText(ConfigPath, json);
        }

        // ================================================================
        // Models
        // ================================================================
        private async Task LoadModels()
        {
            try
            {
                var resp = await _http.GetStringAsync($"{_serverUrl}/api/models");
                var doc = JsonDocument.Parse(resp);
                var models = doc.RootElement.GetProperty("models").EnumerateArray()
                    .Select(m => m.GetString()).ToList();
                ModelComboBox.Items.Clear();
                foreach (var m in models)
                    ModelComboBox.Items.Add(m);
                if (ModelComboBox.Items.Count > 0)
                    ModelComboBox.SelectedIndex = 0;
            }
            catch
            {
                ModelComboBox.Items.Clear();
                ModelComboBox.Items.Add("Server not reachable");
                ModelComboBox.SelectedIndex = 0;
            }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await LoadModels();

        // ================================================================
        // Chat rendering
        // ================================================================
        private void AddUserBubble(string text)
        {
            var nameBlock = new TextBlock
            {
                Text = "You",
                Foreground = new SolidColorBrush(Color.FromRgb(74, 144, 226)),
                FontWeight = FontWeights.SemiBold,
                FontSize = _fontSize,
                Margin = new Thickness(0, 10, 0, 2)
            };
            var textBox = new TextBox
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 234)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = _fontSize,
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                Cursor = Cursors.IBeam,
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(0)
            };
            ChatPanel.Children.Add(nameBlock);
            ChatPanel.Children.Add(textBox);
            ScrollToBottom();
        }

        private TextBox AddAiBubble()
        {
            var nameBlock = new TextBlock
            {
                Text = _aiName,
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                FontWeight = FontWeights.SemiBold,
                FontSize = _fontSize,
                Margin = new Thickness(0, 10, 0, 2)
            };
            var textBox = new TextBox
            {
                Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 234)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = _fontSize,
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                Cursor = Cursors.IBeam,
                Margin = new Thickness(0, 2, 0, 4),
                Padding = new Thickness(0)
            };
            ChatPanel.Children.Add(nameBlock);
            ChatPanel.Children.Add(textBox);
            ScrollToBottom();
            return textBox;
        }

        private void AddThinkingIndicator()
        {
            var box = new TextBox
            {
                Text = "Thinking...",
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontStyle = FontStyles.Italic,
                FontSize = _fontSize,
                IsReadOnly = true,
                Tag = "thinking",
                Margin = new Thickness(0, 2, 0, 4),
                Padding = new Thickness(0)
            };
            ChatPanel.Children.Add(box);
            ScrollToBottom();
        }

        private void RemoveThinkingIndicator()
        {
            var thinking = ChatPanel.Children.OfType<TextBox>()
                .FirstOrDefault(b => b.Tag as string == "thinking");
            if (thinking != null)
                ChatPanel.Children.Remove(thinking);
        }

        private void AddNoteBlock(string text)
        {
            ChatPanel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)),
                FontStyle = FontStyles.Italic,
                FontSize = _fontSize - 1,
                Margin = new Thickness(0, 4, 0, 4)
            });
        }

        private void ScrollToBottom()
        {
            ChatScrollViewer.ScrollToBottom();
        }

        private void ClearChat()
        {
            ChatPanel.Children.Clear();
        }

        // ================================================================
        // Conversations
        // ================================================================
        private async Task ResumeLatestConversation()
        {
            try
            {
                var resp = await _http.GetStringAsync($"{_serverUrl}/api/conversations/latest");
                var doc = JsonDocument.Parse(resp);
                _conversationId = doc.RootElement.GetProperty("conversation_id").GetInt32();
                var messages = doc.RootElement.GetProperty("messages").EnumerateArray().ToList();
                _conversation.Clear();
                ClearChat();
                foreach (var msg in messages)
                {
                    var role = msg.GetProperty("role").GetString();
                    var content = msg.GetProperty("content").GetString() ?? "";
                    _conversation.Add(new ChatMessage(role!, content));
                    if (role == "user") AddUserBubble(content);
                    else if (role == "assistant") { AddAiBubble().Text = content; }
                }
                _lastMessageCount = _conversation.Count;
                if (_conversation.Count > 0)
                    AddNoteBlock("[Resumed previous conversation]");
            }
            catch
            {
                AddNoteBlock("[Could not reach the server. Check Settings → Server Address.]");
            }
        }

        private async Task StartNewConversation()
        {
            try
            {
                var body = JsonSerializer.Serialize(new { title = "New Chat" });
                var resp = await _http.PostAsync($"{_serverUrl}/api/conversations/new",
                    new StringContent(body, Encoding.UTF8, "application/json"));
                var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                _conversationId = doc.RootElement.GetProperty("conversation_id").GetInt32();
                _conversation.Clear();
                _lastMessageCount = 0;
                ClearChat();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not start a new chat:\n{ex.Message}", "Server error");
            }
        }

        private async Task PollForUpdates()
        {
            if (_isStreaming || _conversationId == null) return;
            try
            {
                var resp = await _http.GetStringAsync($"{_serverUrl}/api/conversations/{_conversationId}");
                var doc = JsonDocument.Parse(resp);
                var messages = doc.RootElement.GetProperty("messages").EnumerateArray().ToList();
                if (messages.Count == _lastMessageCount) return;
                if (!messages.Any() || messages.Last().GetProperty("role").GetString() != "assistant") return;

                var lastFetched = messages.Last().GetProperty("content").GetString() ?? "";
                var lastDisplayed = _conversation.LastOrDefault();
                if (lastDisplayed?.Role == "assistant" && lastDisplayed.Content == lastFetched) return;

                StatusLabel.Text = "Syncing...";
                _lastMessageCount = messages.Count;
                _conversation.Clear();
                ClearChat();
                foreach (var msg in messages)
                {
                    var role = msg.GetProperty("role").GetString()!;
                    var content = msg.GetProperty("content").GetString() ?? "";
                    _conversation.Add(new ChatMessage(role, content));
                    if (role == "user") AddUserBubble(content);
                    else AddAiBubble().Text = content;
                }
                ScrollToBottom();
                StatusLabel.Text = "";
            }
            catch { }
        }

        // ================================================================
        // Send message + streaming
        // ================================================================
        private async void SendBtn_Click(object sender, RoutedEventArgs e) => await SendMessage();
        private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
                await SendMessage();
            }
        }

        private async Task SendMessage()
        {
            if (_isStreaming || _conversationId == null) return;
            var text = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            var model = ModelComboBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(model) || model.Contains("not reachable") || model.Contains("No models"))
            {
                MessageBox.Show("No valid model selected. Click Refresh.", "No model");
                return;
            }

            var outgoingText = text;
            if (_attachedFileText != null)
            {
                outgoingText = $"[Attached file: {_attachedFilePath}]\n{_attachedFileText}\n\n[User message]\n{text}";
                _attachedFileText = null;
                _attachedFilePath = null;
                FileLabel.Text = "No file attached — drag a file onto this window, or use Attach File";
                FileLabel.Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102));
            }

            _conversation.Add(new ChatMessage("user", text));
            AddUserBubble(text);
            InputBox.Clear();

            var responseBlock = AddAiBubble();
            AddThinkingIndicator();

            _isStreaming = true;
            StatusLabel.Text = "Thinking...";
            SendBtn.IsEnabled = false;
            StopBtn.IsEnabled = true;

            _streamCts = new CancellationTokenSource();
            var fullResponse = new StringBuilder();

            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    conversation_id = _conversationId,
                    message = outgoingText,
                    model,
                    search = SearchToggle.IsChecked == true
                });

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_serverUrl}/api/chat")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };

                using var resp = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _streamCts.Token);
                using var stream = await resp.Content.ReadAsStreamAsync(_streamCts.Token);
                using var reader = new StreamReader(stream);

                var buffer = new char[64];
                RemoveThinkingIndicator();

                while (!reader.EndOfStream && !_streamCts.Token.IsCancellationRequested)
                {
                    var count = await reader.ReadAsync(buffer, 0, buffer.Length);
                    if (count > 0)
                    {
                        var chunk = new string(buffer, 0, count);
                        fullResponse.Append(chunk);
                        responseBlock.Text = fullResponse.ToString();
                        ScrollToBottom();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                RemoveThinkingIndicator();
                // Stop was pressed — keep what we have
            }
            catch (Exception ex)
            {
                RemoveThinkingIndicator();
                responseBlock.Text = $"[Error: {ex.Message}]";
            }
            finally
            {
                if (fullResponse.Length > 0)
                {
                    _conversation.Add(new ChatMessage("assistant", fullResponse.ToString()));
                    _lastMessageCount = _conversation.Count;
                }
                _isStreaming = false;
                StatusLabel.Text = "";
                SendBtn.IsEnabled = true;
                StopBtn.IsEnabled = false;
                InputBox.Focus();
            }
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            _streamCts?.Cancel();
        }

        // ================================================================
        // New chat
        // ================================================================
        private async void NewChatBtn_Click(object sender, RoutedEventArgs e)
        {
            AddNoteBlock("[Saving memories before starting fresh...]");
            await ExtractMemory(silent: true);
            await StartNewConversation();
            FileLabel.Text = "No file attached — drag a file onto this window, or use Attach File";
            FileLabel.Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102));
        }

        // ================================================================
        // File attach + drag drop
        // ================================================================
        private void AttachBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text/Code files (*.txt;*.py;*.md;*.csv;*.json;*.log)|*.txt;*.py;*.md;*.csv;*.json;*.log|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                LoadFile(dlg.FileName);
        }

        private void InputBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0) LoadFile(files[0]);
            }
        }

        private void InputBox_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0) LoadFile(files[0]);
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void LoadFile(string path)
        {
            try
            {
                _attachedFileText = File.ReadAllText(path, Encoding.UTF8);
                _attachedFilePath = Path.GetFileName(path);
                FileLabel.Text = $"Attached: {_attachedFilePath} ({_attachedFileText.Length} chars) — will be included in next message";
                FileLabel.Foreground = new SolidColorBrush(Color.FromRgb(46, 180, 100));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read file:\n{ex.Message}", "Error");
            }
        }

        // ================================================================
        // Export
        // ================================================================
        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_conversation.Any())
            {
                MessageBox.Show("No conversation yet.", "Nothing to save");
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "chat.txt", Filter = "Text files (*.txt)|*.txt" };
            if (dlg.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                foreach (var msg in _conversation)
                    sb.AppendLine($"{(msg.Role == "user" ? "You" : _aiName)}: {msg.Content}\n");
                File.WriteAllText(dlg.FileName, sb.ToString());
                MessageBox.Show($"Exported to {dlg.FileName}", "Exported");
            }
        }

        // ================================================================
        // Load chat dialog
        // ================================================================
        private async void LoadChatBtn_Click(object sender, RoutedEventArgs e)
        {
            List<ConversationInfo> convos;
            try
            {
                var resp = await _http.GetStringAsync($"{_serverUrl}/api/conversations");
                var doc = JsonDocument.Parse(resp);
                convos = doc.RootElement.EnumerateArray().Select(c => new ConversationInfo(
                    c.GetProperty("id").GetInt32(),
                    c.GetProperty("title").GetString() ?? "",
                    c.GetProperty("updated_at").GetString() ?? ""
                )).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not fetch conversations:\n{ex.Message}", "Error");
                return;
            }

            var dialog = new ConversationDialog(convos, _serverUrl);
            if (dialog.ShowDialog() == true && dialog.SelectedId.HasValue)
            {
                await LoadConversation(dialog.SelectedId.Value);
            }
        }

        private async Task LoadConversation(int id)
        {
            try
            {
                var resp = await _http.GetStringAsync($"{_serverUrl}/api/conversations/{id}");
                var doc = JsonDocument.Parse(resp);
                _conversationId = id;
                _conversation.Clear();
                ClearChat();
                foreach (var msg in doc.RootElement.GetProperty("messages").EnumerateArray())
                {
                    var role = msg.GetProperty("role").GetString()!;
                    var content = msg.GetProperty("content").GetString() ?? "";
                    _conversation.Add(new ChatMessage(role, content));
                    if (role == "user") AddUserBubble(content);
                    else AddAiBubble().Text = content;
                }
                _lastMessageCount = _conversation.Count;
                AddNoteBlock("[Loaded conversation]");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load conversation:\n{ex.Message}", "Error");
            }
        }

        // ================================================================
        // Memory
        // ================================================================
        private async Task ExtractMemory(bool silent = false)
        {
            if (_conversationId == null || !_conversation.Any()) return;
            var model = ModelComboBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(model) || model.Contains("not reachable")) return;
            try
            {
                var body = JsonSerializer.Serialize(new { conversation_id = _conversationId, model });
                await _http.PostAsync($"{_serverUrl}/api/memory/extract",
                    new StringContent(body, Encoding.UTF8, "application/json"));
            }
            catch { }
        }

        // ================================================================
        // Font size
        // ================================================================
        private void FontSmaller_Click(object sender, RoutedEventArgs e)
        {
            if (_fontSize > 10) { _fontSize--; UpdateFontSize(); }
        }

        private void FontLarger_Click(object sender, RoutedEventArgs e)
        {
            if (_fontSize < 28) { _fontSize++; UpdateFontSize(); }
        }

        private void UpdateFontSize()
        {
            foreach (var child in ChatPanel.Children.OfType<TextBlock>())
                child.FontSize = _fontSize;
            foreach (var child in ChatPanel.Children.OfType<TextBox>())
                if (child != InputBox)
                    child.FontSize = _fontSize;
            InputBox.FontSize = _fontSize;
        }
        // ================================================================
        // Settings
        // ================================================================
        private async void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            string currentPrompt = "";
            try
            {
                var resp = await _http.GetStringAsync($"{_serverUrl}/api/settings/prompt");
                currentPrompt = JsonDocument.Parse(resp).RootElement.GetProperty("prompt").GetString() ?? "";
            }
            catch { }

            var settingsWindow = new SettingsWindow(_serverUrl, _aiName, currentPrompt);
            if (settingsWindow.ShowDialog() == true)
            {
                _serverUrl = settingsWindow.ServerUrl;
                _aiName = settingsWindow.AiName;
                SaveConfig();

                if (!string.IsNullOrEmpty(settingsWindow.SystemPrompt))
                {
                    try
                    {
                        var body = JsonSerializer.Serialize(new { prompt = settingsWindow.SystemPrompt });
                        await _http.PostAsync($"{_serverUrl}/api/settings/prompt",
                            new StringContent(body, Encoding.UTF8, "application/json"));
                    }
                    catch { }
                }

                await LoadModels();
                await ExtractMemory(silent: true);
                await StartNewConversation();
            }
        }

        // ================================================================
        // Window close — extract memory first
        // ================================================================
        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            await ExtractMemory(silent: true);
            Application.Current.Shutdown();
        }
    }

    // ================================================================
    // Simple data models
    // ================================================================
    public record ChatMessage(string Role, string Content);
    public record ConversationInfo(int Id, string Title, string UpdatedAt);
}