using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace PlanetExplorer
{
    public sealed class InteractionLogBuffer : IDisposable
    {
        private readonly ConcurrentQueue<UserInteractionLog> _queue = new();
        private readonly DispatcherTimer _flushTimer = new();
        private readonly Guid _sessionId;
        private bool _isFlushing = false;

        public Guid SessionId => _sessionId;

        public InteractionLogBuffer(int flushEverySeconds = 5)
        {
            _sessionId = Guid.NewGuid();

            _flushTimer.Interval = TimeSpan.FromSeconds(Math.Max(2, flushEverySeconds));
            _flushTimer.Tick += (_, __) => FlushSafe();
            _flushTimer.Start();

            // Ensure we flush when app exits
            Application.Current.Exit += (_, __) => FlushNow();
        }

        public void Log(int planetId, string actionType, double? durationSeconds = null, string? meta = null)
        {
            _queue.Enqueue(new UserInteractionLog
            {
                PlanetId = planetId,
                ActionType = actionType,
                Timestamp = DateTime.Now,
                SessionId = _sessionId,
                DurationSeconds = durationSeconds,
                Meta = meta
            });
        }

        public void FlushNow() => FlushSafe(force: true);

        private void FlushSafe(bool force = false)
        {
            if (_isFlushing) return;

            // Only flush if we have enough items, unless forced
            if (!force && _queue.Count < 5) return;

            _isFlushing = true;

            try
            {
                var batch = new List<UserInteractionLog>(128);

                while (batch.Count < 200 && _queue.TryDequeue(out var item))
                    batch.Add(item);

                if (batch.Count == 0) return;

                using var db = new PlanetContext();
                db.UserInteractionLogs.AddRange(batch);
                db.SaveChanges();
            }
            catch
            {
                // If DB temporarily fails, we don't crash the app
                // (Worst case: those logs are dropped)
            }
            finally
            {
                _isFlushing = false;
            }
        }

        public void Dispose()
        {
            try
            {
                _flushTimer.Stop();
                FlushNow();
            }
            catch { }
        }
    }
}
