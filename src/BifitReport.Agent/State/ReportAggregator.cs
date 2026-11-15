using BifitReport.Agent.Configuration;
using BifitReport.Agent.Models;
using BifitReport.Agent.Queue;
using BifitReport.Agent.Reporting;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.State;

public sealed class ReportAggregator(
    IOptions<AgentOptions> agentOptions,
    IOptions<ReportOptions> reportOptions,
    ReportStateStore stateStore,
    ReportFormatter formatter,
    DiskMessageQueue queue,
    ILogger<ReportAggregator> logger)
{
    /// <summary>Значение идентификатора смены, когда агент не видел её открытия.</summary>
    private const string UnknownShiftId = "не определена";

    private readonly AgentOptions _agentOptions = agentOptions.Value;
    private readonly ReportOptions _reportOptions = reportOptions.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ReportState? _state;
    private HashSet<string>? _recentEventIds;

    public async Task HandleAsync(BifitEvent bifitEvent, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (!_recentEventIds!.Add(bifitEvent.Id))
            {
                await stateStore.SaveAsync(_state!, cancellationToken);
                return;
            }
            _state!.RecentEventIds.Add(bifitEvent.Id);
            TrimRecentEvents();

            switch (bifitEvent.Type)
            {
                case BifitEventType.ShiftOpened:
                    await HandleShiftOpenedAsync(bifitEvent, cancellationToken);
                    break;
                case BifitEventType.ShiftClosed:
                    await HandleShiftClosedAsync(bifitEvent, cancellationToken);
                    break;
                case BifitEventType.ReceiptRegistered:
                    EnsureCurrentShift(bifitEvent.Timestamp).RegisteredReceiptIds.Add(bifitEvent.ReceiptId!);
                    break;
                case BifitEventType.ReceiptDeleted:
                    EnsureCurrentShift(bifitEvent.Timestamp).DeletedReceiptIds.Add(bifitEvent.ReceiptId!);
                    break;
                case BifitEventType.TerminalPaymentStarted:
                    HandlePaymentStarted(bifitEvent);
                    break;
                case BifitEventType.TerminalPaymentCompleted:
                    await HandlePaymentCompletedAsync(bifitEvent, cancellationToken);
                    break;
                case BifitEventType.ApplicationError:
                    await HandleApplicationErrorAsync(bifitEvent, cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(bifitEvent), bifitEvent.Type, "Неизвестный тип события.");
            }

            await stateStore.SaveAsync(_state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task TrySendPeriodicSummaryAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!_reportOptions.PeriodicSummaryEnabled) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (_state!.CurrentShift is null) return;

            var interval = TimeSpan.FromMinutes(_reportOptions.PeriodicSummaryMinutes);
            var last = _state.LastPeriodicSummaryAt ?? _state.CurrentShift.OpenedAt;
            if (now - last < interval) return;

            var bucket = now.ToUnixTimeSeconds() / (long)interval.TotalSeconds;
            await queue.EnqueueAsync(
                formatter.FormatPeriodicSummary(_state.CurrentShift, now),
                $"periodic:{_state.CurrentShift.InternalId}:{bucket}",
                cancellationToken);
            _state.LastPeriodicSummaryAt = now;
            await stateStore.SaveAsync(_state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task HandleShiftOpenedAsync(BifitEvent bifitEvent, CancellationToken cancellationToken)
    {
        var internalId = bifitEvent.ShiftInternalId ?? UnknownShiftId;
        var current = _state!.CurrentShift;

        if (current is not null && string.Equals(current.InternalId, internalId, StringComparison.Ordinal))
        {
            current.ExternalId = bifitEvent.ShiftExternalId ?? current.ExternalId;
            current.OpenedAt = bifitEvent.ShiftOpenedAt ?? current.OpenedAt;
            current.IsInferred = false;
            return;
        }

        if (current is not null && IsSameUnobservedShift(current, bifitEvent))
        {
            // Агент начал работать в середине смены: открытие он не видел, но счётчики
            // уже накоплены. Принимаем идентификаторы настоящей смены вместо того,
            // чтобы обнулить их «перехватом» новой.
            current.InternalId = internalId;
            current.ExternalId = bifitEvent.ShiftExternalId ?? current.ExternalId;
            current.OpenedAt = bifitEvent.ShiftOpenedAt ?? current.OpenedAt;
            return;
        }

        if (current is not null)
        {
            await queue.EnqueueAsync(
                formatter.FormatInterruptedShift(current, bifitEvent.Timestamp),
                $"shift-interrupted:{current.InternalId}:{internalId}",
                cancellationToken);
        }

        _state.CurrentShift = new ShiftReportState
        {
            InternalId = internalId,
            ExternalId = bifitEvent.ShiftExternalId ?? UnknownShiftId,
            OpenedAt = bifitEvent.ShiftOpenedAt ?? bifitEvent.Timestamp
        };
        _state.LastPeriodicSummaryAt = null;

        if (_reportOptions.SendShiftOpened)
        {
            await queue.EnqueueAsync(
                formatter.FormatShiftOpened(_state.CurrentShift),
                $"shift-open:{internalId}:{_state.CurrentShift.OpenedAt:O}",
                cancellationToken);
        }
    }

    private async Task HandleShiftClosedAsync(BifitEvent bifitEvent, CancellationToken cancellationToken)
    {
        var closedAt = bifitEvent.ShiftClosedAt ?? bifitEvent.Timestamp;
        var current = _state!.CurrentShift;
        var shouldClearCurrentShift = false;
        ShiftReportState shift;

        if (current is not null && string.Equals(current.InternalId, bifitEvent.ShiftInternalId, StringComparison.Ordinal))
        {
            shift = current;
            shouldClearCurrentShift = true;
        }
        else if (current is not null && IsUnknownShiftId(current.InternalId) && closedAt >= current.OpenedAt)
        {
            // Смена была «выведена» из операций и закрывается впервые: сохраняем
            // её счётчики, принимая идентификаторы из строки закрытия.
            current.InternalId = bifitEvent.ShiftInternalId ?? current.InternalId;
            current.ExternalId = bifitEvent.ShiftExternalId ?? current.ExternalId;
            current.OpenedAt = bifitEvent.ShiftOpenedAt ?? current.OpenedAt;
            current.IsInferred = true;
            shift = current;
            shouldClearCurrentShift = true;
        }
        else
        {
            // Закрывается другая смена (например, запоздалая синхронизация уже
            // закрытой сессии): отчёт по ней отправляем, но текущую смену не трогаем.
            shift = new ShiftReportState
            {
                InternalId = bifitEvent.ShiftInternalId ?? UnknownShiftId,
                ExternalId = bifitEvent.ShiftExternalId ?? UnknownShiftId,
                OpenedAt = bifitEvent.ShiftOpenedAt ?? bifitEvent.Timestamp,
                IsInferred = true
            };
        }

        shift.ClosedAt = closedAt;
        if (_reportOptions.SendShiftClosed)
        {
            await queue.EnqueueAsync(
                formatter.FormatShiftClosed(shift),
                $"shift-close:{shift.InternalId}:{shift.ClosedAt:O}",
                cancellationToken);
        }

        if (shouldClearCurrentShift)
        {
            _state.CurrentShift = null;
            _state.LastPeriodicSummaryAt = null;
        }
    }

    private void HandlePaymentStarted(BifitEvent bifitEvent)
    {
        var shift = EnsureCurrentShift(bifitEvent.Timestamp);
        shift.PendingTerminalAmount = bifitEvent.Amount;
        shift.PendingTerminalStartedAt = bifitEvent.Timestamp;
    }

    private async Task HandlePaymentCompletedAsync(BifitEvent bifitEvent, CancellationToken cancellationToken)
    {
        var shift = EnsureCurrentShift(bifitEvent.Timestamp);
        var pendingIsFresh = shift.PendingTerminalStartedAt is not null &&
                             bifitEvent.Timestamp - shift.PendingTerminalStartedAt <= TimeSpan.FromMinutes(_reportOptions.PendingPaymentLifetimeMinutes);

        if (bifitEvent.Success == true)
        {
            shift.TerminalSuccessCount++;
            if (pendingIsFresh && shift.PendingTerminalAmount is not null)
            {
                shift.RecognizedTerminalSuccessAmount += shift.PendingTerminalAmount.Value;
            }
        }
        else
        {
            shift.TerminalFailureCount++;
            await TrackErrorAsync(bifitEvent, shift, cancellationToken);
        }

        shift.PendingTerminalAmount = null;
        shift.PendingTerminalStartedAt = null;
    }

    private async Task HandleApplicationErrorAsync(BifitEvent bifitEvent, CancellationToken cancellationToken)
    {
        await TrackErrorAsync(bifitEvent, _state!.CurrentShift, cancellationToken);
    }

    private async Task TrackErrorAsync(BifitEvent bifitEvent, ShiftReportState? shift, CancellationToken cancellationToken)
    {
        var category = bifitEvent.ErrorCategory ?? "Прочая ошибка";
        if (shift is not null)
        {
            shift.ErrorCount++;
            shift.ErrorCategories[category] = shift.ErrorCategories.GetValueOrDefault(category) + 1;
        }

        if (!bifitEvent.IsCritical) return;

        if (shift is not null) shift.CriticalErrorCount++;
        if (!_reportOptions.SendImmediateCriticalErrors) return;

        var cooldown = TimeSpan.FromMinutes(_reportOptions.CriticalErrorCooldownMinutes);
        if (_state!.LastCriticalNotificationByCategory.TryGetValue(category, out var lastNotification) &&
            bifitEvent.Timestamp - lastNotification < cooldown)
        {
            return;
        }

        await queue.EnqueueAsync(
            formatter.FormatCriticalError(bifitEvent),
            $"critical:{category}:{bifitEvent.Id}",
            cancellationToken);
        _state.LastCriticalNotificationByCategory[category] = bifitEvent.Timestamp;
    }

    private ShiftReportState EnsureCurrentShift(DateTimeOffset timestamp)
    {
        _state!.CurrentShift ??= new ShiftReportState
        {
            InternalId = UnknownShiftId,
            ExternalId = UnknownShiftId,
            OpenedAt = timestamp,
            IsInferred = true
        };
        return _state.CurrentShift;
    }

    private static bool IsUnknownShiftId(string? internalId) =>
        string.IsNullOrEmpty(internalId) || string.Equals(internalId, UnknownShiftId, StringComparison.Ordinal);

    /// <summary>
    /// Проверяет, что событие открытия относится к той же смене, открытие которой агент
    /// не наблюдал: смена выведена из операций, а время открытия в журнале не позже
    /// времени первой наблюдённой операции.
    /// </summary>
    private static bool IsSameUnobservedShift(ShiftReportState current, BifitEvent bifitEvent)
    {
        if (!IsUnknownShiftId(current.InternalId)) return false;
        if (bifitEvent.ShiftOpenedAt is null) return true;
        return bifitEvent.ShiftOpenedAt <= current.OpenedAt;
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_state is not null) return;
        _state = await stateStore.LoadAsync(cancellationToken);
        _recentEventIds = new HashSet<string>(_state.RecentEventIds, StringComparer.Ordinal);
        logger.LogInformation("Состояние отчёта загружено: {EventCount} последних событий.", _recentEventIds.Count);
    }

    private void TrimRecentEvents()
    {
        while (_state!.RecentEventIds.Count > _agentOptions.RecentEventCapacity)
        {
            var oldest = _state.RecentEventIds[0];
            _state.RecentEventIds.RemoveAt(0);
            _recentEventIds!.Remove(oldest);
        }
    }
}
