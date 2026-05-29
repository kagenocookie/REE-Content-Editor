using ContentEditor.BackgroundTasks;
using ContentEditor.Core;
using ReeLib.Msg;

namespace ContentEditor.App;

public class TranslationService
{
    public delegate void TranslationCallback(bool success, string? message);

    public static void QueueTranslation(string sourceText, Language targetLanguage, TranslationCallback callback)
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
        task.Translate(sourceText, targetLanguage, callback);
    }

    public static void QueueTranslation(TranslatableBase sourceText, Language targetLanguage, TranslationCallback callback)
    {
        QueueTranslation(sourceText.String, targetLanguage, callback);
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