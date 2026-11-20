using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SherpaOnnx;

namespace SpeechApp;

internal sealed record Voice(string Id, string Name, string Language, string Example)
{
    public override string ToString() => Name;
}

internal sealed class NeuralSpeech : IDisposable
{
    public const int SampleRate = 16000;
    public const int MaxSeconds = 28;
    private static int Threads => Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

    public static readonly Voice[] Voices =
    [
        new("tr_TR-dfki-medium", "Türkçe · DFKI", "tr", "Merhaba! Bu uygulama Türkçe metinleri doğal bir sesle okur."),
        new("en_US-lessac-medium", "English · Lessac", "en", "Hello! This studio reads English text aloud with a local neural voice."),
        new("en_US-ryan-medium", "English · Ryan", "en", "Good afternoon! This is a short test of the English voice.")
    ];

    private OfflineTts? tts;
    private Voice? activeVoice;
    private OfflineRecognizer? recognizer;
    private string? activeLanguage;

    public (float[] Samples, int Rate) Synthesize(string text, Voice voice, float speed, CancellationToken token)
    {
        if (!SpeechModels.VoiceReady(voice)) throw new InvalidOperationException("Bu sesin modelini önce indirin.");
        token.ThrowIfCancellationRequested();
        if (activeVoice != voice)
        {
            var directory = SpeechModels.VoiceDirectory(voice);
            var config = new OfflineTtsConfig { MaxNumSentences = 2, SilenceScale = .25f };
            config.Model.NumThreads = Threads;
            config.Model.Vits.Model = Path.Combine(directory, voice.Id + ".onnx");
            config.Model.Vits.Tokens = Path.Combine(directory, "tokens.txt");
            config.Model.Vits.DataDir = Path.Combine(directory, "espeak-ng-data");
            var next = new OfflineTts(config);
            tts?.Dispose();
            tts = next;
            activeVoice = voice;
        }

        var audio = tts!.GenerateWithCallback(text, speed, 0, (_, _) => token.IsCancellationRequested ? 0 : 1);
        try
        {
            token.ThrowIfCancellationRequested();
            if (audio.NumSamples == 0) throw new InvalidOperationException("Metinden ses üretilemedi.");
            return (audio.Samples, audio.SampleRate);
        }
        finally { audio.Dispose(); }
    }

    public string Transcribe(float[] samples, string language)
    {
        if (!SpeechModels.RecognitionReady) throw new InvalidOperationException("Whisper ve konuşma algılama modellerini önce indirin.");
        if (samples.Length < SampleRate / 3) return "";

        // Silero filters silence and background noise before Whisper decodes speech.
        var vadConfig = new VadModelConfig { SampleRate = SampleRate, NumThreads = 1 };
        vadConfig.SileroVad.Model = SpeechModels.File("silero_vad.onnx");
        vadConfig.SileroVad.Threshold = .5f;
        vadConfig.SileroVad.MinSpeechDuration = .25f;
        vadConfig.SileroVad.MinSilenceDuration = .55f;
        vadConfig.SileroVad.MaxSpeechDuration = MaxSeconds;
        using var vad = new VoiceActivityDetector(vadConfig, 36);
        vad.AcceptWaveform(samples);
        vad.Flush();
        if (vad.IsEmpty()) return "";

        if (activeLanguage != language)
        {
            var config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = SampleRate;
            config.ModelConfig.ModelType = "whisper";
            config.ModelConfig.Whisper.Encoder = SpeechModels.File("small-encoder.int8.onnx");
            config.ModelConfig.Whisper.Decoder = SpeechModels.File("small-decoder.int8.onnx");
            config.ModelConfig.Whisper.Language = language;
            config.ModelConfig.Whisper.Task = "transcribe";
            config.ModelConfig.Tokens = SpeechModels.File("small-tokens.txt");
            config.ModelConfig.NumThreads = Threads;
            // Release the previous language's session before loading another one.
            recognizer?.Dispose();
            recognizer = null;
            activeLanguage = null;
            recognizer = new OfflineRecognizer(config);
            activeLanguage = language;
        }

        var transcript = new List<string>();
        while (!vad.IsEmpty())
        {
            var segment = vad.Front();
            var start = Math.Max(0, (int)segment.Start - SampleRate / 5);
            var end = Math.Min(samples.Length, (int)segment.Start + segment.Samples.Length + SampleRate / 5);
            using var stream = recognizer!.CreateStream();
            stream.AcceptWaveform(SampleRate, samples[start..end]);
            recognizer.Decode(stream);
            var text = stream.Result.Text.Trim();
            if (text.Length > 0) transcript.Add(text);
            vad.Pop();
        }
        return string.Join(' ', transcript);
    }

    public static float[] ReadWave(string path)
    {
        using var wave = new WaveFileReader(path);
        if (wave.TotalTime > TimeSpan.FromSeconds(MaxSeconds))
            throw new ArgumentException($"En fazla {MaxSeconds} saniyelik bir WAV dosyası seçin.");
        ISampleProvider source = wave.ToSampleProvider();
        if (source.WaveFormat.Channels == 2) source = new StereoToMonoSampleProvider(source);
        if (source.WaveFormat.Channels != 1) throw new ArgumentException("Tek veya iki kanallı bir WAV dosyası seçin.");
        return ReadMono(source);
    }

    public static float[] Resample(float[] samples, int rate)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        using var wave = new RawSourceWaveStream(new MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(rate, 1));
        return ReadMono(wave.ToSampleProvider());
    }

    private static float[] ReadMono(ISampleProvider source)
    {
        source = new WdlResamplingSampleProvider(source, SampleRate);
        var buffer = new float[SampleRate * MaxSeconds];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = source.Read(buffer, count, buffer.Length - count);
            if (read == 0) break;
            count += read;
        }
        return buffer[..count];
    }

    public void Dispose()
    {
        tts?.Dispose();
        recognizer?.Dispose();
    }
}
