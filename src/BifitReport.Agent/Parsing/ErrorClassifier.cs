namespace BifitReport.Agent.Parsing;

public sealed class ErrorClassifier
{
    public ErrorClassification Classify(string logger, string message)
    {
        var source = logger;
        var text = message;

        if (Contains(text, "Редактирование рабочих мест"))
        {
            return new("Настройки рабочего места", IsCritical: false);
        }

        if (ContainsAny(source, ".kkm.", "RRElectro", "fiscal") ||
            ContainsAny(text, "ККТ", "фискал", "регистрац") && ContainsAny(text, "ошиб", "failed", "fail"))
        {
            return new("ККТ/фискализация", IsCritical: true);
        }

        if (ContainsAny(source, ".terminal.", "PaymentTerminal", "payment_terminal") ||
            ContainsAny(text, "терминал", "INPAS", "Сбербанк") && ContainsAny(text, "ошиб", "failed", "fail", "таймаут"))
        {
            return new("Платёжный терминал", IsCritical: true);
        }

        if (ContainsAny(source, "SyncReceiptsTask", "ReceiptSessionService") && ContainsAny(text, "ошиб", "failed", "fail"))
        {
            return new("Синхронизация чеков", IsCritical: true);
        }

        if (ContainsAny(source, "CodeChecking", "chzn") && ContainsAny(text, "ошиб", "failed", "fail"))
        {
            return new("Маркировка", IsCritical: true);
        }

        if (ContainsAny(text, "OutOfMemory", "database is locked", "поврежд", "corrupt"))
        {
            return new("Системная ошибка кассы", IsCritical: true);
        }

        return new("Прочая ошибка", IsCritical: false);
    }

    private static bool Contains(string value, string text) =>
        value.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] values) =>
        values.Any(item => Contains(value, item));
}

public sealed record ErrorClassification(string Category, bool IsCritical);
