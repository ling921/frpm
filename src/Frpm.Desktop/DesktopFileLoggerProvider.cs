using Microsoft.Extensions.Logging;

namespace Frpm.Desktop;

internal sealed class DesktopFileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly string _directory = directory;
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }

    private sealed class FileLogger(DesktopFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            try
            {
                lock (owner._gate)
                {
                    Directory.CreateDirectory(owner._directory);
                    File.AppendAllText(Path.Combine(owner._directory, $"desktop-{DateTime.Now:yyyy-MM-dd}.log"),
                        $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {category}: {formatter(state, exception)}{Environment.NewLine}{exception}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
