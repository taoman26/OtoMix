using OtoMix.Models;
using OtoMix.Services;

namespace OtoMix.Tests;

public class PactlParserTests
{
    // WSLg実機の出力を元にした例 + 実機に無いUSBマイク/日本語説明
    private const string Json = """
    [{"index":1,"name":"RDPSink.monitor","description":"Monitor of RDP Sink"},
     {"index":2,"name":"RDPSource","description":"RDP Source"},
     {"index":3,"name":"alsa_input.usb-Blue_Yeti-00.analog-stereo","description":"内蔵オーディオ アナログステレオ"},
     {"index":4,"name":"alsa_output.pci.hdmi-stereo.monitor","description":"Monitor of HDMI"}]
    """;

    [Fact]
    public void Json_SplitsByMonitorSuffix()
    {
        var r = PactlParser.ParseJson(Json);
        Assert.Equal(new[] { "RDPSource", "alsa_input.usb-Blue_Yeti-00.analog-stereo" }, r.Microphones.Select(d => d.Id));
        Assert.Equal(new[] { "RDPSink.monitor", "alsa_output.pci.hdmi-stereo.monitor" }, r.Loopbacks.Select(d => d.Id));
        Assert.All(r.Loopbacks, d => Assert.Equal(DeviceKind.Loopback, d.Kind));
        Assert.Equal("内蔵オーディオ アナログステレオ", r.Microphones[1].DisplayName);
    }

    [Fact]
    public void Short_Fallback()
    {
        var r = PactlParser.ParseShort("1\tRDPSink.monitor\tmod\ts16le\tSUSPENDED\n2\tRDPSource\tmod\ts16le\tSUSPENDED\n");
        Assert.Single(r.Microphones);
        Assert.Single(r.Loopbacks);
    }

    [Fact]
    public async Task Live_Pactl_ReturnsDevices_WhenAvailable()
    {
        var r = await new LinuxDeviceProvider().GetDevicesAsync();
        Console.WriteLine($"mics={string.Join(",", r.Microphones)} loops={string.Join(",", r.Loopbacks)}");
        Assert.NotEmpty(r.Microphones.Concat(r.Loopbacks));
    }

    [Fact]
    public void ParseJson_NullDescriptionFromPulse1599_FallsBackToName()
    {
        const string json = """[{"name":"alsa_output.pci-0000_00_1f.3.analog-stereo.monitor","description":"(null)"},{"name":"alsa_input.pci-0000_00_1f.3.analog-stereo","description":"(null)"}]""";
        var list = PactlParser.ParseJson(json);
        Assert.Equal("alsa_input.pci-0000_00_1f.3.analog-stereo", Assert.Single(list.Microphones).DisplayName);
        Assert.Equal("alsa_output.pci-0000_00_1f.3.analog-stereo.monitor", Assert.Single(list.Loopbacks).DisplayName);
    }
}
