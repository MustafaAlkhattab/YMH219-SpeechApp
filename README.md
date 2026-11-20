# Fırat Speech

Windows Forms üzerinde Türkçe ve İngilizce metin okuma ve konuşma dökümü. Model dosyaları kurulduktan sonra iki işlem de bilgisayarda çevrimdışı çalışır.

**Öğrenci** · Mustafa Alkhatta, 250541607

## Kurulum ve çalıştırma

Windows 10/11 x64 ve .NET 10 SDK gereklidir. Visual Studio .NET masaüstü iş yüküyle `SpeechApp.csproj` açıp çalıştırabilirsiniz.

```powershell
git clone https://github.com/MustafaAlkhattab/YMH219-SpeechApp.git
cd YMH219-SpeechApp
dotnet run -c Release
```

İlk açılışta **Modelleri indir** düğmesine basın. İndirme yaklaşık 480 MB'dir. Kurulum için en az 650 MB boş alan ayırın. **Model klasörü** ile başka bir diski seçebilirsiniz. Arayüz indirme ve hata durumlarını gösterir. Kurulum, indirilen dosyaların SHA-256 özetlerini doğrular. Windows masaüstü konuşma dil paketi ve API anahtarı gerekmez.

## Metinden sese

İlk sekmede metin yazın ve içeriğin diline uygun sesi seçin. Hem Türkçe hem İngilizce için iki farklı sinir ağı sesi vardır. **Örnek metin** seçili dilde bir deneme cümlesi doldurur. **Sesli oku**, **Duraklat / Devam**, **Durdur** ve **Temizle** işlemleri bulunur. Okuma hızını değiştirebilir, süre ve ilerleme çubuğundan oynatmayı takip edebilirsiniz.

## Sesten metne

1. **Sesten metne** sekmesinde konuşacağınız dili ve mikrofonu seçin.
2. **Mikrofonu başlat** düğmesine basın. Ses göstergesini izleyerek 1–2 cümle konuşun ve **Bitir ve yazıya dök** düğmesine basın. Bir kayıt 28 saniyeyle sınırlıdır ve sınıra ulaşınca otomatik çözümlenir.
3. Metin saat ve dil ile gösterilir ve düzenlenebilir. **TXT kaydet** dosya oluşturur.
4. Mikrofon yerine bir **WAV aç** seçeneği veya **Örneği çöz** ile seçili dilde cihazda üretilen kısa bir sentetik örnek kullanılabilir.

## Teknoloji ve tasarım

C# / .NET 10, Windows Forms, sherpa-onnx 2.0.1 (Windows x64 çalışma zamanı) ve NAudio 2.2.1. `MicrophoneTake` mikrofonu açıp tek bir konuşma parçasını belleğe alır. `NeuralSpeech` Supertonic 3 ile sesi üretir. Silero ile kayıtta konuşma olup olmadığını kontrol eder ve çok dilli Whisper Small int8 ile cümlenin tamamını çözer. Hesaplama arayüz iş parçacığından ayrı yürütülür.

## Ekran görüntüleri

![Türkçe sinir ağıyla metin okuma](docs/01-ready.png)
![Türkçe ve İngilizce örneklerin dökümü](docs/02-playback.png)

## Doğrulama ve sınırlamalar

Windows Release derlemesi, dört ses seçeneğinin ses üretmesi, iki dilde yerel örnek çözümlemesi ve kaydedilmiş bir İngilizce konuşma dosyası sınandı. Mikrofon açma ve durdurma işlemleri gerçek bir Windows aygıtında doğrulandı. Sessizlikte döküm üretilmedi.

Doğruluk telaffuza, çevre gürültüsüne ve seçilen dile bağlıdır. Dil her kayıt için seçilir. Uygulama iki dilli karışık bir kaydı otomatik bölmez ve durdurduktan sonra metni üretir. Mikrofon erişilemiyorsa durumu bildirir ve WAV çözümleme kullanılabilir. Ses kaydı bellekte kalır. Yalnız model kurulumunda ağ kullanılır.

## Model kaynakları

- [Sherpa-ONNX](https://github.com/k2-fsa/sherpa-onnx) · [Whisper Small ONNX](https://huggingface.co/csukuangfj/sherpa-onnx-whisper-small) · [Silero VAD](https://github.com/snakers4/silero-vad)
- [Supertone Supertonic 3](https://huggingface.co/Supertone/supertonic-3) · [Sherpa-ONNX Supertonic int8 paketi](https://k2-fsa.github.io/sherpa/onnx/tts/supertonic.html). Model paketindeki OpenRAIL-M lisansı geçerlidir.
