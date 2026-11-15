using BifitReport.Agent.Parsing;

namespace BifitReport.Agent.Tests;

public sealed class ErrorClassifierTests
{
    private readonly ErrorClassifier _classifier = new();

    [Fact]
    public void PermissionNoiseIsNotCritical()
    {
        var result = _classifier.Classify(
            "com.bifit.cashdesk.desktop.utils.WorkplaceUtils",
            "Ошибка при отправке значения настройки рабочего места на сервер: У вас нет права \"Редактирование рабочих мест\" для выполнения этой операции");

        Assert.Equal("Настройки рабочего места", result.Category);
        Assert.False(result.IsCritical);
    }

    [Fact]
    public void DatabaseErrorIsCritical()
    {
        var result = _classifier.Classify(
            "com.bifit.cashdesk.desktop.db.ReceiptRecords",
            "DB error");

        Assert.Equal("База данных кассы", result.Category);
        Assert.True(result.IsCritical);

        var localCode = _classifier.Classify(
            "com.bifit.cashdesk.desktop.retail.view.fragment.main.ReceiptHistoryFragment",
            "error_db");

        Assert.True(localCode.IsCritical);
    }

    [Fact]
    public void FatalExceptionIsCritical()
    {
        var result = _classifier.Classify(
            "com.bifit.cashdesk.desktop.core.handler.FatalExceptionHandler",
            "Непредвиденная ошибка");

        Assert.Equal("Критический сбой кассы", result.Category);
        Assert.True(result.IsCritical);
    }

    [Fact]
    public void MarkingPluginInitializationErrorIsCritical()
    {
        var result = _classifier.Classify(
            "com.bifit.cashdesk.desktop.plugin.tspiot.TSPIoTExtension",
            "Ошибка инициализации плагина ТСПИоТ");

        Assert.Equal("Плагин кассы", result.Category);
        Assert.True(result.IsCritical);
    }

    [Fact]
    public void KkmDriverErrorIsCritical()
    {
        var result = _classifier.Classify(
            "com.bifit.cashdesk.desktop.plugin.kkm.rr_electro.RRElectro",
            "error");

        Assert.Equal("ККТ/фискализация", result.Category);
        Assert.True(result.IsCritical);
    }

    [Fact]
    public void TransientNetworkRequestIsNotCritical()
    {
        var result = _classifier.Classify(
            "com.bifit.cashdesk.desktop.service.RequestService",
            "request_error");

        Assert.Equal("Прочая ошибка", result.Category);
        Assert.False(result.IsCritical);
    }
}
