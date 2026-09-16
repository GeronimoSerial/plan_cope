using System.Text.Json;
using PlanCope.Local.Api.Services;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class HardwareFingerprintServiceTests
{
    [Fact]
    public void SameInputsProduceSameCompositeHash()
    {
        var reader = new FakeHardwareSignalReader
        {
            MachineGuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            VolumeSerial = "DEADBEEF",
            CpuId = "GenuineIntel Family 6 Model 142"
        };
        var service = new HardwareFingerprintService(reader);

        var first = service.ComputeFingerprint();
        var second = service.ComputeFingerprint();

        Assert.Equal(first.CompositeHash, second.CompositeHash);
        Assert.Equal(64, first.CompositeHash.Length);
    }

    [Fact]
    public void ChangingMachineGuidChangesCompositeHash()
    {
        var service = new HardwareFingerprintService(new FakeHardwareSignalReader
        {
            MachineGuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            VolumeSerial = "DEADBEEF",
            CpuId = "GenuineIntel Family 6 Model 142"
        });
        var baseline = service.ComputeFingerprint().CompositeHash;

        var changed = new HardwareFingerprintService(new FakeHardwareSignalReader
        {
            MachineGuid = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            VolumeSerial = "DEADBEEF",
            CpuId = "GenuineIntel Family 6 Model 142"
        }).ComputeFingerprint().CompositeHash;

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void ChangingVolumeSerialChangesCompositeHash()
    {
        var service = new HardwareFingerprintService(new FakeHardwareSignalReader
        {
            MachineGuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            VolumeSerial = "DEADBEEF",
            CpuId = "GenuineIntel Family 6 Model 142"
        });
        var baseline = service.ComputeFingerprint().CompositeHash;

        var changed = new HardwareFingerprintService(new FakeHardwareSignalReader
        {
            MachineGuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            VolumeSerial = "1234ABCD",
            CpuId = "GenuineIntel Family 6 Model 142"
        }).ComputeFingerprint().CompositeHash;

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void ChangingCpuIdChangesCompositeHash()
    {
        var service = new HardwareFingerprintService(new FakeHardwareSignalReader
        {
            MachineGuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            VolumeSerial = "DEADBEEF",
            CpuId = "GenuineIntel Family 6 Model 142"
        });
        var baseline = service.ComputeFingerprint().CompositeHash;

        var changed = new HardwareFingerprintService(new FakeHardwareSignalReader
        {
            MachineGuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            VolumeSerial = "DEADBEEF",
            CpuId = "GenuineIntel Family 6 Model 99"
        }).ComputeFingerprint().CompositeHash;

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void NullComponentIsRecordedAsNotPresentAndDoesNotThrow()
    {
        var service = new HardwareFingerprintService(new FakeHardwareSignalReader
        {
            MachineGuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            VolumeSerial = null,
            CpuId = "GenuineIntel Family 6 Model 142"
        });

        var result = service.ComputeFingerprint();
        using var document = JsonDocument.Parse(result.ComponentsJson);
        var volumeSerial = document.RootElement.GetProperty("volumeSerial");

        Assert.False(volumeSerial.GetProperty("present").GetBoolean());
        Assert.Equal(JsonValueKind.Null, volumeSerial.GetProperty("value").ValueKind);
    }

    [Fact]
    public void AllThreeComponentNamesAreAlwaysPresentInComponentsJson()
    {
        var service = new HardwareFingerprintService(new FakeHardwareSignalReader
        {
            MachineGuid = null,
            VolumeSerial = null,
            CpuId = null
        });

        var result = service.ComputeFingerprint();
        using var document = JsonDocument.Parse(result.ComponentsJson);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.True(root.TryGetProperty("machineGuid", out var machineGuid));
        Assert.True(root.TryGetProperty("volumeSerial", out var volumeSerial));
        Assert.True(root.TryGetProperty("cpuId", out var cpuId));

        Assert.False(machineGuid.GetProperty("present").GetBoolean());
        Assert.False(volumeSerial.GetProperty("present").GetBoolean());
        Assert.False(cpuId.GetProperty("present").GetBoolean());
    }

    [Fact]
    public void AbsentSignalIsDistinguishedFromEmptyStringInHash()
    {
        var withMissing = new HardwareFingerprintService(new FakeHardwareSignalReader
        {
            MachineGuid = null,
            VolumeSerial = "DEADBEEF",
            CpuId = "GenuineIntel"
        }).ComputeFingerprint().CompositeHash;

        var withEmpty = new HardwareFingerprintService(new FakeHardwareSignalReader
        {
            MachineGuid = string.Empty,
            VolumeSerial = "DEADBEEF",
            CpuId = "GenuineIntel"
        }).ComputeFingerprint().CompositeHash;

        Assert.NotEqual(withMissing, withEmpty);
    }

    private sealed class FakeHardwareSignalReader : IRawHardwareSignalReader
    {
        public string? MachineGuid { get; init; }
        public string? VolumeSerial { get; init; }
        public string? CpuId { get; init; }

        public string? ReadMachineGuid() => MachineGuid;
        public string? ReadSystemVolumeSerial() => VolumeSerial;
        public string? ReadCpuIdentifier() => CpuId;
    }
}