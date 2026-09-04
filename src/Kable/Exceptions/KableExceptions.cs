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
        : base($"장비 명령 '{command}'이(가) 타임아웃({timeout.TotalSeconds:F1}s) 내에 응답하지 않았습니다.")
    {
        Command = command;
        Timeout = timeout;
    }
}
