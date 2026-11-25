using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AsistenFB;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class SavedPost
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public bool Selected { get; set; }
    public string Draft { get; set; } = "";
}

public class MainForm : Form
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly WebView2 web = new();
    private readonly Label browserStatus = new();
    private readonly TextBox address = new();
    private readonly Label countStatus = new();
    private readonly Label infoStatus = new();
    private readonly FlowLayoutPanel postList = new();
    private readonly Button captureButton = new();
    private readonly Button clearButton = new();
    private readonly Button deleteSelectedButton = new();
    private readonly TextBox apiKeyBox = new();
    private readonly TextBox modelBox = new();
    private readonly ComboBox delayBox = new();
    private readonly Button saveKeyButton = new();
    private readonly Button runSelectedButton = new();
    private readonly Button pauseButton = new();
    private readonly Button stopButton = new();
    private readonly ListBox activityLog = new();
    private readonly List<SavedPost> posts = new();
    private readonly string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AsistenFB");
    private readonly string profile;
    private readonly string dataFile;
    private readonly string keyFile;
    private CancellationTokenSource? queueCts;
    private bool queuePaused;
    private bool queueRunning;

    public MainForm()
    {
        profile = Path.Combine(appFolder, "WebView2Profile");
        dataFile = Path.Combine(appFolder, "reading-list.json");
        keyFile = Path.Combine(appFolder, "openai-key.bin");
        Text = "Asisten FB - Daftar Bacaan + AI";
        Width = 1580;
        Height = 960;
        MinimumSize = new Size(1100, 700);
        BackColor = Color.FromArgb(8, 11, 11);
        LoadPosts();
        BuildHeaderAndNavigation();

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6, BackColor = Color.FromArgb(8, 11, 11) };
        bool splitConfigured = false;
        web.Dock = DockStyle.Fill;
        split.Panel1.Controls.Add(web);
        BuildReadingPanel(split.Panel2);

        browserStatus.Dock = DockStyle.Bottom;
        browserStatus.Height = 24;
        browserStatus.BackColor = Color.FromArgb(17, 25, 25);
        browserStatus.ForeColor = Color.FromArgb(217, 255, 0);
        browserStatus.TextAlign = ContentAlignment.MiddleLeft;
        browserStatus.Padding = new Padding(8, 0, 0, 0);

        Controls.Add(split);
        Controls.Add(browserStatus);
        Shown += async (_, _) =>
        {
            int availableWidth = split.ClientSize.Width - split.SplitterWidth;
            if (availableWidth >= 480 + 420)
            {
                split.Panel1MinSize = 480;
                split.Panel2MinSize = 420;
                split.SplitterDistance = Math.Min(820, availableWidth - split.Panel2MinSize);
                splitConfigured = true;
            }
            LoadApiKey();
            await StartBrowser();
            RefreshPostCards();
        };
        Resize += (_, _) =>
        {
            if (!splitConfigured) return;
            int maxDistance = split.ClientSize.Width - split.SplitterWidth - split.Panel2MinSize;
            if (maxDistance >= split.Panel1MinSize && split.SplitterDistance > maxDistance)
                split.SplitterDistance = maxDistance;
        };
        FormClosing += (_, _) => queueCts?.Cancel();
    }

    private void BuildHeaderAndNavigation()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Color.FromArgb(8, 11, 11) };
        header.Controls.Add(new Label { Text = "ASISTEN FB", ForeColor = Color.FromArgb(217, 255, 0), Font = new Font("Segoe UI", 19, FontStyle.Bold), AutoSize = true, Location = new Point(16, 3) });
        header.Controls.Add(new Label { Text = "Pilih postingan • buat komentar AI • periksa draf sebelum dikirim", ForeColor = Color.Gainsboro, AutoSize = true, Location = new Point(19, 38) });
        var nav = new Panel { Dock = DockStyle.Top, Height = 43, BackColor = Color.FromArgb(25, 35, 35) };
        Button NavButton(string label, int x, int width, Action action)
        {
            var b = new Button { Text = label, Location = new Point(x, 6), Size = new Size(width, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(217, 255, 0), ForeColor = Color.Black };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) => action();
            return b;
        }
        nav.Controls.Add(NavButton("Kembali", 8, 76, () => { if (web.CanGoBack) web.GoBack(); }));
        nav.Controls.Add(NavButton("Maju", 90, 64, () => { if (web.CanGoForward) web.GoForward(); }));
        nav.Controls.Add(NavButton("Muat ulang", 160, 90, () => web.Reload()));
        nav.Controls.Add(NavButton("Beranda FB", 256, 94, () => web.Source = new Uri("https://www.facebook.com/")));
        address.SetBounds(358, 8, 560, 26);
        address.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        address.Text = "https://www.facebook.com/";
        nav.Controls.Add(address);
        var go = NavButton("Buka", 926, 66, Navigate);
        go.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        nav.Controls.Add(go);
        Controls.Add(nav);
        Controls.Add(header);
    }

    private void BuildReadingPanel(Control host)
    {
        host.BackColor = Color.FromArgb(12, 17, 17);
        var title = new Label { Text = "DAFTAR BACAAN & KOMENTAR AI", Dock = DockStyle.Top, Height = 40, ForeColor = Color.FromArgb(217, 255, 0), Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 190, BackColor = Color.FromArgb(18, 27, 27) };
        var keyLabel = new Label { Text = "OpenAI API Key:", ForeColor = Color.Gainsboro, Location = new Point(10, 12), AutoSize = true };
        toolbar.Controls.Add(keyLabel);
        apiKeyBox.SetBounds(112, 8, 250, 27);
        apiKeyBox.UseSystemPasswordChar = true;
        apiKeyBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        apiKeyBox.Width = 250;
        apiKeyBox.PlaceholderText = "Tempel API Key di sini";
        toolbar.Controls.Add(apiKeyBox);

        saveKeyButton.Text = "SIMPAN KEY";
        saveKeyButton.SetBounds(370, 7, 105, 29);
        saveKeyButton.BackColor = Color.FromArgb(217, 255, 0);
        saveKeyButton.ForeColor = Color.Black;
        saveKeyButton.FlatStyle = FlatStyle.Flat;
        saveKeyButton.FlatAppearance.BorderSize = 0;
        saveKeyButton.Click += (_, _) => SaveApiKey();
        toolbar.Controls.Add(saveKeyButton);

        var modelLabel = new Label { Text = "Model:", ForeColor = Color.Gainsboro, Location = new Point(485, 12), AutoSize = true };
        toolbar.Controls.Add(modelLabel);
        modelBox.SetBounds(535, 8, 100, 27);
        modelBox.Text = "gpt-5.5";
        modelBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        toolbar.Controls.Add(modelBox);

        var keyHint = new Label { Text = "Key disimpan terenkripsi di komputer ini (Windows DPAPI), bukan di GitHub.", ForeColor = Color.Silver, Location = new Point(12, 40), Size = new Size(620, 20), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        toolbar.Controls.Add(keyHint);

        countStatus.SetBounds(12, 65, 350, 20);
        countStatus.ForeColor = Color.Gainsboro;
        countStatus.Font = new Font("Segoe UI", 8.5f);
        toolbar.Controls.Add(countStatus);

        captureButton.Text = "＋ TAMBAHKAN TEKS TERPILIH DARI FACEBOOK";
        captureButton.SetBounds(10, 89, 350, 32);
        captureButton.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        captureButton.BackColor = Color.FromArgb(217, 255, 0);
        captureButton.ForeColor = Color.Black;
        captureButton.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        captureButton.FlatStyle = FlatStyle.Flat;
        captureButton.FlatAppearance.BorderSize = 0;
        captureButton.Click += async (_, _) => await CaptureSelection();
        toolbar.Controls.Add(captureButton);

        var delayLabel = new Label { Text = "Jeda:", ForeColor = Color.Gainsboro, Location = new Point(12, 134), AutoSize = true };
        toolbar.Controls.Add(delayLabel);
        delayBox.SetBounds(55, 130, 75, 27);
        delayBox.DropDownStyle = ComboBoxStyle.DropDownList;
        delayBox.Items.AddRange(new object[] { "2 detik", "5 detik", "8 detik", "10 detik" });
        delayBox.SelectedIndex = 1;
        toolbar.Controls.Add(delayBox);

        runSelectedButton.Text = "JALANKAN SEMUA DRAF";
        runSelectedButton.SetBounds(140, 128, 185, 31);
        runSelectedButton.BackColor = Color.FromArgb(35, 155, 75);
        runSelectedButton.ForeColor = Color.White;
        runSelectedButton.FlatStyle = FlatStyle.Flat;
        runSelectedButton.FlatAppearance.BorderSize = 0;
        runSelectedButton.Click += async (_, _) => await RunSelectedQueue();
        toolbar.Controls.Add(runSelectedButton);

        pauseButton.Text = "PAUSE";
        pauseButton.SetBounds(333, 128, 75, 31);
        pauseButton.BackColor = Color.FromArgb(180, 130, 20);
        pauseButton.ForeColor = Color.White;
        pauseButton.FlatStyle = FlatStyle.Flat;
        pauseButton.FlatAppearance.BorderSize = 0;
        pauseButton.Enabled = false;
        pauseButton.Click += (_, _) => TogglePause();
        toolbar.Controls.Add(pauseButton);

        stopButton.Text = "STOP";
        stopButton.SetBounds(415, 128, 70, 31);
        stopButton.BackColor = Color.FromArgb(150, 40, 40);
        stopButton.ForeColor = Color.White;
        stopButton.FlatStyle = FlatStyle.Flat;
        stopButton.FlatAppearance.BorderSize = 0;
        stopButton.Enabled = false;
        stopButton.Click += (_, _) => queueCts?.Cancel();
        toolbar.Controls.Add(stopButton);

        postList.Dock = DockStyle.Fill;
        postList.FlowDirection = FlowDirection.TopDown;
        postList.WrapContents = false;
        postList.AutoScroll = true;
        postList.Padding = new Padding(8);
        postList.BackColor = Color.FromArgb(12, 17, 17);
        postList.SizeChanged += (_, _) => RefreshPostCards();

        activityLog.Dock = DockStyle.Bottom;
        activityLog.Height = 90;
        activityLog.BackColor = Color.FromArgb(5, 8, 8);
        activityLog.ForeColor = Color.FromArgb(217, 255, 0);
        activityLog.Font = new Font("Consolas", 8.5f);
        var logLabel = new Label { Text = "LOG AKTIVITAS", Dock = DockStyle.Bottom, Height = 20, ForeColor = Color.Silver, BackColor = Color.FromArgb(18, 27, 27), TextAlign = ContentAlignment.MiddleLeft };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 55, BackColor = Color.FromArgb(18, 27, 27) };
        deleteSelectedButton.Text = "HAPUS YANG DICENTANG";
        deleteSelectedButton.SetBounds(10, 8, 190, 32);
        deleteSelectedButton.BackColor = Color.FromArgb(55, 65, 65);
        deleteSelectedButton.ForeColor = Color.White;
        deleteSelectedButton.FlatStyle = FlatStyle.Flat;
        deleteSelectedButton.FlatAppearance.BorderSize = 0;
        deleteSelectedButton.Click += (_, _) => DeleteSelected();
        footer.Controls.Add(deleteSelectedButton);
        clearButton.Text = "KOSONGKAN DAFTAR";
        clearButton.SetBounds(208, 8, 150, 32);
        clearButton.BackColor = Color.FromArgb(90, 35, 35);
        clearButton.ForeColor = Color.White;
        clearButton.FlatStyle = FlatStyle.Flat;
        clearButton.FlatAppearance.BorderSize = 0;
        clearButton.Click += (_, _) => ClearAll();
        footer.Controls.Add(clearButton);
        infoStatus.Text = "Maksimal 50 item • komentar AI hanya disimpan sebagai draf";
        infoStatus.SetBounds(368, 12, 300, 25);
        infoStatus.ForeColor = Color.Silver;
        infoStatus.Font = new Font("Segoe UI", 8);
        footer.Controls.Add(infoStatus);

        host.Controls.Add(postList);
        host.Controls.Add(activityLog);
        host.Controls.Add(logLabel);
        host.Controls.Add(footer);
        host.Controls.Add(toolbar);
        host.Controls.Add(title);
    }

    private void SaveApiKey()
    {
        string key = apiKeyBox.Text.Trim();
        if (key.Length < 20)
        {
            MessageBox.Show("Masukkan API Key yang valid terlebih dahulu.", "API Key belum diisi");
            return;
        }
        try
        {
            Directory.CreateDirectory(appFolder);
            byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(keyFile, encrypted);
            apiKeyBox.Clear();
            AddLog("API Key disimpan secara terenkripsi di komputer ini.");
            infoStatus.Text = "API Key tersimpan aman di komputer ini.";
            MessageBox.Show("API Key berhasil disimpan. Kolom dikosongkan agar kunci tidak terlihat.", "Berhasil");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal menyimpan API Key: " + ex.Message, "Kesalahan");
        }
    }

    private string? ReadApiKey()
    {
        try
        {
            if (!File.Exists(keyFile)) return null;
            byte[] encrypted = File.ReadAllBytes(keyFile);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            MessageBox.Show("API Key tersimpan tidak dapat dibuka di akun Windows ini. Masukkan dan simpan kembali API Key Anda.", "API Key");
            return null;
        }
    }

    private void LoadApiKey()
    {
        if (File.Exists(keyFile)) infoStatus.Text = "API Key terenkripsi sudah tersimpan. Kolom tetap kosong demi keamanan.";
    }

    private async Task<string> GenerateCommentAsync(string postText, CancellationToken token)
    {
        string? key = ReadApiKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("API Key belum disimpan. Tempel API Key lalu tekan SIMPAN KEY.");
        string model = string.IsNullOrWhiteSpace(modelBox.Text) ? "gpt-5.5" : modelBox.Text.Trim();
        var payload = new
        {
            model,
            store = false,
            instructions = "Anda membantu menulis komentar Facebook berbahasa Indonesia yang alami, sopan, relevan, dan terasa manusiawi. Baca isi postingan, pahami konteksnya, lalu buat SATU komentar singkat yang spesifik terhadap isi postingan. Jangan mengarang fakta, jangan mengulang isi postingan, jangan menggunakan hashtag, jangan menyebut bahwa Anda AI, jangan membuat komentar generik seperti 'mantap' saja. Jika konteksnya duka atau masalah serius, tanggapi dengan empati. Keluarkan hanya teks komentar, tanpa tanda kutip atau penjelasan.",
            input = "Buat satu komentar untuk postingan berikut:\n\n" + postText
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, token);
        string body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            string detail = body.Length > 700 ? body[..700] : body;
            throw new InvalidOperationException("OpenAI API menolak permintaan (" + (int)response.StatusCode + "): " + detail);
        }
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
            return outputText.GetString()?.Trim() ?? "";
        var pieces = new List<string>();
        if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in content.EnumerateArray())
                    if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        pieces.Add(text.GetString() ?? "");
            }
        }
        string result = string.Join("\n", pieces).Trim();
        if (result.Length == 0) throw new InvalidOperationException("Respons API diterima, tetapi teks komentar tidak ditemukan.");
        return result;
    }

    private async Task GenerateForPostAsync(SavedPost post, CancellationToken token)
    {
        AddLog("Membuat draf: " + Short(post.Text, 55));
        string draft = await GenerateCommentAsync(post.Text, token);
        post.Draft = draft;
        SavePosts();
        RefreshPostCards();
        AddLog("Draf selesai dibuat.");
    }

    private async Task RunSelectedQueue()
    {
        if (queueRunning) return;
        var selected = posts.Where(p => p.Selected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Centang minimal satu postingan terlebih dahulu.", "Belum ada pilihan");
            return;
        }
        if (string.IsNullOrWhiteSpace(ReadApiKey()))
        {
            MessageBox.Show("Simpan API Key terlebih dahulu melalui kolom di atas.", "API Key diperlukan");
            return;
        }
        queueCts = new CancellationTokenSource();
        queuePaused = false;
        queueRunning = true;
        runSelectedButton.Enabled = false;
        pauseButton.Enabled = true;
        stopButton.Enabled = true;
        pauseButton.Text = "PAUSE";
        int seconds = int.Parse(delayBox.SelectedItem?.ToString()?.Split(' ')[0] ?? "5");
        int done = 0;
        try
        {
            AddLog("Antrean dimulai: " + selected.Count + " postingan.");
            foreach (var post in selected)
            {
                queueCts.Token.ThrowIfCancellationRequested();
                while (queuePaused)
                {
                    queueCts.Token.ThrowIfCancellationRequested();
                    await Task.Delay(250, queueCts.Token);
                }
                try
                {
                    await GenerateForPostAsync(post, queueCts.Token);
                    done++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AddLog("GAGAL: " + Short(ex.Message, 100));
                    infoStatus.Text = "Ada item gagal. Periksa log aktivitas.";
                }
                if (done + selected.Count(p => p.Draft.Length > 0) < selected.Count)
                    await Task.Delay(TimeSpan.FromSeconds(seconds), queueCts.Token);
            }
            AddLog("Antrean selesai. Draf dibuat: " + done + " dari " + selected.Count + ".");
        }
        catch (OperationCanceledException)
        {
            AddLog("Antrean dihentikan pengguna.");
        }
        finally
        {
            queueRunning = false;
            queuePaused = false;
            queueCts?.Dispose();
            queueCts = null;
            runSelectedButton.Enabled = true;
            pauseButton.Enabled = false;
            pauseButton.Text = "PAUSE";
            stopButton.Enabled = false;
        }
    }

    private void TogglePause()
    {
        if (!queueRunning) return;
        queuePaused = !queuePaused;
        pauseButton.Text = queuePaused ? "LANJUT" : "PAUSE";
        AddLog(queuePaused ? "Antrean dijeda." : "Antrean dilanjutkan.");
    }

    private void AddLog(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => AddLog(message))); return; }
        activityLog.Items.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + message);
        while (activityLog.Items.Count > 100) activityLog.Items.RemoveAt(activityLog.Items.Count - 1);
    }

    private static string Short(string value, int max) => value.Length <= max ? value : value[..max] + "…";

    private async Task CaptureSelection()
    {
        if (web.CoreWebView2 == null)
        {
            MessageBox.Show("Halaman Facebook belum siap.", "Asisten FB");
            return;
        }
        try
        {
            string json = await web.CoreWebView2.ExecuteScriptAsync("window.getSelection ? window.getSelection().toString() : '';");
            string selectedText = (JsonSerializer.Deserialize<string>(json) ?? "").Trim();
            if (selectedText.Length < 8)
            {
                MessageBox.Show("Blok atau sorot dahulu teks postingan di Facebook sebelah kiri, kemudian klik tombol hijau.", "Belum ada teks yang dipilih");
                return;
            }
            if (selectedText.Length > 5000) selectedText = selectedText[..5000];
            string source = web.Source?.ToString() ?? "https://www.facebook.com/";
            if (posts.Any(p => string.Equals(Normalize(p.Text), Normalize(selectedText), StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Teks postingan ini sudah ada di daftar.", "Duplikat");
                return;
            }
            if (posts.Count >= 50)
            {
                MessageBox.Show("Daftar sudah mencapai batas 50 item. Hapus beberapa item terlebih dahulu.", "Daftar penuh");
                return;
            }
            posts.Insert(0, new SavedPost { Text = selectedText, SourceUrl = source, AddedAt = DateTime.Now });
            SavePosts();
            RefreshPostCards();
            infoStatus.Text = "Postingan berhasil ditambahkan.";
        }
        catch (Exception ex) { MessageBox.Show("Tidak dapat mengambil teks pilihan: " + ex.Message, "Gagal menambahkan"); }
    }

    private static string Normalize(string value) => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private void RefreshPostCards()
    {
        if (postList.IsDisposed || postList.ClientSize.Width < 100) return;
        postList.SuspendLayout();
        postList.Controls.Clear();
        int cardWidth = Math.Max(300, postList.ClientSize.Width - 38);
        foreach (var post in posts.ToList())
        {
            var card = new Panel { Width = cardWidth, Height = 232, BackColor = Color.FromArgb(23, 32, 32), Margin = new Padding(2, 4, 2, 5), Padding = new Padding(8), BorderStyle = BorderStyle.FixedSingle };
            var check = new CheckBox { Text = "Pilih", Checked = post.Selected, AutoSize = true, ForeColor = Color.FromArgb(217, 255, 0), Location = new Point(8, 7) };
            check.CheckedChanged += (_, _) => { post.Selected = check.Checked; SavePosts(); };
            card.Controls.Add(check);
            var when = new Label { Text = "Ditambahkan: " + post.AddedAt.ToString("dd/MM HH:mm"), ForeColor = Color.Silver, AutoSize = true, Location = new Point(82, 9) };
            card.Controls.Add(when);
            var excerpt = new Label { Text = Short(post.Text, 180), ForeColor = Color.White, Font = new Font("Segoe UI", 8.5f), Location = new Point(8, 32), Size = new Size(cardWidth - 24, 46), AutoEllipsis = true };
            card.Controls.Add(excerpt);
            var draftBox = new TextBox { Text = post.Draft, Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(8, 82), Size = new Size(cardWidth - 24, 74), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(8, 13, 13), ForeColor = Color.White, PlaceholderText = "Draf komentar AI akan muncul di sini; Anda boleh mengeditnya." };
            draftBox.Leave += (_, _) => { post.Draft = draftBox.Text; SavePosts(); };
            card.Controls.Add(draftBox);
            Button MakeButton(string label, int x, int width, Action action, Color color)
            {
                var b = new Button { Text = label, Location = new Point(x, 174), Size = new Size(width, 29), BackColor = color, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
                b.FlatAppearance.BorderSize = 0;
                b.Click += (_, _) => action();
                return b;
            }
            card.Controls.Add(MakeButton("BUAT DRAF AI", 8, 110, async () =>
            {
                try { await GenerateForPostAsync(post, CancellationToken.None); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Gagal membuat komentar AI"); }
            }, Color.FromArgb(35, 120, 160)));
            card.Controls.Add(MakeButton("SALIN DRAF", 124, 92, () =>
            {
                post.Draft = draftBox.Text;
                SavePosts();
                if (!string.IsNullOrWhiteSpace(post.Draft)) Clipboard.SetText(post.Draft);
            }, Color.FromArgb(65, 85, 85)));
            card.Controls.Add(MakeButton("BUKA", 222, 62, () => OpenSource(post.SourceUrl), Color.FromArgb(70, 90, 40)));
            card.Controls.Add(MakeButton("HAPUS", 290, 68, () => { posts.Remove(post); SavePosts(); RefreshPostCards(); }, Color.FromArgb(110, 45, 45)));
            postList.Controls.Add(card);
        }
        countStatus.Text = posts.Count + " / 50 item tersimpan";
        if (posts.Count == 0)
            postList.Controls.Add(new Label { Text = "DAFTAR MASIH KOSONG\n\nSorot teks postingan di panel Facebook, lalu tekan tombol hijau di atas.", ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 10), TextAlign = ContentAlignment.MiddleCenter, Width = cardWidth, Height = 130 });
        postList.ResumeLayout();
    }

    private void DeleteSelected()
    {
        int removed = posts.RemoveAll(p => p.Selected);
        SavePosts();
        RefreshPostCards();
        infoStatus.Text = removed + " item terpilih dihapus.";
    }

    private void ClearAll()
    {
        if (posts.Count == 0) return;
        if (MessageBox.Show("Hapus semua item dari daftar bacaan?", "Konfirmasi", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        posts.Clear();
        SavePosts();
        RefreshPostCards();
        infoStatus.Text = "Daftar bacaan dikosongkan.";
    }

    private void OpenSource(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) web.Source = uri;
        else MessageBox.Show("Alamat sumber tidak valid.");
    }

    private void LoadPosts()
    {
        try
        {
            Directory.CreateDirectory(appFolder);
            if (!File.Exists(dataFile)) return;
            var loaded = JsonSerializer.Deserialize<List<SavedPost>>(File.ReadAllText(dataFile));
            if (loaded != null) posts.AddRange(loaded.Take(50));
        }
        catch { }
    }

    private void SavePosts()
    {
        try
        {
            Directory.CreateDirectory(appFolder);
            File.WriteAllText(dataFile, JsonSerializer.Serialize(posts, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { infoStatus.Text = "Gagal menyimpan daftar: " + ex.Message; }
    }

    private void Navigate()
    {
        var value = address.Text.Trim();
        if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) value = "https://" + value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)) web.Source = uri;
        else MessageBox.Show("Alamat tidak valid.");
    }

    private async Task StartBrowser()
    {
        try
        {
            Directory.CreateDirectory(profile);
            var env = await CoreWebView2Environment.CreateAsync(null, profile);
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            web.NavigationStarting += (_, e) =>
            {
                browserStatus.Text = "Membuka halaman Facebook...";
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) address.Text = uri.ToString();
            };
            web.NavigationCompleted += (_, e) => browserStatus.Text = e.IsSuccess ? "Facebook siap. Login langsung; sorot teks postingan untuk menyimpannya." : "Halaman gagal dimuat; coba muat ulang.";
            web.Source = new Uri("https://www.facebook.com/");
            browserStatus.Text = "Silakan login langsung di situs Facebook.";
        }
        catch (Exception ex)
        {
            browserStatus.Text = "WebView2 belum siap.";
            MessageBox.Show("WebView2 Runtime diperlukan. Pasang Evergreen Runtime resmi Microsoft:\nhttps://developer.microsoft.com/microsoft-edge/webview2/\n\n" + ex.Message, "WebView2", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}