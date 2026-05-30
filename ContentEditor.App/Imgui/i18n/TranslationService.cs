using ContentEditor.BackgroundTasks;
using ContentEditor.Core;
using ReeLib.Msg;

namespace ContentEditor.App;

public static class TranslationService
{
    public delegate void TranslationCallback(bool success, string? message);

    public static void QueueTranslation(string sourceText, Language targetLanguage, TranslationCallback callback, Language? sourceLanguage = null)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) {
            callback(true, sourceText);
            return;
        }
        if (AsyncTranslationTask.TryGetCachedTranslation(targetLanguage, sourceText, out var translated)) {
            callback.Invoke(true, translated);
            return;
        }

        var task = GetTranslationTask();

        if (sourceLanguage == null && targetLanguage == Language.English) {
            // game files are generally either japanese or english, so we're very likely doing jp->en in such a case
            sourceLanguage = Language.Japanese;
        }
        task.Translate(sourceText, sourceLanguage, targetLanguage, callback);
    }

    public static void QueueTranslation(TranslatableBase sourceText, Language targetLanguage, TranslationCallback callback, Language? sourceLanguage = null)
    {
        QueueTranslation(sourceText.String, targetLanguage, callback, sourceLanguage);
    }

    private static AsyncTranslationTask GetTranslationTask()
    {
        var tlTask = MainLoop.Instance.BackgroundTasks.GetPendingTask<AsyncTranslationTask>();
        if (tlTask == null) {
            MainLoop.Instance.BackgroundTasks.Queue(tlTask = new AsyncTranslationTask());
        }
        return tlTask;
    }
}