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

    public static AppConfig.ComputedSetting<int, string> DateOnlyFormat { get; }
    public static AppConfig.ComputedSetting2<int, bool, string> DateTimeFormat { get; }

    static Time()
    {
        DateOnlyFormat = AppConfig.Computed(
            AppConfig.Instance.DateFormat,
            (fmt) => DateFormats[Math.Clamp(fmt, 0, DateFormats.Length - 1)]);

        DateTimeFormat = AppConfig.Computed(
            AppConfig.Instance.DateFormat,
            AppConfig.Instance.ClockFormat,
            (fmt, clock) => DateFormats[Math.Clamp(fmt, 0, DateFormats.Length - 1)] + (clock ? " hh:mm tt" : " HH:mm"));
    }

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
