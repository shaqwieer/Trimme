namespace Trimme.ArchitectureTests.Probe.Application;

/// <summary>A "handler" with final message text in code: exactly what R-NEG-09 must reject.</summary>
internal static class ProbeHardcodedMessage
{
    public static string Render(string name) => "مرحباً " + name + "، تم تأكيد حجزك {{shop_name}}";
}
