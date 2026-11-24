using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace ToadTools.Telemetry;

/// <summary>Откуда читается железо: настоящая машина или подмена в тестах. Каждый метод может бросить — это обрабатывает <see cref="MachineInfo" />.</summary>
internal interface IMachineSource
{
    string Name();
    string? Cpu();
    int? Cores();
    long? RamMb();
    (string? Name, long? MemoryMb) Gpu();
    string DiskKind();
    (long? TotalGb, long? FreeGb) Disk();
    string? Os();
    int[] RevitYears();
}

/// <summary>
///     Карточка компьютера — запись <c>machine</c> пачки. Читается реестром и вызовами Windows, без WMI. Поле, которое не
///     прочиталось, — null (<c>Unknown</c> у диска); метод не бросает. Медленный (обращения к диску и реестру) — только из
///     фонового потока.
/// </summary>
public static class MachineInfo
{
    public static JsonObject Read(string toadTools, Func<DateTime> utcNow) => Read(toadTools, utcNow, new WindowsMachine());

    internal static JsonObject Read(string toadTools, Func<DateTime> utcNow, IMachineSource source)
    {
        var gpu = Try(source.Gpu, (null, null));
        var disk = Try(source.Disk, (null, null));
        return new JsonObject
        {
            ["type"] = "machine",
            ["name"] = Try(source.Name, null) ?? Environment.MachineName,
            ["cpu"] = Try(source.Cpu, null),
            ["cores"] = Try(source.Cores, null),
            ["ramMb"] = Try(source.RamMb, null),
            ["gpu"] = gpu.Name,
            ["gpuMemoryMb"] = gpu.MemoryMb,
            ["diskKind"] = Try(source.DiskKind, "Unknown"),
            ["diskTotalGb"] = disk.TotalGb,
            ["diskFreeGb"] = disk.FreeGb,
            ["os"] = Try(source.Os, null),
            ["revitYears"] = new JsonArray(Try(source.RevitYears, []).Select(y => (JsonNode)y).ToArray()),
            ["toadTools"] = toadTools,
            ["at"] = DateTime.SpecifyKind(Try(utcNow, DateTime.UtcNow), DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture)
        };
    }

    private static T Try<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return fallback;
        }
    }

    /// <summary>Настоящая машина.</summary>
    private sealed class WindowsMachine : IMachineSource
    {
        private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        public string Name() => Environment.MachineName;

        public string? Cpu() =>
            (Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")?.GetValue("ProcessorNameString") as string)?.Trim();

        public int? Cores() => Environment.ProcessorCount;

        public long? RamMb() => ActivityProbe.TotalMemoryMb();

        /// <summary>Первый видеоадаптер с памятью — обычно дискретный; иначе первый по списку.</summary>
        public (string? Name, long? MemoryMb) Gpu()
        {
            using var display = Registry.LocalMachine.OpenSubKey(DisplayClass);
            if (display is null) return (null, null);
            var adapters = new List<(string Name, long? Memory)>();
            foreach (var sub in display.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                using var key = display.OpenSubKey(sub);
                if (key?.GetValue("DriverDesc") is not string name) continue;
                var memory = key.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long q => q,
                    byte[] { Length: >= 8 } b => BitConverter.ToInt64(b, 0),
                    _ => key.GetValue("HardwareInformation.MemorySize") switch
                    {
                        int i => (long)(uint)i,
                        byte[] { Length: >= 4 } b => BitConverter.ToUInt32(b, 0),
                        _ => (long?)null
                    }
                };
                adapters.Add((name.Trim(), memory is > 0 ? memory / (1024 * 1024) : null));
            }

            var best = adapters.OrderByDescending(a => a.Memory ?? 0).FirstOrDefault();
            return best.Name is null ? (null, null) : (best.Name, best.Memory);
        }

        public string DiskKind() => SystemDisk.SeekPenalty() switch
        {
            true => "Hdd",
            false => "Ssd",
            null => "Unknown"
        };

        public (long? TotalGb, long? FreeGb) Disk()
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            return (drive.TotalSize / (1024L * 1024 * 1024), drive.AvailableFreeSpace / (1024L * 1024 * 1024));
        }

        public string? Os()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is null) return null;
            var product = key.GetValue("ProductName") as string ?? "Windows";
            var build = int.TryParse(key.GetValue("CurrentBuildNumber") as string, out var number) ? number : 0;
            // Windows 11 по-прежнему пишет в ProductName «Windows 10»: различает их только номер сборки.
            if (build >= 22000) product = product.Replace("Windows 10", "Windows 11");
            var version = key.GetValue("DisplayVersion") as string ?? key.GetValue("ReleaseId") as string;
            return string.Join(" ", new[] { product, version, build > 0 ? $"(сборка {build})" : null }.Where(p => !string.IsNullOrEmpty(p)));
        }

        /// <summary>Годы Revit, у которых есть <c>Revit.exe</c> в папке установки по умолчанию.</summary>
        public int[] RevitYears()
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk");
            if (!Directory.Exists(root)) return [];
            return Directory.GetDirectories(root, "Revit 20*")
                .Select(dir => (Dir: dir, Year: int.TryParse(Path.GetFileName(dir).Substring("Revit ".Length), out var y) ? y : 0))
                .Where(d => d.Year is >= 2000 and < 2100 && File.Exists(Path.Combine(d.Dir, "Revit.exe")))
                .Select(d => d.Year).Distinct().OrderBy(y => y).ToArray();
        }
    }

    /// <summary>SSD или HDD у системного диска: «штраф за поиск» физического диска, на котором лежит том Windows.</summary>
    private static class SystemDisk
    {
        private const uint FileShareReadWrite = 3;
        private const uint OpenExisting = 3;
        private const uint VolumeGetDiskExtents = 0x00560000;
        private const uint StorageQueryProperty = 0x002D1400;
        private const int SeekPenaltyProperty = 7;

        public static bool? SeekPenalty()
        {
            var letter = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
            int disk;
            using (var volume = Open($@"\\.\{letter}"))
            {
                if (volume.IsInvalid) return null;
                var extents = new byte[64];
                if (!DeviceIoControl(volume, VolumeGetDiskExtents, null, 0, extents, extents.Length, out _, IntPtr.Zero)) return null;
                // VOLUME_DISK_EXTENTS: число частей (4 байта), выравнивание, затем DISK_EXTENT с номером диска в начале.
                disk = BitConverter.ToInt32(extents, 8);
            }

            using var physical = Open($@"\\.\PhysicalDrive{disk}");
            if (physical.IsInvalid) return null;
            var query = new byte[12];
            BitConverter.GetBytes(SeekPenaltyProperty).CopyTo(query, 0);
            var answer = new byte[12];
            if (!DeviceIoControl(physical, StorageQueryProperty, query, query.Length, answer, answer.Length, out var returned, IntPtr.Zero) || returned < 9)
                return null;
            return answer[8] != 0;
        }

        private static SafeFileHandle Open(string path) => CreateFile(path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputSize, byte[] output, int outputSize,
            out int returned, IntPtr overlapped);
    }
}
