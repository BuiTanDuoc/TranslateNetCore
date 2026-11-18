using System.Collections.Concurrent;
using NetTranslate.Application.Abstractions;

namespace NetTranslate.Infrastructure.RateLimiting;

/// <summary>
/// Rate limiter cửa sổ trượt 60 giây, lưu trong bộ nhớ (một instance server).
/// Chạy nhiều instance sau load balancer → thay bằng implementation dùng Redis.
/// </summary>
public sealed class SlidingWindowRateLimiter : IRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Queue<long>> _buckets = new();
    private long _lastSweep = Environment.TickCount64;

    public bool TryAcquire(string bucket, int limitPerMinute)
    {
        if (limitPerMinute <= 0) return true;

        var now = Environment.TickCount64;
        var windowStart = now - (long)Window.TotalMilliseconds;
        var queue = _buckets.GetOrAdd(bucket, _ => new Queue<long>());

        bool allowed;
        lock (queue)
        {
            while (queue.Count > 0 && queue.Peek() <= windowStart) queue.Dequeue();
            allowed = queue.Count < limitPerMinute;
            if (allowed) queue.Enqueue(now);
        }

        SweepIfNeeded(now, windowStart);
        return allowed;
    }

    /// <summary>Dọn các bucket rỗng mỗi 5 phút để không rò rỉ bộ nhớ khi có nhiều IP.</summary>
    private void SweepIfNeeded(long now, long windowStart)
    {
        var last = Interlocked.Read(ref _lastSweep);
        if (now - last < 5 * 60_000 || Interlocked.CompareExchange(ref _lastSweep, now, last) != last) return;

        foreach (var (key, queue) in _buckets)
        {
            lock (queue)
            {
                while (queue.Count > 0 && queue.Peek() <= windowStart) queue.Dequeue();
                if (queue.Count == 0) _buckets.TryRemove(key, out _);
            }
        }
    }
}
