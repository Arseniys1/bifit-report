namespace BifitReport.LogViewer.Logs;

public abstract class LogServiceException(string message) : Exception(message)
{
}

public sealed class LogResourceNotFoundException(string message) : LogServiceException(message)
{
}

public sealed class LogRequestException(string message) : LogServiceException(message)
{
}

public sealed class LogLimitExceededException(string message) : LogServiceException(message)
{
}
