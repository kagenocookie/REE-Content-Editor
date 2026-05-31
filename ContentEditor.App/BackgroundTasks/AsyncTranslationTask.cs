using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ContentEditor.App;
using ContentEditor.App.Translation;
using ContentEditor.Core;
using ReeLib.Msg;

namespace ContentEditor.BackgroundTasks;

public class AsyncTranslationTask : IBackgroundTask
{
    public string Status { get; private set; }
    public TaskStatus TaskStatus { get; set; }

    private sealed record class PendingTranslation(string text, Language? sourceLang, Language targetLanguage, TranslationService.TranslationCallback callback, int batchGrouping = -1)
    {
        public bool IsCompatibleForBatch(PendingTranslation other) => other.sourceLang == sourceLang && other.targetLanguage == targetLanguage && batchGrouping == other.batchGrouping;
        public override string ToString() => $"{text} [{sourceLang}=>{targetLanguage}]";
    }

    private static string GetTranslationCacheFilepath(Language lang) => Path.Combine(AppConfig.Instance.CacheFilepath.Get() ?? Path.Combine(AppConfig.AppDataPath, "cache"), "translations", lang.ToString() + ".json");

    private static string LangToIso2Code(Language? lang) => lang == null ? "auto" : LangToIso2Code(lang.Value);
    private static string LangToIso2Code(Language lang) => lang switch {
        Language.English => "en",
        Language.Japanese => "ja",
        Language.French => "fr",
        Language.German => "de",
        Language.Bulgarian => "bg",
        Language.Czech => "cz",
        Language.Danish => "dk",
        Language.Dutch => "nl",
        Language.Finnish => "fi",
        Language.Greek => "gr",
        Language.Hindi => "in",
        Language.Hungarian => "hu",
        Language.Indonesian => "id",
        Language.Italian => "it",
        Language.Korean => "kr",
        Language.LatinAmericanSpanish => "es",
        Language.Norwegian => "no",
        Language.Polish => "pl",
        Language.Portuguese => "pt",
        Language.PortugueseBr => "br",
        Language.Romanian => "ro",
        Language.Russian => "ru",
        Language.SimplifiedChinese => "zh-CN",
        Language.Slovak => "sk",
        Language.Swedish => "sv",
        Language.Thai => "th",
        Language.Turkish => "tr",
        Language.Ukrainian => "uk",
        Language.Vietnamese => "vi",
        Language.TraditionalChinese => "zh-TW",
        Language.Spanish => "es",
        _ => lang.ToString(),
    };

    private int processedTranslations = 0;
    private int requestedTranslations = 0;

    public float Progress => requestedTranslations <= 0 ? -1 : (float)processedTranslations / requestedTranslations;

    private static readonly Dictionary<Language, ConcurrentDictionary<string, string>> _translationsCache = new();

    private readonly ConcurrentBag<PendingTranslation> requests = new();

    public AsyncTranslationTask()
    {
        Status = "Preparing";
    }

    public static bool TryGetCachedTranslation(Language targetLanguage, string text, [MaybeNullWhen(false)] out string translated)
    {
        if (!_translationsCache.TryGetValue(targetLanguage, out var texts)) {
            var cachePath = GetTranslationCacheFilepath(targetLanguage);
            if (File.Exists(cachePath) && cachePath.TryDeserializeJsonFile<ConcurrentDictionary<string, string>>(out var fileCache, out var err)) {
                _translationsCache[targetLanguage] = texts = fileCache;
            } else {
                _translationsCache[targetLanguage] = texts = new();
            }
        }

        if (texts.TryGetValue(text, out translated)) {
            return true;
        }
        return false;
    }

    public override string ToString() => "Translating";

    public static void ResetCachedTranslations()
    {
        _translationsCache.Clear();
    }

