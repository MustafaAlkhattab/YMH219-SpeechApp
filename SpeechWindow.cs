using System.Diagnostics;
using NAudio.Wave;

namespace SpeechApp;

internal sealed class SpeechWindow : Form
{
    private enum Mode { Idle, Installing, Synthesizing, Playing, Paused, Recording, Transcribing }
    private Mode mode;
    private NeuralSpeech speech = new();
    private MicrophoneTake? take;
    private WaveOutEvent? player;
    private CancellationTokenSource? cancellation;
    private bool closePending;
    private readonly System.Windows.Forms.Timer meterTimer = new() { Interval = 100 };
    private readonly Label status = Ui.Label("Hazırlanıyor…", 10, true, Ui.Muted);
    private readonly Label modelState = Ui.Label("Dil modelleri", 10, false, Ui.Muted);
    private readonly ProgressBar installProgress = new() { Dock = DockStyle.Fill, Height = 10, Margin = new(0, 3, 0, 7), Visible = false };
    private readonly ProgressBar micLevel = new() { Dock = DockStyle.Fill, Height = 13, Margin = new(0, 3, 0, 8) };
    private readonly Label micStatus = Ui.Label("Mikrofon kapalı", 10, true, Ui.Muted);
    private readonly TextBox input = Editor("Seslendirilecek metin");
    private readonly TextBox transcript = Editor("Konuşma dökümü");
    private readonly ComboBox voices = Picker("Okuma dili ve ses", 203);
    private readonly ComboBox languages = Picker("Dikte dili", 140);
    private readonly ComboBox devices = Picker("Mikrofon", 278);
    private readonly NumericUpDown rate = new() { Minimum = .70m, Maximum = 1.40m, Value = 1m, Increment = .05m, DecimalPlaces = 2, Width = 72 };
    private readonly Button install = Ui.Button("Modelleri indir");
    private readonly Button cancel = Ui.Button("İndirmeyi iptal et");
    private readonly Button location = Ui.Button("Model klasörü");
    private readonly Button play = Ui.Button("Sesli oku", true);
    private readonly Button pause = Ui.Button("Duraklat");
    private readonly Button stop = Ui.Button("Durdur");
    private readonly Button record = Ui.Button("Mikrofonu başlat", true);
    private readonly Button finish = Ui.Button("Bitir ve yazıya dök");
    private readonly Button openWave = Ui.Button("WAV aç");
    private readonly Button demoWave = Ui.Button("Örneği çöz");

    public SpeechWindow()
    {
        Ui.Window(this, "Fırat Speech", 1050, 690, 705, 575);
        input.PlaceholderText = "Okunacak metni buraya yazın…";
        transcript.PlaceholderText = "Konuşma dökümü burada görünür. Sonucu düzenleyebilir ve kaydedebilirsiniz.";
        input.MaxLength = 1600;
        voices.Items.AddRange(NeuralSpeech.Voices.Cast<object>().ToArray());
        voices.SelectedIndex = 0;
        languages.Items.AddRange(["Türkçe", "English"]);
        languages.SelectedIndex = 0;
        Ui.Input(rate, "Okuma hızı");
        rate.Margin = new(0, 6, 8, 0);
        var sample = Ui.Button("Örnek metin");
        var clear = Ui.Button("Temizle");
        var clearTranscript = Ui.Button("Temizle");
        var save = Ui.Button("TXT kaydet");
        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = new(0, 4, 0, 9), Padding = new(14, 7) };
        var textToSpeech = new TabPage("Metinden sese") { BackColor = Ui.Surface };
        var speechToText = new TabPage("Sesten metne") { BackColor = Ui.Surface };
        textToSpeech.Controls.Add(Ui.Stack(18,
            (Ui.Label("Metni seslendir", 19, true), 0),
            (Ui.Label("Metninizin diline uygun sesi seçin.", 10, false, Ui.Muted), 0),
            (Ui.Bar(Caption("Ses"), voices, Caption("Hız"), rate, sample), 0),
            (input, 100),
            (Ui.Bar(play, pause, stop, clear), 0)));
        speechToText.Controls.Add(Ui.Stack(18,
            (Ui.Label("Konuşmayı yazıya dök", 19, true), 0),
            (Ui.Label("Dili seçin. Kısa bir cümle söyleyin ve kaydı bitirin (en fazla 28 sn).", 10, false, Ui.Muted), 0),
            (Ui.Bar(Caption("Dil"), languages, Caption("Mikrofon"), devices), 0),
            (Ui.Bar(record, finish, openWave, demoWave), 0),
            (micStatus, 0), (micLevel, 0),
            (transcript, 100),
            (Ui.Bar(save, clearTranscript), 0)));
        tabs.TabPages.AddRange([textToSpeech, speechToText]);
        status.Margin = new(0, 8, 0, 0);
        modelState.Margin = new(0, 11, 10, 0);
        Controls.Add(Ui.Stack(18, (Ui.Label("Fırat Speech", 28, true), 0),
            (Ui.Bar(install, cancel, location, modelState), 0), (installProgress, 0), (tabs, 100), (status, 0)));

