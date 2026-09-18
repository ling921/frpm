namespace Frpm.Infrastructure.Runtime;

[SingletonService]
public sealed class RunLogBuffer
{
    private const int MaxBufferedLines = 2_000;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = [];

    internal void Start(Guid runId)
    {
        lock (_gate)
        {
            _entries[runId] = new Entry();
        }
    }

    internal void Append(Guid runId, string line)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(runId, out var entry))
            {
                return;
            }

            entry.Lines.Enqueue(line);
            entry.NextIndex++;
            if (entry.Lines.Count > MaxBufferedLines)
            {
                entry.Lines.Dequeue();
                entry.FirstIndex++;
            }
        }
    }

    internal void Complete(Guid runId)
    {
        lock (_gate)
        {
            _entries.Remove(runId);
        }
    }

    internal bool TryRead(Guid runId, long cursor, int maxLines, out LogBufferTail tail)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(runId, out var entry) || cursor < entry.FirstIndex)
            {
                tail = default;
                return false;
            }

            var skip = Math.Clamp(cursor - entry.FirstIndex, 0, entry.Lines.Count);
            var lines = entry.Lines.Skip((int)skip).Take(maxLines).ToList();
            tail = new(cursor + lines.Count, lines);
            return true;
        }
    }

    private sealed class Entry
    {
        public Queue<string> Lines { get; } = new();
        public long FirstIndex { get; set; }
        public long NextIndex { get; set; }
    }
}

internal readonly record struct LogBufferTail(long NextCursor, IReadOnlyList<string> Lines);
