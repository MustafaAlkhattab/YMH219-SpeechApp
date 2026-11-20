using System.Buffers.Binary;
using NAudio.Wave;

namespace SpeechApp;

internal sealed class MicrophoneTake : IDisposable
{
    private readonly WaveInEvent device;
    private readonly List<float> samples = new(NeuralSpeech.SampleRate * 8);
    private readonly TaskCompletionSource<float[]> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object sync = new();
    private float level;

    public MicrophoneTake(int deviceNumber)
    {
        device = new WaveInEvent
        {
            DeviceNumber = deviceNumber, BufferMilliseconds = 50,
            WaveFormat = new(NeuralSpeech.SampleRate, 16, 1)
        };
        device.DataAvailable += OnData;
        device.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null) completion.TrySetException(e.Exception);
            else lock (sync) completion.TrySetResult(samples.ToArray());
        };
    }

    public Task<float[]> Completion => completion.Task;
    public (int Count, float Level) Meter { get { lock (sync) return (samples.Count, level); } }
    public void Start() => device.StartRecording();
    public void Stop() => device.StopRecording();

    private void OnData(object? sender, WaveInEventArgs e)
    {
        const int limit = NeuralSpeech.SampleRate * NeuralSpeech.MaxSeconds;
        bool full;
        lock (sync)
        {
            float peak = 0;
            for (int i = 0; i + 1 < e.BytesRecorded && samples.Count < limit; i += 2)
            {
                float value = BinaryPrimitives.ReadInt16LittleEndian(e.Buffer.AsSpan(i, 2)) / 32768f;
                samples.Add(value);
                peak = Math.Max(peak, Math.Abs(value));
            }
            level = peak;
            full = samples.Count >= limit;
        }
        if (full) Stop();
    }

    public void Dispose() => device.Dispose();
}
