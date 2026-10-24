namespace OtoMix.Models;

public enum DeviceKind { Microphone, Loopback }

public sealed record AudioDevice(string Id, string DisplayName, DeviceKind Kind)
{
    public override string ToString() => DisplayName;
}
