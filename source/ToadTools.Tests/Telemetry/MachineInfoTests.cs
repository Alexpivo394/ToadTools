using System.Text.Json.Nodes;
using Shouldly;
using ToadTools.Telemetry;

namespace ToadTools.Tests.Telemetry;

public class MachineInfoTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);

    private sealed class Broken : IMachineSource
    {
        public string Name() => throw new InvalidOperationException();
        public string? Cpu() => throw new UnauthorizedAccessException();
        public int? Cores() => throw new InvalidOperationException();
        public long? RamMb() => throw new InvalidOperationException();
        public (string? Name, long? MemoryMb) Gpu() => throw new System.Security.SecurityException();
        public string DiskKind() => throw new System.ComponentModel.Win32Exception();
        public (long? TotalGb, long? FreeGb) Disk() => throw new System.IO.IOException();
        public string? Os() => throw new InvalidOperationException();
        public int[] RevitYears() => throw new System.IO.DirectoryNotFoundException();
    }

    [Test]
    public void Read_OnThisMachine_HasNameCoresRam()
    {
        var machine = MachineInfo.Read("4.6.0", () => Now);

        ((string)machine["name"]!).ShouldBe(Environment.MachineName);
        ((int)machine["cores"]!).ShouldBeGreaterThan(0);
        ((long)machine["ramMb"]!).ShouldBeGreaterThan(0);
        ((long)machine["diskTotalGb"]!).ShouldBeGreaterThan(0);
        ((string)machine["os"]!).ShouldStartWith("Windows");
        new[] { "Ssd", "Hdd", "Unknown" }.ShouldContain((string)machine["diskKind"]!);
        ((string)machine["toadTools"]!).ShouldBe("4.6.0");
        DateTime.Parse((string)machine["at"]!).ToUniversalTime().ShouldBe(Now);
    }

    [Test]
    public void Read_NeverThrows()
    {
        var machine = MachineInfo.Read("4.6.0", () => Now, new Broken());

        ((string)machine["name"]!).ShouldBe(Environment.MachineName);
        machine["cpu"].ShouldBeNull();
        machine["ramMb"].ShouldBeNull();
        machine["gpu"].ShouldBeNull();
        ((string)machine["diskKind"]!).ShouldBe("Unknown");
        machine["revitYears"]!.AsArray().ShouldBeEmpty();
    }

    [Test]
    public void Machine_Json_MatchesBatchFieldNames()
    {
        var machine = MachineInfo.Read("4.6.0", () => Now);

        machine.Select(p => p.Key).ShouldBe(
        [
            "type", "name", "cpu", "cores", "ramMb", "gpu", "gpuMemoryMb", "diskKind", "diskTotalGb", "diskFreeGb", "os", "revitYears", "toadTools", "at"
        ], ignoreOrder: true);
        ((string)machine["type"]!).ShouldBe("machine");
    }

    [Test]
    public void Probe_Memory_Positive()
    {
        ActivityProbe.FreeMemoryMb().ShouldBeGreaterThan(0);
        ActivityProbe.ProcessMemoryMb().ShouldBeGreaterThan(0);
        ActivityProbe.IdleTime().ShouldNotBeNull();
        Should.NotThrow(() => ActivityProbe.IsActive(IntPtr.Zero, TimeSpan.FromMinutes(5)));
    }
}
