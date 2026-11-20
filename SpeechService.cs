using System.Speech.Recognition;
using System.Speech.Synthesis;
namespace SpeechApp;

public sealed class SpeechService : IDisposable
{
    private readonly SpeechSynthesizer speaker = new();
    private SpeechRecognitionEngine? recognizer;
    public event Action<string>? Status;
    public event Action<string, float>? Recognized;
    public event Action? RecognitionStopped;
    public event Action? SpeakingStopped;
    public bool Listening { get; private set; }
    public bool Speaking { get; private set; }
    public bool Paused => speaker.State == SynthesizerState.Paused;
    public string[] Voices => speaker.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToArray();
    public RecognizerInfo[] Recognizers => SpeechRecognitionEngine.InstalledRecognizers().ToArray();
    public SpeechService()
    {
        speaker.SpeakCompleted += (_, e) => { Speaking = false; Status?.Invoke(e.Error is null ? "Okuma tamamlandı." : "Okuma hatası: " + e.Error.Message); SpeakingStopped?.Invoke(); };
    }
    public void Speak(string text, string voice, int rate)
    {
        if (Listening) throw new InvalidOperationException("Önce mikrofonu durdurun.");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Okunacak metni girin.");
        speaker.SelectVoice(voice); speaker.Rate = rate; speaker.SetOutputToDefaultAudioDevice();
        Speaking = true;
        try { speaker.SpeakAsync(text); Status?.Invoke("METİN OKUNUYOR"); } catch { Speaking = false; throw; }
    }
    public void PauseOrResume() { if (!Speaking) return; if (Paused) { speaker.Resume(); Status?.Invoke("METİN OKUNUYOR"); } else { speaker.Pause(); Status?.Invoke("OKUMA DURAKLATILDI"); } }
    public void StopSpeaking() { if (Paused) speaker.Resume(); speaker.SpeakAsyncCancelAll(); }
    public void Listen(RecognizerInfo info)
    {
        if (Speaking) throw new InvalidOperationException("Önce metin okumayı durdurun.");
        if (Listening) return;
        recognizer?.Dispose(); recognizer = new(info);
        try
        {
            recognizer.LoadGrammar(new DictationGrammar());
            recognizer.SpeechRecognized += (_, e) => Recognized?.Invoke(e.Result.Text, e.Result.Confidence);
            recognizer.RecognizeCompleted += (_, e) => { Listening = false; Status?.Invoke(e.Error is null ? "Mikrofon durduruldu." : "Tanıma hatası: " + e.Error.Message); RecognitionStopped?.Invoke(); };
            recognizer.SetInputToDefaultAudioDevice(); Listening = true; recognizer.RecognizeAsync(RecognizeMode.Multiple); Status?.Invoke("MİKROFON DİNLENİYOR · " + info.Culture.Name);
        }
        catch { Listening = false; recognizer.Dispose(); recognizer = null; throw; }
    }
    public void StopListening() { if (Listening) recognizer?.RecognizeAsyncCancel(); }
    public void Dispose() { recognizer?.Dispose(); speaker.Dispose(); }
}
