namespace Kable.Exceptions;

using System;

public class DeviceDisconnectedException : Exception
{
    public DeviceDisconnectedException(string message) : base(message) { }
    public DeviceDisconnectedException(string message, Exception innerException) : base(message, innerException) { }
}

public class DeviceTimeoutException : TimeoutException
{
    public string Command { get; }
    public TimeSpan Timeout { get; }

    public DeviceTimeoutException(string command, TimeSpan timeout)
        : base($"Device command '{command}' timed out after {timeout.TotalSeconds:F1}s.")
    {
        Command = command;
        Timeout = timeout;
    }
}

public class ProtocolViolationException : Exception
{
    public ProtocolViolationException(string message) : base(message) { }
    public ProtocolViolationException(string message, Exception innerException) : base(message, innerException) { }
}
