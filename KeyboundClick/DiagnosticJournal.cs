using System.Text.Json;
using System.Threading.Channels;

namespace KeyboundClick;

internal sealed class DiagnosticJournal : IDisposable
{
    private readonly Channel<string> entries = Channel.CreateUnbounded<string>();
    private readonly Task writerTask;

    internal DiagnosticJournal(string configDirectory)
    {
        DirectoryPath = Path.Combine(configDirectory, "diagnostics");
        Directory.CreateDirectory(DirectoryPath);
        FilePath = Path.Combine(DirectoryPath, $"session-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.jsonl");
        writerTask = Task.Run(WriteEntriesAsync);
    }

    internal string DirectoryPath { get; }
    internal string FilePath { get; }
    internal string? Error { get; private set; }

    internal void Record(string kind, object data)
    {
        if (Error != null) return;
        var entry = JsonSerializer.Serialize(new
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = kind,
            Data = data,
        });
        entries.Writer.TryWrite(entry);
    }

    public void Dispose()
    {
        entries.Writer.TryComplete();
        if (!writerTask.Wait(TimeSpan.FromSeconds(2))) Error = "诊断写入任务未在卸载时限内结束";
    }

    private async Task WriteEntriesAsync()
    {
        try
        {
            await using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream);
            await foreach (var entry in entries.Reader.ReadAllAsync()) await writer.WriteLineAsync(entry);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
}
