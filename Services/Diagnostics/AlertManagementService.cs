using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PollSystem.Services.Diagnostics
{
    public class AlertManagementService : IHostedService, IDisposable
    {
        private readonly ILogger<AlertManagementService> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly AlertOptions _options;
        private readonly ConcurrentDictionary<string, AlertHistory> _alertHistory;
        private readonly ConcurrentQueue<Alert> _recentAlerts;
        private readonly ConcurrentDictionary<string, AlertSubscription> _subscriptions;
        private Timer _cleanupTimer;
        private Timer _aggregationTimer;

        public event EventHandler<AlertEventArgs> OnAlertRaised;
        public event EventHandler<AlertStatusChangedEventArgs> OnAlertStatusChanged;

        public AlertManagementService(
            ILogger<AlertManagementService> logger,
            IMetricsCollector metricsCollector,
            IOptions<AlertOptions> options)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _options = options.Value;
            _alertHistory = new ConcurrentDictionary<string, AlertHistory>();
            _recentAlerts = new ConcurrentQueue<Alert>();
            _subscriptions = new ConcurrentDictionary<string, AlertSubscription>();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Cleanup old alerts every hour
            _cleanupTimer = new Timer(
                CleanupOldAlerts,
                null,
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(1));

            // Aggregate alerts every minute
            _aggregationTimer = new Timer(
                AggregateAlerts,
                null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1));

            return Task.CompletedTask;
        }

        public void RaiseAlert(Alert alert)
        {
            try
            {
                // Enrich alert with context
                EnrichAlertContext(alert);

                // Check for alert suppression
                if (ShouldSuppressAlert(alert))
                    return;

                // Record alert
                RecordAlert(alert);

                // Process alert based on severity
                ProcessAlert(alert);

                // Notify subscribers
                NotifySubscribers(alert);

                // Track metrics
                TrackAlertMetrics(alert);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing alert: {AlertType}", alert.Type);
            }
        }

        private void EnrichAlertContext(Alert alert)
        {
            alert.Context ??= new Dictionary<string, object>();
            
            // Add system context
            alert.Context["processId"] = Environment.ProcessId;
            alert.Context["machineName"] = Environment.MachineName;
            alert.Context["timestamp"] = DateTime.UtcNow;
            alert.Context["threadId"] = Environment.CurrentManagedThreadId;

            // Add alert history context
            if (_alertHistory.TryGetValue(alert.Type, out var history))
            {
                alert.Context["previousOccurrences"] = history.OccurrenceCount;
                alert.Context["firstOccurrence"] = history.FirstOccurrence;
                alert.Context["lastOccurrence"] = history.LastOccurrence;
            }
        }

        private bool ShouldSuppressAlert(Alert alert)
        {
            if (!_alertHistory.TryGetValue(alert.Type, out var history))
                return false;

            // Check throttling
            if (DateTime.UtcNow - history.LastOccurrence < _options.AlertThrottleInterval)
                return true;

            // Check for alert storm protection
            if (history.GetRecentCount(TimeSpan.FromMinutes(5)) > _options.MaxAlertsPerInterval)
            {
                _logger.LogWarning("Alert storm detected for {AlertType}", alert.Type);
                return true;
            }

            return false;
        }

        private void RecordAlert(Alert alert)
        {
            // Add to recent alerts queue
            _recentAlerts.Enqueue(alert);
            while (_recentAlerts.Count > _options.MaxRecentAlerts)
            {
                _recentAlerts.TryDequeue(out _);
            }

            // Update alert history
            var history = _alertHistory.GetOrAdd(alert.Type, _ => new AlertHistory());
            history.RecordOccurrence(alert);
        }

        private void ProcessAlert(Alert alert)
        {
            // Log alert based on severity
            var logLevel = alert.Severity switch
            {
                AlertSeverity.Critical => LogLevel.Critical,
                AlertSeverity.Warning => LogLevel.Warning,
                _ => LogLevel.Information
            };

            _logger.Log(logLevel, "Alert: {AlertType} - {Message} ({Severity})",
                alert.Type, alert.Message, alert.Severity);

            // Raise alert event
            OnAlertRaised?.Invoke(this, new AlertEventArgs { Alert = alert });

            // Handle critical alerts
            if (alert.Severity == AlertSeverity.Critical)
            {
                HandleCriticalAlert(alert);
            }
        }

        private void HandleCriticalAlert(Alert alert)
        {
            // Implement critical alert handling logic
            // For example: Send immediate notifications, trigger emergency procedures, etc.
            _logger.LogCritical("Critical alert triggered: {AlertType} - {Message}",
                alert.Type, alert.Message);

            // Track critical alert metrics
            _metricsCollector.RecordMetricWithTags(
                "alerts.critical",
                1,
                new Dictionary<string, object>
                {
                    ["type"] = alert.Type,
                    ["component"] = alert.Component
                });
        }

        private void NotifySubscribers(Alert alert)
        {
            foreach (var subscription in _subscriptions.Values)
            {
                if (subscription.ShouldNotify(alert))
                {
                    try
                    {
                        subscription.NotificationCallback(alert);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error notifying subscriber for alert {AlertType}", alert.Type);
                    }
                }
            }
        }

        private void TrackAlertMetrics(Alert alert)
        {
            _metricsCollector.RecordMetricWithTags(
                "alerts.total",
                1,
                new Dictionary<string, object>
                {
                    ["type"] = alert.Type,
                    ["severity"] = alert.Severity.ToString(),
                    ["component"] = alert.Component
                });

            if (_alertHistory.TryGetValue(alert.Type, out var history))
            {
                _metricsCollector.RecordMetricWithTags(
                    "alerts.frequency",
                    history.GetRecentCount(TimeSpan.FromHours(1)),
                    new Dictionary<string, object>
                    {
                        ["type"] = alert.Type,
                        ["component"] = alert.Component
                    });
            }
        }

        private void AggregateAlerts(object state)
        {
            try
            {
                var aggregations = new Dictionary<string, AlertAggregation>();

                foreach (var alert in _recentAlerts)
                {
                    var key = $"{alert.Component}:{alert.Type}";
                    if (!aggregations.TryGetValue(key, out var aggregation))
                    {
                        aggregation = new AlertAggregation
                        {
                            Component = alert.Component,
                            Type = alert.Type,
                            FirstOccurrence = alert.Timestamp,
                            LastOccurrence = alert.Timestamp,
                            Count = 0,
                            MaxSeverity = AlertSeverity.Info
                        };
                        aggregations[key] = aggregation;
                    }

                    aggregation.Count++;
                    aggregation.LastOccurrence = alert.Timestamp;
                    if (alert.Severity > aggregation.MaxSeverity)
                        aggregation.MaxSeverity = alert.Severity;
                }

                foreach (var aggregation in aggregations.Values)
                {
                    _metricsCollector.RecordMetricWithTags(
                        "alerts.aggregated",
                        aggregation.Count,
                        new Dictionary<string, object>
                        {
                            ["component"] = aggregation.Component,
                            ["type"] = aggregation.Type,
                            ["severity"] = aggregation.MaxSeverity.ToString()
                        });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error aggregating alerts");
            }
        }

        private void CleanupOldAlerts(object state)
        {
            try
            {
                var cutoff = DateTime.UtcNow - _options.AlertRetentionPeriod;

                foreach (var history in _alertHistory.Values)
                {
                    history.RemoveOlderThan(cutoff);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up old alerts");
            }
        }

        public IDisposable Subscribe(string subscriptionId, AlertSubscriptionOptions options, Action<Alert> callback)
        {
            var subscription = new AlertSubscription(subscriptionId, options, callback);
            _subscriptions[subscriptionId] = subscription;
            return new SubscriptionHandle(this, subscriptionId);
        }

        private void Unsubscribe(string subscriptionId)
        {
            _subscriptions.TryRemove(subscriptionId, out _);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cleanupTimer?.Change(Timeout.Infinite, 0);
            _aggregationTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _aggregationTimer?.Dispose();
        }

        private class SubscriptionHandle : IDisposable
        {
            private readonly AlertManagementService _service;
            private readonly string _subscriptionId;

            public SubscriptionHandle(AlertManagementService service, string subscriptionId)
            {
                _service = service;
                _subscriptionId = subscriptionId;
            }

            public void Dispose()
            {
                _service.Unsubscribe(_subscriptionId);
            }
        }
    }

    public class AlertOptions
    {
        public TimeSpan AlertThrottleInterval { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan AlertRetentionPeriod { get; set; } = TimeSpan.FromDays(30);
        public int MaxRecentAlerts { get; set; } = 1000;
        public int MaxAlertsPerInterval { get; set; } = 100;
    }

    public class Alert
    {
        public string Type { get; set; }
        public string Component { get; set; }
        public string Message { get; set; }
        public AlertSeverity Severity { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Context { get; set; }
        public string CorrelationId { get; set; }
    }

    public class AlertHistory
    {
        private readonly ConcurrentQueue<AlertOccurrence> _occurrences;
        private long _totalCount;

        public DateTime FirstOccurrence { get; private set; }
        public DateTime LastOccurrence { get; private set; }
        public long OccurrenceCount => _totalCount;

        public AlertHistory()
        {
            _occurrences = new ConcurrentQueue<AlertOccurrence>();
            FirstOccurrence = DateTime.UtcNow;
            LastOccurrence = DateTime.UtcNow;
        }

        public void RecordOccurrence(Alert alert)
        {
            var occurrence = new AlertOccurrence
            {
                Timestamp = alert.Timestamp,
                Severity = alert.Severity,
                Context = alert.Context
            };

            _occurrences.Enqueue(occurrence);
            LastOccurrence = alert.Timestamp;
            Interlocked.Increment(ref _totalCount);
        }

        public int GetRecentCount(TimeSpan duration)
        {
            var cutoff = DateTime.UtcNow - duration;
            return _occurrences.Count(o => o.Timestamp >= cutoff);
        }

        public void RemoveOlderThan(DateTime cutoff)
        {
            while (_occurrences.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
            {
                _occurrences.TryDequeue(out _);
            }
        }
    }

    public class AlertOccurrence
    {
        public DateTime Timestamp { get; set; }
        public AlertSeverity Severity { get; set; }
        public Dictionary<string, object> Context { get; set; }
    }

    public class AlertAggregation
    {
        public string Component { get; set; }
        public string Type { get; set; }
        public DateTime FirstOccurrence { get; set; }
        public DateTime LastOccurrence { get; set; }
        public int Count { get; set; }
        public AlertSeverity MaxSeverity { get; set; }
    }

    public class AlertSubscriptionOptions
    {
        public AlertSeverity MinimumSeverity { get; set; } = AlertSeverity.Warning;
        public List<string> ComponentFilter { get; set; }
        public List<string> TypeFilter { get; set; }
        public bool IncludeContext { get; set; } = true;
    }

    public class AlertSubscription
    {
        public string Id { get; }
        private readonly AlertSubscriptionOptions _options;
        private readonly Action<Alert> _callback;

        public AlertSubscription(string id, AlertSubscriptionOptions options, Action<Alert> callback)
        {
            Id = id;
            _options = options;
            _callback = callback;
        }

        public bool ShouldNotify(Alert alert)
        {
            if (alert.Severity < _options.MinimumSeverity)
                return false;

            if (_options.ComponentFilter?.Any() == true && 
                !_options.ComponentFilter.Contains(alert.Component))
                return false;

            if (_options.TypeFilter?.Any() == true && 
                !_options.TypeFilter.Contains(alert.Type))
                return false;

            return true;
        }

        public void NotificationCallback(Alert alert)
        {
            if (!_options.IncludeContext)
            {
                alert = new Alert
                {
                    Type = alert.Type,
                    Component = alert.Component,
                    Message = alert.Message,
                    Severity = alert.Severity,
                    Timestamp = alert.Timestamp
                };
            }

            _callback(alert);
        }
    }

    public class AlertEventArgs : EventArgs
    {
        public Alert Alert { get; set; }
    }

    public class AlertStatusChangedEventArgs : EventArgs
    {
        public string AlertType { get; set; }
        public AlertSeverity OldSeverity { get; set; }
        public AlertSeverity NewSeverity { get; set; }
        public DateTime Timestamp { get; set; }
    }
} 