using System.Collections.Concurrent;

namespace PhotoViewer.Api.Services;

/// <summary>
/// In-memory rate limiter for login attempts — 5 attempts per minute per IP.
/// </summary>
public class LoginRateLimiter
{
    private readonly ConcurrentDictionary<string, List<DateTime>> _attempts = new();
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public bool IsBlocked(string ipAddress)
    {
        CleanupOld(ipAddress);
        if (_attempts.TryGetValue(ipAddress, out var list))
        {
            return list.Count >= MaxAttempts;
        }
        return false;
    }

    public void RecordAttempt(string ipAddress)
    {
        var list = _attempts.GetOrAdd(ipAddress, _ => new List<DateTime>());
        lock (list)
        {
            list.Add(DateTime.UtcNow);
        }
    }

    public void ClearAttempts(string ipAddress)
    {
        _attempts.TryRemove(ipAddress, out _);
    }

    private void CleanupOld(string ipAddress)
    {
        if (_attempts.TryGetValue(ipAddress, out var list))
        {
            var cutoff = DateTime.UtcNow - Window;
            lock (list)
            {
                list.RemoveAll(t => t < cutoff);
            }
        }
    }
}
