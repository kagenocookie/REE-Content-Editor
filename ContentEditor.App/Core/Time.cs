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