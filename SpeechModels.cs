using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;

namespace SpeechApp;

internal sealed record InstallProgress(string Name, int Percent);

internal static class SpeechModels
{
    private const string Whisper = "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-small/resolve/8f3c18b358db4d1f2fc1eae49d75cd20989e4309/";
    private const string Releases = "https://github.com/k2-fsa/sherpa-onnx/releases/download/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private static readonly string Config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YMH219-SpeechApp");
    private static string LocationFile => Path.Combine(Config, "model-location.txt");
    public static string Root { get; private set; } = ReadLocation();

    private sealed record Download(string File, string Url, string Sha256);
    private static readonly Download[] Recognition =
    [
        new("small-encoder.int8.onnx", Whisper + "small-encoder.int8.onnx", "4CBE7B22FA9026B843B60A68640C747DE05BAFB1A11B57EDC0E66C232D9F33A9"),
        new("small-decoder.int8.onnx", Whisper + "small-decoder.int8.onnx", "ACAD50B5C782696E91B55914CC5AB4F756F1532F76E22AA6FC615F39FB69A8EE"),
        new("small-tokens.txt", Whisper + "small-tokens.txt", "B34B360DBB493E781E479794586D661700670D65564001F23024971D1F2FA126"),
        new("silero_vad.onnx", Releases + "asr-models/silero_vad.onnx", "9E2449E1087496D8D4CABA907F23E0BD3F78D91FA552479BB9C23AC09CBB1FD6")
    ];
    private static readonly string[] VoiceHashes =
    [
        "B63BABAEFCDD63202C7079ABA25B72A2013825AD5895E2AEFF03D1AE5A5B3BBF",
        "9E3FEBFACF0ABF4270172D2958BCEC246032B7E88EFC2720840CC80C93DE334E",
        "C546AF78B6395B4E7C4CE1ED899438B64426A362F5D4EC5FECD090DED9AD7505"
    ];

    public static string File(string name) => Path.Combine(Root, name);
    private static string Bundle(Voice voice) => "vits-piper-" + voice.Id;
    public static string VoiceDirectory(Voice voice) => File(Bundle(voice));
    public static bool VoiceReady(Voice voice) => IsVoiceDirectoryReady(VoiceDirectory(voice), voice);
    public static bool RecognitionReady => Recognition.All(item => Present(File(item.File)));
    public static bool Ready => RecognitionReady && NeuralSpeech.Voices.All(VoiceReady);

    private static bool IsVoiceDirectoryReady(string directory, Voice voice) =>
        Present(Path.Combine(directory, voice.Id + ".onnx")) &&
        Present(Path.Combine(directory, "tokens.txt")) &&
        Present(Path.Combine(directory, "espeak-ng-data", "phondata"));

    private static bool Present(string path)
    {
        var file = new FileInfo(path);
        return file.Exists && file.Length > 0;
    }

    public static void SetLocation(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Config);
        System.IO.File.WriteAllText(LocationFile, path);
        Root = path;
    }

    private static string ReadLocation()
    {
        try
        {
            if (System.IO.File.Exists(LocationFile))
            {
                var value = System.IO.File.ReadAllText(LocationFile).Trim();
                if (Path.IsPathFullyQualified(value)) return value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return Path.Combine(Config, "models");
    }

    public static async Task InstallAsync(IProgress<InstallProgress> progress, CancellationToken token)
    {
        Directory.CreateDirectory(Root);
        string staging = File(".download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            for (int i = 0; i < NeuralSpeech.Voices.Length; i++)
            {
                Voice voice = NeuralSpeech.Voices[i];
                if (VoiceReady(voice)) continue;
                string name = Bundle(voice) + ".tar.bz2";
                string archive = Path.Combine(staging, name);
                await FetchAsync(new(name, Releases + "tts-models/" + name, VoiceHashes[i]), archive, progress, token).ConfigureAwait(false);
                progress.Report(new("Ses paketi açılıyor · " + voice.Name, 100));
                await ExtractAsync(archive, staging, token).ConfigureAwait(false);
                string extracted = Path.Combine(staging, Bundle(voice));
                if (!IsVoiceDirectoryReady(extracted, voice)) throw new InvalidDataException("Ses paketi eksik.");
                string destination = VoiceDirectory(voice);
                if (Directory.Exists(destination))
                    Directory.Move(destination, destination + ".previous-" + Guid.NewGuid().ToString("N"));
                Directory.Move(extracted, destination);
                System.IO.File.Delete(archive);
            }

            foreach (Download item in Recognition)
            {
                if (Present(File(item.File))) continue;
                string pending = Path.Combine(staging, item.File);
                await FetchAsync(item, pending, progress, token).ConfigureAwait(false);
                System.IO.File.Move(pending, File(item.File), overwrite: true);
            }
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task FetchAsync(Download item, string destination, IProgress<InstallProgress> progress, CancellationToken token)
    {
        using var response = await Http.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long received = 0;
        int last = -1;
        progress.Report(new(item.File, 0));
        while (true)
        {
            int size = await input.ReadAsync(buffer, token).ConfigureAwait(false);
            if (size == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, size), token).ConfigureAwait(false);
            sha.AppendData(buffer, 0, size);
            received += size;
            int percent = response.Content.Headers.ContentLength is > 0 and var total
                ? (int)Math.Clamp(received * 100 / total, 0, 100) : 0;
            if (percent != last) progress.Report(new(item.File, last = percent));
        }
        if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(sha.GetHashAndReset()), item.Sha256))
            throw new InvalidDataException("İndirilen modelin SHA-256 doğrulaması başarısız oldu: " + item.File);
    }

    private static async Task ExtractAsync(string archive, string folder, CancellationToken token)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "tar.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
        };
        foreach (string arg in new[] { "-xf", archive, "-C", folder }) start.ArgumentList.Add(arg);
        using var extractor = Process.Start(start) ?? throw new IOException("Model paketi açılamadı.");
        var errors = extractor.StandardError.ReadToEndAsync();
        try { await extractor.WaitForExitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!extractor.HasExited) extractor.Kill();
            await extractor.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }
        if (extractor.ExitCode != 0) throw new IOException("Model paketi açılamadı. " + await errors.ConfigureAwait(false));
    }
}
