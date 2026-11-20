using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SherpaOnnx;

namespace SpeechApp;

internal sealed record Voice(int Speaker, string Name, string Language, string Example)
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
        new(0, "Türkçe · Ses 1", "tr", "Merhaba! Bugün hava çok güzel. Yarın tekrar görüşürüz."),
        new(6, "Türkçe · Ses 2", "tr", "Günaydın! Bugün Fırat Üniversitesi'nde yeni bir proje deniyoruz."),
        new(0, "English · Voice 1", "en", "Hello! This studio reads English text aloud with a local neural voice."),
        new(6, "English · Voice 2", "en", "Good afternoon! This is a short test of the English voice.")
    ];

    private OfflineTts? tts;
    private OfflineRecognizer? recognizer;
    private string? activeLanguage;

    public (float[] Samples, int Rate) Synthesize(string text, Voice voice, float speed, CancellationToken token)
    {
        if (!SpeechModels.SynthesisReady) throw new InvalidOperationException("Okuma modelini önce indirin.");
        token.ThrowIfCancellationRequested();
        if (tts is null)
        {
            var config = new OfflineTtsConfig { MaxNumSentences = 2, SilenceScale = .25f };
            config.Model.NumThreads = Threads;
            config.Model.Supertonic.DurationPredictor = SpeechModels.VoiceFile("duration_predictor.int8.onnx");
            config.Model.Supertonic.TextEncoder = SpeechModels.VoiceFile("text_encoder.int8.onnx");
            config.Model.Supertonic.VectorEstimator = SpeechModels.VoiceFile("vector_estimator.int8.onnx");
            config.Model.Supertonic.Vocoder = SpeechModels.VoiceFile("vocoder.int8.onnx");
            config.Model.Supertonic.TtsJson = SpeechModels.VoiceFile("tts.json");
            config.Model.Supertonic.UnicodeIndexer = SpeechModels.VoiceFile("unicode_indexer.bin");
            config.Model.Supertonic.VoiceStyle = SpeechModels.VoiceFile("voice.bin");
            tts = new OfflineTts(config);
        }

        var generation = new OfflineTtsGenerationConfig { Sid = voice.Speaker, Speed = speed, NumSteps = 8, SilenceScale = .26f };
        generation.Extra["lang"] = voice.Language;
        var audio = tts.GenerateWithConfig(text, generation, (_, _, _, _) => token.IsCancellationRequested ? 0 : 1);
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

        // Decode the whole short take once. A VAD boundary can land inside a word,
        // and independent fragments would deprive Whisper of sentence context.
        using var stream = recognizer!.CreateStream();
        stream.AcceptWaveform(SampleRate, samples);
        recognizer.Decode(stream);
        return stream.Result.Text.Trim();
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