        Shown += (_, _) =>
        {
            try
            {
                for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                    devices.Items.Add(WaveInEvent.GetCapabilities(i).ProductName);
                if (devices.Items.Count > 0) devices.SelectedIndex = 0;
                status.Text = "Hazır";
            }
            catch (Exception ex) { Error("Mikrofon aygıtları okunamadı", ex); }
            RefreshControls();
        };
        install.Click += async (_, _) => await Install();
        cancel.Click += (_, _) => cancellation?.Cancel();
        location.Click += (_, _) => SelectLocation();
        sample.Click += (_, _) => { input.Text = SelectedVoice.Example; input.Focus(); };
        play.Click += async (_, _) => await Speak();
        pause.Click += (_, _) => TogglePause();
        stop.Click += (_, _) => StopPlayback();
        clear.Click += (_, _) => { StopPlayback(); input.Clear(); };
        record.Click += async (_, _) => await Record();
        finish.Click += (_, _) => take?.Stop();
        openWave.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "WAV ses dosyaları|*.wav" };
            if (dialog.ShowDialog(this) == DialogResult.OK) await TranscribeFile(dialog.FileName);
        };
        demoWave.Click += async (_, _) =>
        {
            string language = RecognitionLanguage;
            Voice voice = DemoVoice;
            await Transcribe(() =>
            {
                var sample = speech.Synthesize(voice.Example, voice, 1, CancellationToken.None);
                return NeuralSpeech.Resample(sample.Samples, sample.Rate);
            }, language, example: true);
        };
        clearTranscript.Click += (_, _) => transcript.Clear();
        save.Click += (_, _) => SaveTranscript();
        voices.SelectedIndexChanged += (_, _) => RefreshControls();
        languages.SelectedIndexChanged += (_, _) => RefreshControls();
        devices.SelectedIndexChanged += (_, _) => RefreshControls();
        meterTimer.Tick += (_, _) => UpdateMeter();
        RefreshControls();
    }

    private Voice SelectedVoice => (Voice)voices.SelectedItem!;
    private string RecognitionLanguage => languages.SelectedIndex == 1 ? "en" : "tr";
    private Voice DemoVoice => NeuralSpeech.Voices.First(voice => voice.Language == RecognitionLanguage);
    private static TextBox Editor(string name) => new()
    {
        Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true,
        AccessibleName = name, Font = new("Segoe UI", 13), BackColor = Color.White, ForeColor = Ui.Text,
        BorderStyle = BorderStyle.FixedSingle, Margin = new(0, 6, 0, 9)
    };
    private static ComboBox Picker(string name, int width) => new()
    {
        AccessibleName = name, Width = width, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new(0, 6, 10, 3)
    };
    private static Label Caption(string name)
    {
        var label = Ui.Label(name, 10, false, Ui.Muted);
        label.Margin = new(0, 8, 5, 0);
        return label;
    }

    private void SetMode(Mode next, string message)
    {
        mode = next;
        status.Text = message;
        RefreshControls();
    }

    private void RefreshControls()
    {
        bool idle = mode == Mode.Idle;
        bool speaking = mode is Mode.Playing or Mode.Paused;
        bool hasRecognition = SpeechModels.RecognitionReady;
        install.Enabled = idle && !SpeechModels.Ready;
        location.Enabled = idle;
        cancel.Visible = installProgress.Visible = mode == Mode.Installing;
        modelState.Text = SpeechModels.Ready ? "Yerel modeller hazır" : "İlk kurulum · yaklaşık 550 MB";
        play.Enabled = idle && SpeechModels.VoiceReady(SelectedVoice);
        pause.Enabled = speaking;
        pause.Text = mode == Mode.Paused ? "Devam et" : "Duraklat";
        stop.Enabled = speaking || mode == Mode.Synthesizing;
        voices.Enabled = rate.Enabled = idle;
        languages.Enabled = devices.Enabled = idle;
        record.Enabled = idle && hasRecognition && devices.SelectedIndex >= 0;
        finish.Enabled = mode == Mode.Recording;
        openWave.Enabled = idle && hasRecognition;
        demoWave.Enabled = idle && hasRecognition && SpeechModels.VoiceReady(DemoVoice);
        if (devices.Items.Count == 0 && idle) micStatus.Text = "Mikrofon algılanamadı. WAV dosyası kullanılabilir.";
    }

    private async Task Install()
    {
        SetMode(Mode.Installing, "Modeller indiriliyor…");
        using var token = new CancellationTokenSource();
        cancellation = token;
        try
        {
            var progress = new Progress<InstallProgress>(update =>
            {
                if (mode != Mode.Installing) return;
                installProgress.Value = update.Percent;
                status.Text = update.Name + $" · %{update.Percent}";
            });
            await SpeechModels.InstallAsync(progress, token.Token);
            End("Modeller kuruldu. Uygulama artık çevrimdışı kullanılabilir.");
        }
        catch (OperationCanceledException) { End("İndirme iptal edildi. Tamamlanan paketler saklandı."); }
        catch (Exception ex) { End("Modeller indirilemedi. Bağlantıyı ve disk alanını kontrol edin. " + ex.Message); }
        finally { cancellation = null; }
    }

    private void SelectLocation()
    {
        using var folder = new FolderBrowserDialog { Description = "Yerel konuşma modellerinin tutulacağı klasörü seçin." };
        if (Directory.Exists(SpeechModels.Root)) folder.SelectedPath = SpeechModels.Root;
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SpeechModels.SetLocation(folder.SelectedPath);
            speech.Dispose();
            speech = new();
            End(SpeechModels.Ready ? "Model klasörü bağlandı." : "Klasör seçildi. Eksik modelleri indirebilirsiniz.");
        }
        catch (Exception ex) { Error("Model klasörü kullanılamıyor", ex); }
    }

    private async Task Speak()
    {
        string reading = input.Text.Trim();
        if (reading.Length == 0) { status.Text = "Önce okunacak metni yazın."; input.Focus(); return; }
        var voice = SelectedVoice;
        var speed = (float)rate.Value;
        using var token = new CancellationTokenSource();
        cancellation = token;
        SetMode(Mode.Synthesizing, voice.Name + " · Ses hazırlanıyor…");
        try
        {
            var audio = await Task.Run(() => speech.Synthesize(reading, voice, speed, token.Token));
            if (closePending) { End("Ses durduruldu."); return; }
            var bytes = new byte[audio.Samples.Length * sizeof(float)];
            Buffer.BlockCopy(audio.Samples, 0, bytes, 0, bytes.Length);
            var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(audio.Rate, 1))
                { BufferLength = bytes.Length, ReadFully = false };
            provider.AddSamples(bytes, 0, bytes.Length);
            var output = new WaveOutEvent();
            player = output;
            output.PlaybackStopped += (_, e) =>
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke(() => PlaybackFinished(output, e.Exception));
            };
            output.Init(provider);
            output.Play();
            SetMode(Mode.Playing, voice.Name + " · Okunuyor");
        }
        catch (OperationCanceledException) { End("Okuma durduruldu."); }
        catch (Exception ex) { player?.Dispose(); player = null; End("Ses çıkışı açılamadı. " + ex.Message); }
        finally { cancellation = null; }
    }

    private void PlaybackFinished(WaveOutEvent output, Exception? error)
    {
        if (!ReferenceEquals(output, player)) return;
        output.Dispose();
        player = null;
        End(error is null ? "Okuma tamamlandı." : "Ses çıkışı hatası: " + error.Message);
    }

    private void TogglePause()
    {
        if (mode == Mode.Paused) { player?.Play(); SetMode(Mode.Playing, "Okunuyor"); }
        else if (mode == Mode.Playing) { player?.Pause(); SetMode(Mode.Paused, "Okuma duraklatıldı."); }
    }

    private void StopPlayback()
    {
        if (mode == Mode.Synthesizing) cancellation?.Cancel();
        else if (player is not null)
        {
            var current = player;
            player = null;
            current.Stop();
            current.Dispose();
            End("Okuma durduruldu.");
        }
    }

    private async Task Record()
    {
        if (devices.SelectedIndex < 0) { status.Text = "Kullanılabilir bir mikrofon seçin."; return; }
        string language = RecognitionLanguage;
        try
        {
            using var recording = new MicrophoneTake(devices.SelectedIndex);
            take = recording;
            recording.Start();
            SetMode(Mode.Recording, "Mikrofon açık · Kayıt bitince metne dönüştürülür.");
            meterTimer.Start();
            float[] audio = await recording.Completion;
            meterTimer.Stop();
            take = null;
            if (closePending) { End("Kayıt durduruldu."); return; }
            micStatus.Text = "Mikrofon kapalı";
            micLevel.Value = 0;
            await Transcribe(() => audio, language);
        }
        catch (Exception ex)
        {
            End("Mikrofon açılamadı veya kayıt kesildi. Aygıtı ve erişim iznini kontrol edin. " + ex.Message);
            micStatus.Text = "Mikrofon kapalı";
        }
        finally { meterTimer.Stop(); take = null; micLevel.Value = 0; }
    }

    private void UpdateMeter()
    {
        if (take is null) return;
        var (count, level) = take.Meter;
        micLevel.Value = Math.Clamp((int)(level * 180), 0, 100);
        micStatus.Text = $"●  Kayıt · {count / (double)NeuralSpeech.SampleRate:00.0} / {NeuralSpeech.MaxSeconds} sn";
    }

    private Task TranscribeFile(string path) => Transcribe(() => NeuralSpeech.ReadWave(path), RecognitionLanguage);

    private async Task Transcribe(Func<float[]> source, string language, bool example = false)
    {
        SetMode(Mode.Transcribing, (language == "tr" ? "Türkçe" : "English") + " · Konuşma çözümleniyor…");
        try
        {
            string text = await Task.Run(() => speech.Transcribe(source(), language));
            if (text.Length == 0) { End("Konuşma algılanmadı. Mikrofona yakın ve net konuşmayı deneyin."); return; }
            string origin = example ? " · örnek" : "";
            transcript.AppendText($"[{DateTime.Now:HH:mm:ss} · {language.ToUpperInvariant()}{origin}] {text}{Environment.NewLine}");
            End(example ? "Sentetik örneğin dökümü hazır." : "Döküm hazır.");
        }
        catch (Exception ex) { End("Döküm oluşturulamadı. " + ex.Message); }
    }

    private void SaveTranscript()
    {
        using var dialog = new SaveFileDialog { Filter = "Metin dosyası|*.txt", FileName = "konusma-dokumu.txt" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, transcript.Text); status.Text = "Döküm kaydedildi."; }
        catch (Exception ex) { Error("Döküm kaydedilemedi", ex); }
    }

    private void Error(string description, Exception ex) => status.Text = description + ". " + ex.Message;

    private void End(string message)
    {
        SetMode(Mode.Idle, message);
        if (closePending) BeginInvoke(Close);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (mode is Mode.Installing or Mode.Synthesizing or Mode.Transcribing or Mode.Recording)
        {
            e.Cancel = true;
            closePending = true;
            cancellation?.Cancel();
            take?.Stop();
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        player?.Dispose();
        player = null;
        meterTimer.Dispose();
        speech.Dispose();
        base.OnFormClosed(e);
    }
}
