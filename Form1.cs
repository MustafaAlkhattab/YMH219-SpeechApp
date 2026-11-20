namespace SpeechApp;

public partial class Form1 : Form
{
    private SpeechService? speech;
    private bool closing;
    private readonly Label status = new() { Text = "Hazır", AutoSize = true, ForeColor = Color.DarkBlue };
    private readonly RichTextBox text = new() { Dock = DockStyle.Fill, Text = "Hello Mustafa. Welcome to the YMH219 speech application.", AccessibleName = "Okunacak metin" };
    private readonly RichTextBox transcript = new() { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "Tanınan konuşma" };
    private readonly ComboBox voices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, AccessibleName = "Ses" };
    private readonly ComboBox engines = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, AccessibleName = "Tanıma dili" };
    private readonly NumericUpDown rate = new() { Minimum = -10, Maximum = 10, Value = 0, Width = 60 };
    private readonly Button speak = new() { Text = "Sesli oku", AutoSize = true };
    private readonly Button pause = new() { Text = "Duraklat / Devam", AutoSize = true, Enabled = false };
    private readonly Button stop = new() { Text = "Okumayı durdur", AutoSize = true, Enabled = false };
    private readonly Button listen = new() { Text = "Mikrofon başlat", AutoSize = true };
    private readonly Button stopMic = new() { Text = "Mikrofon durdur", AutoSize = true, Enabled = false };
    public Form1()
    {
        InitializeComponent(); Controls.Clear(); AutoScaleMode = AutoScaleMode.None;
        Text = "Fırat Speech | YMH219"; ClientSize = new(1050, 790); MinimumSize = new(950, 700); StartPosition = FormStartPosition.CenterScreen; Font = new("Segoe UI", 10); BackColor = Color.FromArgb(239, 246, 255);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 1, RowCount = 8 };
        root.RowStyles.Add(new(SizeType.Absolute, 58)); root.RowStyles.Add(new(SizeType.Absolute, 55)); root.RowStyles.Add(new(SizeType.Absolute, 85)); root.RowStyles.Add(new(SizeType.Percent, 50)); root.RowStyles.Add(new(SizeType.Absolute, 85)); root.RowStyles.Add(new(SizeType.Percent, 50)); root.RowStyles.Add(new(SizeType.Absolute, 45)); root.RowStyles.Add(new(SizeType.Absolute, 30));
        root.Controls.Add(new Label { Text = "FIRAT SPEECH", AutoSize = true, Font = new("Segoe UI", 24, FontStyle.Bold), ForeColor = Color.FromArgb(30, 64, 175) }, 0, 0);
        root.Controls.Add(new Label { Text = "Metni seslendir veya mikrofon konuşmasını metne dönüştür.\nKonuşurken seçili tanıma motorunun dilini kullanın.", AutoSize = true }, 0, 1);
        var ttsTools = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var clear = new Button { Text = "Metni temizle", AutoSize = true };
        ttsTools.Controls.AddRange([new Label { Text = "Ses", AutoSize = true }, voices, new Label { Text = "Hız", AutoSize = true }, rate, speak, pause, stop, clear]);
        root.Controls.Add(ttsTools, 0, 2); root.Controls.Add(text, 0, 3);
        var micTools = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(0, 12, 0, 0) };
        var clearTranscript = new Button { Text = "Dökümü temizle", AutoSize = true }; var save = new Button { Text = "TXT kaydet", AutoSize = true };
        micTools.Controls.AddRange([new Label { Text = "Tanıma", AutoSize = true }, engines, listen, stopMic, clearTranscript, save]);
        root.Controls.Add(micTools, 0, 4); root.Controls.Add(transcript, 0, 5); root.Controls.Add(status, 0, 6);
        root.Controls.Add(new Label { Text = "Mustafa Alkhatta · 250541607 | Windows System.Speech", AutoSize = true }, 0, 7); Controls.Add(root);
        Shown += (_, _) => InitializeSpeech();
        speak.Click += (_, _) => Run(() => speech!.Speak(text.Text, (string)voices.SelectedItem!, (int)rate.Value));
        pause.Click += (_, _) => Run(() => speech!.PauseOrResume()); stop.Click += (_, _) => Run(() => speech!.StopSpeaking());
        listen.Click += (_, _) => Run(() => { if (engines.SelectedIndex < 0) throw new InvalidOperationException("Windows'ta kurulu konuşma tanıma motoru bulunamadı. Desteklenen dilin konuşma bileşenini kurun."); speech!.Listen(speech.Recognizers[engines.SelectedIndex]); });
        stopMic.Click += (_, _) => Run(() => speech!.StopListening());
        clear.Click += (_, _) => { if (speech?.Speaking == true) speech.StopSpeaking(); text.Clear(); };
        clearTranscript.Click += (_, _) => transcript.Clear();
        save.Click += (_, _) => { using var dialog = new SaveFileDialog { Filter = "Metin (*.txt)|*.txt", FileName = "konusma-dokumu.txt" }; if (dialog.ShowDialog(this) == DialogResult.OK) Run(() => File.WriteAllText(dialog.FileName, transcript.Text, new System.Text.UTF8Encoding(false))); };
    }
    private void InitializeSpeech()
    {
        try
        {
            speech = new(); voices.Items.AddRange(speech.Voices); engines.Items.AddRange(speech.Recognizers.Select(r => $"{r.Culture.Name} — {r.Name}").ToArray());
            if (voices.Items.Count > 0) voices.SelectedIndex = 0; if (engines.Items.Count > 0) engines.SelectedIndex = 0;
            speech.Status += message => Ui(() => { status.Text = message; RefreshButtons(); });
            speech.Recognized += (result, confidence) => Ui(() => transcript.AppendText($"[{DateTime.Now:HH:mm:ss}] {result} (güven: {confidence:P0}){Environment.NewLine}"));
            speech.RecognitionStopped += () => Ui(RefreshButtons); speech.SpeakingStopped += () => Ui(RefreshButtons);
            status.Text = engines.Items.Count == 0 ? "Tanıma motoru yok; mikrofon için Windows konuşma bileşeni gerekli." : "Hazır · Dil: " + speech.Recognizers[0].Culture.Name;
        }
        catch (Exception ex) { status.Text = "Ses altyapısı başlatılamadı: " + ex.Message; }
        RefreshButtons();
    }
    private void Run(Action action) { try { action(); } catch (Exception ex) { status.Text = "Hata: " + ex.Message; MessageBox.Show(this, ex.Message, "Ses / mikrofon hatası", MessageBoxButtons.OK, MessageBoxIcon.Warning); } finally { RefreshButtons(); } }
    private void RefreshButtons()
    {
        var busy = speech?.Speaking == true; var mic = speech?.Listening == true;
        speak.Enabled = speech is not null && voices.Items.Count > 0 && !busy && !mic; pause.Enabled = stop.Enabled = busy;
        listen.Enabled = speech is not null && !mic && !busy; stopMic.Enabled = mic; engines.Enabled = !mic; voices.Enabled = rate.Enabled = !busy;
        pause.Text = speech?.Paused == true ? "Devam et" : "Duraklat";
    }
    private void Ui(Action action) { if (closing || IsDisposed || !IsHandleCreated) return; try { BeginInvoke(action); } catch (InvalidOperationException) { } }
    protected override void OnFormClosing(FormClosingEventArgs e) { closing = true; speech?.Dispose(); base.OnFormClosing(e); }
}
