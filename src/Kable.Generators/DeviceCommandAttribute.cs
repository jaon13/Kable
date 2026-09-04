namespace Kable.Generators;

using System;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DeviceCommandAttribute : Attribute
{
    public string Template { get; }
    public bool IsUrgent { get; set; }

    public DeviceCommandAttribute(string template)
    {
        Template = template;
    }
}

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class SpontaneousEventAttribute : Attribute
{
    public string Pattern { get; }

    public SpontaneousEventAttribute(string pattern)
    {
        Pattern = pattern;
    }
}

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TelemetryEventAttribute : Attribute
{
    public string Pattern { get; }

    public TelemetryEventAttribute(string pattern)
    {
        Pattern = pattern;
    }
}

[AttributeUsage(AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class DeviceRpcContractAttribute : Attribute
{
    public byte Delimiter { get; set; } = 0x0A;
    public bool SupportsCorrelationId { get; set; } = false;

    public DeviceRpcContractAttribute() { }
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GeneratedProtocolCodecAttribute : Attribute
{
    public byte Delimiter { get; set; } = 0x0A;
    public bool SupportsCorrelationId { get; set; } = false;

    public GeneratedProtocolCodecAttribute() { }
}
