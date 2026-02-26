namespace ContentEditor.App;

public class Time
{
    public static float Delta => _deltaTime;
    public static float Elapsed => _totalTime;

    private static float _deltaTime;
    private static float _totalTime;

    private static Time mainTime = new();
    private static Time uiTime = new();

    private const float MaxDeltaTime = 1f;
    private Thread mainThread;

    internal Time() { mainThread = Thread.CurrentThread; }

    private static readonly string[] DateFormats =
    [
        "dd/MM/yyyy",
        "MM/dd/yyyy",
        "yyyy/MM/dd"
    ];
    public static string dateFormat => DateFormats[Math.Clamp(AppConfig.Instance.DateFormat.Get(), 0, DateFormats.Length - 1)];
    internal void Init()
    {

    }

    internal void Update(float deltaTime, bool isMainThread)
    {
        if (isMainThread) {
            _deltaTime = MathF.Min(MaxDeltaTime, deltaTime);
            _totalTime += deltaTime;
        } else {
            _deltaTime = MathF.Min(MaxDeltaTime, deltaTime);
            _totalTime += deltaTime;
        }
    }
}
