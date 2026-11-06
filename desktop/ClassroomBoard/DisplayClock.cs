namespace ClassroomBoard;

/// <summary>调度使用单调时间；校准时间以收到响应的瞬间为锚点，不随系统校时跳动。</summary>
public sealed class DisplayClock
{
    private readonly TimeProvider time;
    private readonly long started;
    private DateTimeOffset anchor, lastWall;
    private double anchorAt, lastElapsed;
    public DisplayClock(TimeProvider time, double cachedOffset)
    {
        this.time = time; started = time.GetTimestamp(); lastWall = time.GetUtcNow();
        anchor = lastWall.AddSeconds(double.IsFinite(cachedOffset) && Math.Abs(cachedOffset) < 365 * 86400 ? cachedOffset : 0);
    }
    public double Elapsed => time.GetElapsedTime(started).TotalSeconds;
    public DateTimeOffset UtcNow => time.GetUtcNow();
    public DateTimeOffset Now => anchor.AddSeconds(Elapsed - anchorAt).ToOffset(TimeSpan.FromHours(8));
    public double Offset => (Now - UtcNow).TotalSeconds;
    public bool ObserveSystemChange()
    {
        double elapsed = Elapsed;
        var wall = UtcNow;
        bool changed = Math.Abs((wall - lastWall).TotalSeconds - (elapsed - lastElapsed)) > 2;
        lastWall = wall; lastElapsed = elapsed;
        return changed;
    }
    public void Synchronize(double serverTime, double receivedAt)
    {
        if (!double.IsFinite(serverTime) || serverTime < 946684800 || serverTime > 4102444800)
            throw new System.IO.InvalidDataException("服务时间无效，保留缓存并暂停本次更新。");
        anchor = DateTimeOffset.FromUnixTimeMilliseconds((long)(serverTime * 1000)); anchorAt = receivedAt;
    }
}
