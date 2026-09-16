using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace PlanCope.Local.Api.Services;

public interface IRawHardwareSignalReader
{
    string? ReadMachineGuid();
    string? ReadSystemVolumeSerial();
    string? ReadCpuIdentifier();
}

[SupportedOSPlatform("windows")]
public sealed class WindowsHardwareSignalReader : IRawHardwareSignalReader
{
    private const string MachineGuidKeyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography";
    private const string MachineGuidValueName = "MachineGuid";
    private const string CpuIdentifierKeyPath = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string CpuIdentifierValueName = "ProcessorNameString";

    public string? ReadMachineGuid()
    {
        try
        {
            return ReadRegistryString(MachineGuidKeyPath, MachineGuidValueName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string? ReadSystemVolumeSerial()
    {
        var rootPath = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrEmpty(rootPath))
        {
            return null;
        }

        try
        {
            if (!GetVolumeInformationW(rootPath, null, 0, out var serialNumber, out _, out _, null, 0))
            {
                return null;
            }

            return serialNumber.ToString("X8");
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string? ReadCpuIdentifier()
    {
        try
        {
            return ReadRegistryString(CpuIdentifierKeyPath, CpuIdentifierValueName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ReadRegistryString(string keyPath, string valueName)
    {
        return Registry.GetValue(keyPath, valueName, null) as string;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        int fileSystemNameSize);
}

public sealed class HardwareFingerprintService(IRawHardwareSignalReader reader)
{
    private const string ComponentSeparator = "|";
    private const string MissingComponentPlaceholder = "<missing>";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public HardwareFingerprintResult ComputeFingerprint()
    {
        var machineGuid = reader.ReadMachineGuid();
        var volumeSerial = reader.ReadSystemVolumeSerial();
        var cpuId = reader.ReadCpuIdentifier();

        var composite = string.Join(
            ComponentSeparator,
            machineGuid ?? MissingComponentPlaceholder,
            volumeSerial ?? MissingComponentPlaceholder,
            cpuId ?? MissingComponentPlaceholder);

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(composite));
        var compositeHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var components = new HardwareFingerprintComponents(
            new HardwareFingerprintComponent(Present: machineGuid is not null, Value: machineGuid),
            new HardwareFingerprintComponent(Present: volumeSerial is not null, Value: volumeSerial),
            new HardwareFingerprintComponent(Present: cpuId is not null, Value: cpuId));

        return new HardwareFingerprintResult(compositeHash, JsonSerializer.Serialize(components, JsonOptions));
    }
}

public sealed record HardwareFingerprintResult(string CompositeHash, string ComponentsJson);

public sealed record HardwareFingerprintComponent(bool Present, string? Value);

public sealed record HardwareFingerprintComponents(
    HardwareFingerprintComponent MachineGuid,
    HardwareFingerprintComponent VolumeSerial,
    HardwareFingerprintComponent CpuId);