    public void Translate(string text, Language? sourceLanguage, Language targetLanguage, TranslationService.TranslationCallback callback)
    {
        if (string.IsNullOrWhiteSpace(text)) {
            callback(true, text);
            return;
        }
        if (TryGetCachedTranslation(targetLanguage, text, out var cached)) {
            callback(true, cached);
            return;
        }

        requestedTranslations++;
        requests.Add(new PendingTranslation(text, sourceLanguage, targetLanguage, callback));
    }

    private const int MaxBatchTextLength = 1000;
    private const string TranslationSeparatorMarker = "\n|||\n";

    private static Task? _saveCacheTask;

    public async Task Execute(CancellationToken token = default)
    {
        // give a bit of a delay so the inital task dispatcher has time to queue any initial TL requests
        await Task.Delay(50, token);
        Status = "Fetching translations";
        int nextBatchGroupId = 0;
        List<PendingTranslation> batch = new();
        while (TaskStatus == TaskStatus.Running && !token.IsCancellationRequested && requests.TryTake(out var task)) {
            batch.Add(task);
            var batchLen = task.text.Length;
            var sourceLang = task.sourceLang;
            var lang = task.targetLanguage;
            var texts = _translationsCache[lang];
            if (texts.TryGetValue(task.text, out var cachedResult)) {
                task.callback.Invoke(true, cachedResult);
                continue;
            }

            // attempt to batch up more than one at a time
            while (batchLen < MaxBatchTextLength && requests.TryTake(out var nextTask)) {
                var nextLen = batchLen + nextTask.text.Length + TranslationSeparatorMarker.Length;
                if (nextLen > MaxBatchTextLength || !nextTask.IsCompatibleForBatch(task)) {
                    requests.Add(nextTask);
                    break;
                }

                if (texts.TryGetValue(nextTask.text, out cachedResult)) {
                    nextTask.callback.Invoke(true, cachedResult);
                    continue;
                }

                batchLen = nextLen;
                batch.Add(nextTask);
            }

            var combinedText = string.Join(TranslationSeparatorMarker, batch.Select(b => b.text));
            var combinedTranslation = await GoogleTranslate.Translate(LangToIso2Code(sourceLang), LangToIso2Code(lang), combinedText);
            var translations = combinedTranslation.Split("|||", StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
            if (translations.Length != batch.Count) {
                // well, what now?
                Logger.Error("Batch translation failed, received mismatched number of translations. Try again maybe.");
                batch.Clear();
                continue;
            }

            var didAnyTranslation = false;
            for (int i = 0; i < batch.Count; i++) {
                if (translations[i] != batch[i].text) {
                    didAnyTranslation = true;
                }
            }
            if (!didAnyTranslation && sourceLang == null && batch.Count > 1) {
                // maybe it didn't correctly detect source language, re-queue half batches
                for (int i = 0; i < batch.Count; i++) {
                    if (i == batch.Count / 2 + 1) {
                        nextBatchGroupId++;
                    }
                    var b = batch[i];
                    requests.Add(new PendingTranslation(b.text, b.sourceLang, b.targetLanguage, b.callback, nextBatchGroupId));
                }
                nextBatchGroupId++;
                continue;
            }

            for (int i = 0; i < batch.Count; i++) {
                var batchTask = batch[i];
                var translated = translations[i];
                texts[batchTask.text] = translated;
                texts[translated] = translated;
                batchTask.callback.Invoke(true, translated);
            }
            batch.Clear();
            if (_saveCacheTask == null || _saveCacheTask.IsCompleted) {
                _saveCacheTask = Task.Run(() => SaveTranslationCacheDelayed(lang));
            }
            processedTranslations++;
        }
    }

    private static async Task SaveTranslationCacheDelayed(Language lang)
    {
        await Task.Delay(5000);
        try {
            var texts = _translationsCache[lang];
            var cachePath = GetTranslationCacheFilepath(lang);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            using var fs = File.Create(cachePath);
            JsonSerializer.Serialize(fs, texts);
            _saveCacheTask = null;
        } catch (Exception e) {
            // ignore
            Logger.Debug("Failed to save translations cache", e.Message);
        }
    }
}
