namespace BrowserSkills.Core;

/// <summary>
/// 日志服务接口 —— 由宿主应用实现，BrowserSkills 通过此接口输出日志。
/// </summary>
public interface ILogger
{
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message);
    void Exception(string context, Exception ex);
    IDisposable? Trace(string signature);
}

/// <summary>
/// 空日志实现 —— 用于无日志场景。
/// </summary>
public class NullLogger : ILogger
{
    public static NullLogger Instance { get; } = new();
    private NullLogger() { }
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message) { }
    public void Exception(string context, Exception ex) { }
    public IDisposable? Trace(string signature) => null;
}
