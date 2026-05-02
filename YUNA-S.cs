using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using DiskInfoToolkit;
using LibreHardwareMonitor.Hardware;

internal static class Program
{
	public static readonly string BASE = AppContext.BaseDirectory.TrimEnd('\\', '/');

	[STAThread]
	private static void Main()
	{
		if (BASE.Length > 0)
		{
			try
			{
				Directory.SetCurrentDirectory(BASE);
			}
			catch
			{
			}
		}
		AppDomain.CurrentDomain.AssemblyResolve += delegate(object? s, ResolveEventArgs a)
		{
			string name = new AssemblyName(a.Name).Name;
			string text = Path.Combine(BASE, name + ".dll");
			return File.Exists(text) ? Assembly.LoadFrom(text) : null;
		};
		AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
		{
			Log(e.ExceptionObject.ToString());
		};
		Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
		{
			Log(e.Exception.ToString());
		};
		if (!IsAdmin())
		{
			try
			{
				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = Environment.ProcessPath,
					Verb = "runas",
					UseShellExecute = true,
					WorkingDirectory = BASE
				};
				Process.Start(startInfo);
				return;
			}
			catch
			{
				return;
			}
		}
		try
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(defaultValue: false);
			SplashForm splash = new SplashForm();
			splash.Show();
			Application.DoEvents();
			YunaS main = new YunaS();
			main.Opacity = 0.0;
			main.DataReady += delegate
			{
				main.Opacity = 1.0;
				System.Windows.Forms.Timer fadeDelay = new System.Windows.Forms.Timer
				{
					Interval = 1000
				};
				fadeDelay.Tick += delegate
				{
					fadeDelay.Stop();
					fadeDelay.Dispose();
					System.Windows.Forms.Timer fade = new System.Windows.Forms.Timer
					{
						Interval = 30
					};
					fade.Tick += delegate
					{
						if (splash.Opacity > 0.05)
						{
							splash.Opacity -= 0.05;
						}
						else
						{
							fade.Stop();
							fade.Dispose();
							splash.Close();
							splash.Dispose();
						}
					};
					fade.Start();
				};
				fadeDelay.Start();
			};
			Application.Run(main);
		}
		catch (Exception ex)
		{
			Log(ex.ToString());
		}
	}

	private static bool IsAdmin()
	{
		try
		{
			using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
			return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}

	public static void Log(string msg)
	{
		try
		{
			File.WriteAllText(Path.Combine(BASE, "YUNSEUL-S-crash.txt"), DateTime.Now.ToString() + "\n" + msg);
		}
		catch
		{
		}
	}
}
internal static class Clr
{
	public static readonly Color BG = H("#0d1117");

	public static readonly Color CARD = H("#161b22");

	public static readonly Color BORDER = H("#21262d");

	public static readonly Color FG = H("#e6edf3");

	public static readonly Color FG2 = H("#8b949e");

	public static readonly Color BLUE = H("#58a6ff");

	public static readonly Color PURPLE = H("#bc8cff");

	public static readonly Color GREEN = H("#3fb950");

	public static readonly Color YELLOW = H("#d29922");

	public static readonly Color RED = H("#f85149");

	public static readonly Color CYAN = H("#39d3f2");

	public static readonly Color ORANGE = H("#ff7b54");

	public static readonly Color[] DISK = new Color[8]
	{
		H("#7EC8E3"),
		H("#A8D5A2"),
		H("#C9B1D9"),
		H("#FFD59A"),
		H("#FFB6C1"),
		H("#B2F0E8"),
		H("#F5DEB3"),
		H("#DDA0DD")
	};

	public static Color H(string hex)
	{
		int num = Convert.ToInt32(hex.TrimStart('#'), 16);
		return Color.FromArgb(255, (num >> 16) & 0xFF, (num >> 8) & 0xFF, num & 0xFF);
	}

	public static Color Usage(float p)
	{
		return (p < 60f) ? BLUE : ((p < 85f) ? YELLOW : RED);
	}

	public static Color Temp(float? t)
	{
		if (!t.HasValue || t <= 0f)
		{
			return FG2;
		}
		return (t < 50f) ? GREEN : ((t < 75f) ? YELLOW : RED);
	}
}
internal static class Fmt
{
	private static readonly string[] U = new string[5] { "B", "KB", "MB", "GB", "TB" };

	public static string Bytes(double n)
	{
		int num = 0;
		while (n >= 1024.0 && num < U.Length - 1)
		{
			n /= 1024.0;
			num++;
		}
		return n.ToString("0.#") + U[num];
	}

	public static string Speed(double n)
	{
		int num = 0;
		while (n >= 1024.0 && num < U.Length - 1)
		{
			n /= 1024.0;
			num++;
		}
		return (int)Math.Round(n) + U[num] + "/s";
	}
}
internal class CpuSnap
{
	public string Name;

	public float[] CoreLoad;

	public float? Freq;

	public int? BoostCore;

	public int? TopCore;

	public float? Temp;

	public float? Power;
}
internal class GpuSnap
{
	public string Name;

	public float Load;

	public float MemPct;

	public float MemUsedMB;

	public float MemTotalMB;

	public float? Temp;
}
internal class MemSnap
{
	public float UsedGB;

	public float TotalGB;

	public float LoadPct;
}
internal class DiskSnap
{
	public string Mount;

	public float UsePct;

	public float UsedGB;

	public float TotalGB;

	public float ReadBps;

	public float WriteBps;

	public float? Temp;
}
internal class HwSnap
{
	public CpuSnap Cpu;

	public List<GpuSnap> Gpus = new List<GpuSnap>();

	public MemSnap Mem;

	public List<DiskSnap> Disks = new List<DiskSnap>();
}
internal class HwCollector : IDisposable
{
	private Computer _pc;

	private Thread _thread;

	private volatile bool _stop;

	private readonly object _lk = new object();

	private HwSnap _snap = new HwSnap();

	private PerformanceCounter[] _loadCtrs;

	private PerformanceCounter[] _perfCtrs;

	private PerformanceCounter[] _freqCtrs;

	private volatile float _nominalMHz;

	private bool _ctrsReady;

	private readonly Dictionary<string, PerformanceCounter> _diskRd = new Dictionary<string, PerformanceCounter>();

	private readonly Dictionary<string, PerformanceCounter> _diskWr = new Dictionary<string, PerformanceCounter>();

	private bool _diskToolkitOk;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, nint lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, nint hTemplateFile);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(nint hDevice, uint dwIoControlCode, byte[] lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize, ref uint lpBytesReturned, nint lpOverlapped);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(nint hObject);

	public HwCollector()
	{
		_pc = new Computer
		{
			IsCpuEnabled = true,
			IsGpuEnabled = true,
			IsMemoryEnabled = true,
			IsStorageEnabled = true,
			IsMotherboardEnabled = true
		};
		try
		{
			_pc.Open();
		}
		catch
		{
			try
			{
				_pc.Close();
			}
			catch
			{
			}
			_pc = new Computer
			{
				IsCpuEnabled = true,
				IsGpuEnabled = true,
				IsMemoryEnabled = true,
				IsStorageEnabled = false,
				IsMotherboardEnabled = true
			};
			try
			{
				_pc.Open();
			}
			catch
			{
				try
				{
					_pc.Close();
				}
				catch
				{
				}
				_pc = new Computer
				{
					IsCpuEnabled = true,
					IsGpuEnabled = true,
					IsMemoryEnabled = true
				};
				_pc.Open();
			}
		}
		try
		{
			string[] source = (from n in new PerformanceCounterCategory("Processor Information").GetInstanceNames()
				where !n.Contains("_Total") && !n.Contains("_Idle")
				orderby n
				select n).ToArray();
			_loadCtrs = source.Select((string n) => new PerformanceCounter("Processor Information", "% Processor Utility", n)).ToArray();
			_perfCtrs = source.Select((string n) => new PerformanceCounter("Processor Information", "% Processor Performance", n)).ToArray();
			_freqCtrs = source.Select((string n) => new PerformanceCounter("Processor Information", "Processor Frequency", n)).ToArray();
			PerformanceCounter[] loadCtrs = _loadCtrs;
			foreach (PerformanceCounter performanceCounter in loadCtrs)
			{
				performanceCounter.NextValue();
			}
			PerformanceCounter[] perfCtrs = _perfCtrs;
			foreach (PerformanceCounter performanceCounter2 in perfCtrs)
			{
				performanceCounter2.NextValue();
			}
			PerformanceCounter[] freqCtrs = _freqCtrs;
			foreach (PerformanceCounter performanceCounter3 in freqCtrs)
			{
				performanceCounter3.NextValue();
			}
		}
		catch
		{
			_loadCtrs = null;
			_perfCtrs = null;
			_freqCtrs = null;
		}
		try
		{
			foreach (string item in from n in new PerformanceCounterCategory("LogicalDisk").GetInstanceNames()
				where n.Length == 2 && n[1] == ':'
				select n)
			{
				_diskRd[item] = new PerformanceCounter("LogicalDisk", "Disk Read Bytes/sec", item);
				_diskWr[item] = new PerformanceCounter("LogicalDisk", "Disk Write Bytes/sec", item);
				_diskRd[item].NextValue();
				_diskWr[item].NextValue();
			}
		}
		catch
		{
		}
		try
		{
			StorageManager.ReloadStorages();
			_diskToolkitOk = true;
		}
		catch
		{
			_diskToolkitOk = false;
		}
		_thread = new Thread(Loop)
		{
			IsBackground = true,
			Name = "HwCollect"
		};
		_thread.Start();
	}

	public string GetCpuName()
	{
		foreach (IHardware item in _pc.Hardware)
		{
			if (item.HardwareType.ToString() == "Cpu")
			{
				return item.Name ?? "";
			}
		}
		return "";
	}

	private static float? ReadDiskTempByIndex(int physIdx)
	{
		try
		{
			nint num = CreateFileW("\\\\.\\PhysicalDrive" + physIdx, 0u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (((IntPtr)num).ToInt64() == -1)
			{
				return null;
			}
			try
			{
				byte[] lpInBuffer = new byte[8] { 22, 0, 0, 0, 0, 0, 0, 0 };
				byte[] array = new byte[1024];
				uint lpBytesReturned = 0u;
				if (!DeviceIoControl(num, 2954240u, lpInBuffer, 8u, array, (uint)array.Length, ref lpBytesReturned, IntPtr.Zero))
				{
					return null;
				}
				if (lpBytesReturned < 28)
				{
					return null;
				}
				short num2 = BitConverter.ToInt16(array, 12);
				if (num2 <= 0 || num2 > 32)
				{
					return null;
				}
				short num3 = BitConverter.ToInt16(array, 26);
				if (num3 > 0 && num3 < 150)
				{
					return num3;
				}
				if (num3 > 273 && num3 < 400)
				{
					return num3 - 273;
				}
			}
			finally
			{
				CloseHandle(num);
			}
		}
		catch
		{
		}
		return null;
	}

	private static Dictionary<string, int> GetDriveLetterToPhysicalIndex()
	{
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		try
		{
			Dictionary<string, string> dictionary2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			using (ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT Antecedent,Dependent FROM Win32_LogicalDiskToPartition"))
			{
				foreach (ManagementObject item in managementObjectSearcher.Get())
				{
					string input = ((item["Antecedent"] != null) ? item["Antecedent"].ToString() : "");
					string input2 = ((item["Dependent"] != null) ? item["Dependent"].ToString() : "");
					Match match = Regex.Match(input2, "DeviceID=\"([A-Za-z]:)\"");
					if (match.Success)
					{
						string value = match.Groups[1].Value.ToUpper();
						Match match2 = Regex.Match(input, "DeviceID=\"(.+?)\"");
						if (match2.Success)
						{
							dictionary2[match2.Groups[1].Value] = value;
						}
					}
				}
			}
			Dictionary<int, List<string>> dictionary3 = new Dictionary<int, List<string>>();
			using (ManagementObjectSearcher managementObjectSearcher2 = new ManagementObjectSearcher("SELECT Antecedent,Dependent FROM Win32_DiskDriveToDiskPartition"))
			{
				foreach (ManagementObject item2 in managementObjectSearcher2.Get())
				{
					string input3 = ((item2["Antecedent"] != null) ? item2["Antecedent"].ToString() : "");
					string input4 = ((item2["Dependent"] != null) ? item2["Dependent"].ToString() : "");
					Match match3 = Regex.Match(input3, "PHYSICALDRIVE(\\d+)", RegexOptions.IgnoreCase);
					if (!match3.Success)
					{
						continue;
					}
					int key = int.Parse(match3.Groups[1].Value);
					Match match4 = Regex.Match(input4, "DeviceID=\"(.+?)\"");
					if (match4.Success)
					{
						string value2 = match4.Groups[1].Value;
						if (!dictionary3.ContainsKey(key))
						{
							dictionary3[key] = new List<string>();
						}
						dictionary3[key].Add(value2);
					}
				}
			}
			foreach (KeyValuePair<int, List<string>> item3 in dictionary3)
			{
				foreach (string item4 in item3.Value)
				{
					if (dictionary2.TryGetValue(item4, out var value3) && !dictionary.ContainsKey(value3))
					{
						dictionary[value3] = item3.Key;
					}
				}
			}
		}
		catch
		{
		}
		return dictionary;
	}

	private static float? GetWmiAcpiTemp()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
			float num = 0f;
			foreach (ManagementObject item in managementObjectSearcher.Get())
			{
				float num2 = Convert.ToSingle(item["CurrentTemperature"]);
				float num3 = num2 / 10f - 273.15f;
				if (num3 > 20f && num3 < 120f && num3 > num)
				{
					num = num3;
				}
			}
			return (num > 0f) ? new float?(num) : null;
		}
		catch
		{
		}
		return null;
	}

	private static Dictionary<int, float?> GetSmartTemps()
	{
		Dictionary<int, float?> dictionary = new Dictionary<int, float?>();
		try
		{
			Dictionary<string, int> dictionary2 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			using (ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT Index, PNPDeviceID FROM Win32_DiskDrive"))
			{
				foreach (ManagementObject item in managementObjectSearcher.Get())
				{
					string text = ((item["PNPDeviceID"] != null) ? item["PNPDeviceID"].ToString() : "");
					if (text.Length > 0)
					{
						dictionary2[text] = Convert.ToInt32(item["Index"]);
					}
				}
			}
			using ManagementObjectSearcher managementObjectSearcher2 = new ManagementObjectSearcher("root\\WMI", "SELECT InstanceName, VendorSpecific FROM MSStorageDriver_ATAPISmartData");
			foreach (ManagementObject item2 in managementObjectSearcher2.Get())
			{
				try
				{
					string key = Regex.Replace((item2["InstanceName"] != null) ? item2["InstanceName"].ToString() : "", "\\\\+\\d+$", "");
					if (!dictionary2.TryGetValue(key, out var value) || !(item2["VendorSpecific"] is byte[] array))
					{
						continue;
					}
					for (int i = 2; i + 12 <= array.Length; i += 12)
					{
						byte b = array[i];
						if (b == 194 || b == 190)
						{
							float num = (int)array[i + 5];
							if (num > 0f && num < 100f && !dictionary.ContainsKey(value))
							{
								dictionary[value] = num;
								break;
							}
						}
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return dictionary;
	}

	private static float? GetThermalZoneTemp()
	{
		try
		{
			PerformanceCounterCategory performanceCounterCategory = new PerformanceCounterCategory("Thermal Zone Information");
			string[] instanceNames = performanceCounterCategory.GetInstanceNames();
			float num = 0f;
			string[] array = instanceNames;
			foreach (string instanceName in array)
			{
				try
				{
					using PerformanceCounter performanceCounter = new PerformanceCounter("Thermal Zone Information", "Temperature", instanceName);
					float num2 = performanceCounter.NextValue();
					float num3 = num2 / 10f - 273.15f;
					if (num3 > 20f && num3 < 120f && num3 > num)
					{
						num = num3;
					}
				}
				catch
				{
				}
			}
			return (num > 0f) ? new float?(num) : null;
		}
		catch
		{
		}
		return null;
	}

	private void WriteSensorDump()
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder("=== LHM Sensor Dump (after first collect) ===\n");
			foreach (IHardware item in _pc.Hardware)
			{
				stringBuilder.AppendLine("[" + item.HardwareType.ToString() + "] " + item.Name);
				ISensor[] sensors = item.Sensors;
				foreach (ISensor sensor in sensors)
				{
					stringBuilder.AppendLine("  " + sensor.SensorType.ToString() + " | " + sensor.Name + " = " + sensor.Value);
				}
				IHardware[] subHardware = item.SubHardware;
				foreach (IHardware hardware in subHardware)
				{
					stringBuilder.AppendLine("  [Sub:" + hardware.HardwareType.ToString() + "] " + hardware.Name);
					ISensor[] sensors2 = hardware.Sensors;
					foreach (ISensor sensor2 in sensors2)
					{
						stringBuilder.AppendLine("    " + sensor2.SensorType.ToString() + " | " + sensor2.Name + " = " + sensor2.Value);
					}
				}
			}
			stringBuilder.AppendLine("\n=== Thermal Zone Information (Performance Counter) ===");
			try
			{
				PerformanceCounterCategory performanceCounterCategory = new PerformanceCounterCategory("Thermal Zone Information");
				string[] instanceNames = performanceCounterCategory.GetInstanceNames();
				if (instanceNames.Length == 0)
				{
					stringBuilder.AppendLine("  No instances");
				}
				string[] array = instanceNames;
				foreach (string text in array)
				{
					try
					{
						using PerformanceCounter performanceCounter = new PerformanceCounter("Thermal Zone Information", "Temperature", text);
						float num = performanceCounter.NextValue();
						float num2 = num / 10f - 273.15f;
						stringBuilder.AppendLine("  [" + text + "] raw=" + num + " => " + num2.ToString("0.0") + "C");
					}
					catch (Exception ex)
					{
						stringBuilder.AppendLine("  [" + text + "] err: " + ex.Message);
					}
				}
			}
			catch (Exception ex2)
			{
				stringBuilder.AppendLine("  Category error: " + ex2.Message);
			}
			stringBuilder.AppendLine("\n=== Drive Letter to Physical Index ===");
			try
			{
				Dictionary<string, int> driveLetterToPhysicalIndex = GetDriveLetterToPhysicalIndex();
				if (driveLetterToPhysicalIndex.Count == 0)
				{
					stringBuilder.AppendLine("  No mappings found");
				}
				foreach (KeyValuePair<string, int> item2 in driveLetterToPhysicalIndex)
				{
					stringBuilder.AppendLine("  " + item2.Key + " -> PhysDrive" + item2.Value);
				}
			}
			catch (Exception ex3)
			{
				stringBuilder.AppendLine("  Error: " + ex3.Message);
			}
			stringBuilder.AppendLine("\n=== Disk Temps via IOCTL (StorageDeviceTemperatureProperty=22) ===");
			try
			{
				Dictionary<string, int> driveLetterToPhysicalIndex2 = GetDriveLetterToPhysicalIndex();
				HashSet<int> hashSet = new HashSet<int>();
				foreach (KeyValuePair<string, int> item3 in driveLetterToPhysicalIndex2)
				{
					if (!hashSet.Contains(item3.Value))
					{
						hashSet.Add(item3.Value);
						float? num3 = ReadDiskTempByIndex(item3.Value);
						stringBuilder.AppendLine("  PhysDrive" + item3.Value + " (" + item3.Key + "): " + (num3.HasValue ? (num3.Value + "C") : "N/A"));
					}
				}
			}
			catch (Exception ex4)
			{
				stringBuilder.AppendLine("  Error: " + ex4.Message);
			}
			stringBuilder.AppendLine("\n=== Disk Temps via WMI SMART ===");
			try
			{
				Dictionary<int, float?> smartTemps = GetSmartTemps();
				if (smartTemps.Count == 0)
				{
					stringBuilder.AppendLine("  No SMART data found");
				}
				foreach (KeyValuePair<int, float?> item4 in smartTemps)
				{
					stringBuilder.AppendLine("  PhysDrive" + item4.Key + ": " + (item4.Value.HasValue ? (item4.Value.Value + "C") : "N/A"));
				}
			}
			catch (Exception ex5)
			{
				stringBuilder.AppendLine("  Error: " + ex5.Message);
			}
			stringBuilder.AppendLine("\n=== WMI ACPI Thermal Zone (CPU fallback) ===");
			try
			{
				float? wmiAcpiTemp = GetWmiAcpiTemp();
				stringBuilder.AppendLine("  " + (wmiAcpiTemp.HasValue ? (wmiAcpiTemp.Value.ToString("0.0") + "C") : "N/A"));
			}
			catch (Exception ex6)
			{
				stringBuilder.AppendLine("  Error: " + ex6.Message);
			}
			stringBuilder.AppendLine("\n=== Motherboard ALL Temperature Sensors ===");
			try
			{
				foreach (IHardware item5 in _pc.Hardware)
				{
					if (item5.HardwareType.ToString() != "Motherboard")
					{
						continue;
					}
					ISensor[] sensors3 = item5.Sensors;
					foreach (ISensor sensor3 in sensors3)
					{
						if (sensor3.SensorType.ToString() == "Temperature")
						{
							stringBuilder.AppendLine("  [MB] " + sensor3.Name + " = " + sensor3.Value);
						}
					}
					IHardware[] subHardware2 = item5.SubHardware;
					foreach (IHardware hardware2 in subHardware2)
					{
						stringBuilder.AppendLine("  [Sub:" + hardware2.HardwareType.ToString() + "] " + hardware2.Name);
						ISensor[] sensors4 = hardware2.Sensors;
						foreach (ISensor sensor4 in sensors4)
						{
							if (sensor4.SensorType.ToString() == "Temperature")
							{
								stringBuilder.AppendLine("    " + sensor4.Name + " = " + sensor4.Value);
							}
						}
					}
				}
			}
			catch (Exception ex7)
			{
				stringBuilder.AppendLine("  Error: " + ex7.Message);
			}
			stringBuilder.AppendLine("\n=== LHM Storage Hardware ===");
			try
			{
				bool flag = false;
				foreach (IHardware item6 in _pc.Hardware)
				{
					if (!(item6.HardwareType.ToString() != "Storage"))
					{
						flag = true;
						stringBuilder.AppendLine("  " + item6.Name);
						ISensor[] sensors5 = item6.Sensors;
						foreach (ISensor sensor5 in sensors5)
						{
							stringBuilder.AppendLine("    " + sensor5.SensorType.ToString() + " | " + sensor5.Name + " = " + sensor5.Value);
						}
					}
				}
				if (!flag)
				{
					stringBuilder.AppendLine("  No Storage hardware found (IsStorageEnabled may have failed)");
				}
			}
			catch (Exception ex8)
			{
				stringBuilder.AppendLine("  Error: " + ex8.Message);
			}
			stringBuilder.AppendLine("\n=== DiskInfoToolkit SMART Temps ===");
			try
			{
				if (!_diskToolkitOk)
				{
					stringBuilder.AppendLine("  StorageManager.ReloadStorages() failed");
				}
				else
				{
					foreach (Storage storage in StorageManager.Storages)
					{
						try
						{
							storage.Update();
							SmartInfo smart = storage.Smart;
							stringBuilder.AppendLine("  PhysDrive" + storage.DriveNumber + " [" + storage.Model + "] NVMe=" + storage.IsNVMe + " Temp=" + ((smart != null && smart.Temperature.HasValue) ? (smart.Temperature.Value + "C") : "N/A"));
						}
						catch (Exception ex9)
						{
							stringBuilder.AppendLine("  Error on drive: " + ex9.Message);
						}
					}
					if (StorageManager.Storages.Count == 0)
					{
						stringBuilder.AppendLine("  No storages found");
					}
				}
			}
			catch (Exception ex10)
			{
				stringBuilder.AppendLine("  Error: " + ex10.Message);
			}
			File.WriteAllText(Path.Combine(Program.BASE, "sensors_dump.txt"), stringBuilder.ToString());
		}
		catch
		{
		}
	}

	private void Loop()
	{
		Thread.Sleep(900);
		if (_loadCtrs != null)
		{
			PerformanceCounter[] loadCtrs = _loadCtrs;
			foreach (PerformanceCounter performanceCounter in loadCtrs)
			{
				performanceCounter.NextValue();
			}
		}
		if (_perfCtrs != null)
		{
			PerformanceCounter[] perfCtrs = _perfCtrs;
			foreach (PerformanceCounter performanceCounter2 in perfCtrs)
			{
				performanceCounter2.NextValue();
			}
		}
		if (_freqCtrs != null)
		{
			float num = 0f;
			PerformanceCounter[] freqCtrs = _freqCtrs;
			foreach (PerformanceCounter performanceCounter3 in freqCtrs)
			{
				try
				{
					float num2 = performanceCounter3.NextValue();
					if (num2 > num)
					{
						num = num2;
					}
				}
				catch
				{
				}
			}
			_nominalMHz = num;
		}
		if (_nominalMHz <= 0f)
		{
			try
			{
				using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
				using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectSearcher.Get().GetEnumerator();
				if (managementObjectEnumerator.MoveNext())
				{
					ManagementObject managementObject = (ManagementObject)managementObjectEnumerator.Current;
					_nominalMHz = Convert.ToSingle(managementObject["CurrentClockSpeed"]);
				}
			}
			catch
			{
			}
		}
		_ctrsReady = true;
		try
		{
			Collect();
		}
		catch
		{
		}
		WriteSensorDump();
		while (!_stop)
		{
			try
			{
				Collect();
			}
			catch
			{
			}
			Thread.Sleep(1000);
		}
	}

	private void Collect()
	{
		foreach (IHardware item in _pc.Hardware)
		{
			item.Update();
			IHardware[] subHardware = item.SubHardware;
			foreach (IHardware hardware in subHardware)
			{
				hardware.Update();
			}
		}
		HwSnap hwSnap = new HwSnap();
		float? temp = null;
		foreach (IHardware item2 in _pc.Hardware)
		{
			if (item2.HardwareType.ToString() != "Motherboard")
			{
				continue;
			}
			IHardware[] subHardware2 = item2.SubHardware;
			foreach (IHardware hardware2 in subHardware2)
			{
				ISensor[] sensors = hardware2.Sensors;
				foreach (ISensor sensor in sensors)
				{
					float? value = sensor.Value;
					if (value.HasValue && !(value <= 0f) && !(sensor.SensorType.ToString() != "Temperature"))
					{
						string text = sensor.Name.ToLower();
						if (!temp.HasValue && (text.Contains("cpu") || text.Contains("tcpu")))
						{
							temp = value.Value;
						}
					}
				}
			}
			ISensor[] sensors2 = item2.Sensors;
			foreach (ISensor sensor2 in sensors2)
			{
				float? value2 = sensor2.Value;
				if (value2.HasValue && !(value2 <= 0f) && !(sensor2.SensorType.ToString() != "Temperature"))
				{
					string text2 = sensor2.Name.ToLower();
					if (!temp.HasValue && (text2.Contains("cpu") || text2.Contains("tcpu")))
					{
						temp = value2.Value;
					}
				}
			}
		}
		foreach (IHardware item3 in _pc.Hardware)
		{
			string text3 = item3.HardwareType.ToString();
			switch (text3)
			{
			case "Cpu":
			{
				CpuSnap cpuSnap = new CpuSnap
				{
					Name = item3.Name
				};
				ISensor[] sensors5 = item3.Sensors;
				foreach (ISensor sensor5 in sensors5)
				{
					float? value5 = sensor5.Value;
					if (!value5.HasValue || value5 <= 0f)
					{
						continue;
					}
					string text6 = sensor5.SensorType.ToString();
					string name3 = sensor5.Name;
					if (text6 == "Temperature")
					{
						if (!cpuSnap.Temp.HasValue)
						{
							cpuSnap.Temp = value5.Value;
						}
						if (name3.Contains("Tdie") || name3.Contains("Tctl") || name3.Contains("Package"))
						{
							cpuSnap.Temp = value5.Value;
						}
					}
					if (text6 == "Power" && name3.ToLower().Contains("package"))
					{
						cpuSnap.Power = value5.Value;
					}
				}
				IHardware[] subHardware3 = item3.SubHardware;
				foreach (IHardware hardware3 in subHardware3)
				{
					ISensor[] sensors6 = hardware3.Sensors;
					foreach (ISensor sensor6 in sensors6)
					{
						float? value6 = sensor6.Value;
						if (value6.HasValue && !(value6 <= 0f) && sensor6.SensorType.ToString() == "Temperature" && !cpuSnap.Temp.HasValue)
						{
							cpuSnap.Temp = value6.Value;
						}
					}
				}
				if (!cpuSnap.Temp.HasValue && temp.HasValue)
				{
					cpuSnap.Temp = temp;
				}
				if (!cpuSnap.Temp.HasValue)
				{
					float? thermalZoneTemp = GetThermalZoneTemp();
					if (thermalZoneTemp.HasValue)
					{
						cpuSnap.Temp = thermalZoneTemp;
					}
				}
				if (!cpuSnap.Temp.HasValue)
				{
					float? wmiAcpiTemp = GetWmiAcpiTemp();
					if (wmiAcpiTemp.HasValue)
					{
						cpuSnap.Temp = wmiAcpiTemp;
					}
				}
				if (_ctrsReady && _loadCtrs != null)
				{
					float[] array = _loadCtrs.Select(delegate(PerformanceCounter c)
					{
						try
						{
							return Math.Min(100f, c.NextValue());
						}
						catch
						{
							return 0f;
						}
					}).ToArray();
					float[] array2 = null;
					if (_perfCtrs != null)
					{
						array2 = _perfCtrs.Select(delegate(PerformanceCounter c)
						{
							try
							{
								return c.NextValue();
							}
							catch
							{
								return 0f;
							}
						}).ToArray();
					}
					cpuSnap.CoreLoad = array;
					if (array.Length != 0)
					{
						float value7 = array.Max();
						cpuSnap.TopCore = Array.IndexOf(array, value7);
					}
					if (array2 != null && array2.Length != 0 && _nominalMHz > 100f)
					{
						float[] array3 = new float[array2.Length];
						for (int num5 = 0; num5 < array2.Length; num5++)
						{
							array3[num5] = _nominalMHz * array2[num5] / 100f;
						}
						float num6 = array3.Max();
						if (num6 > 100f)
						{
							cpuSnap.Freq = num6 / 1000f;
							cpuSnap.BoostCore = Array.IndexOf(array3, num6);
						}
					}
					if (!cpuSnap.Freq.HasValue && _freqCtrs != null)
					{
						float[] array4 = _freqCtrs.Select(delegate(PerformanceCounter c)
						{
							try
							{
								return c.NextValue();
							}
							catch
							{
								return 0f;
							}
						}).ToArray();
						if (array4.Length != 0 && array4.Max() > 0f)
						{
							float num7 = array4.Max();
							cpuSnap.Freq = num7 / 1000f;
							cpuSnap.BoostCore = Array.IndexOf(array4, num7);
						}
					}
				}
				hwSnap.Cpu = cpuSnap;
				break;
			}
			default:
				if (!(text3 == "GpuIntel"))
				{
					if (!(text3 == "Memory"))
					{
						break;
					}
					MemSnap memSnap = new MemSnap();
					float num = 0f;
					ISensor[] sensors3 = item3.Sensors;
					foreach (ISensor sensor3 in sensors3)
					{
						float? value3 = sensor3.Value;
						if (value3.HasValue)
						{
							string text4 = sensor3.SensorType.ToString();
							string name = sensor3.Name;
							if (text4 == "Data" && name == "Memory Used")
							{
								memSnap.UsedGB = value3.Value;
							}
							if (text4 == "Data" && name == "Memory Available")
							{
								num = value3.Value;
							}
							if (text4 == "Load" && name == "Memory")
							{
								memSnap.LoadPct = value3.Value;
							}
						}
					}
					memSnap.TotalGB = memSnap.UsedGB + num;
					if (hwSnap.Mem == null || memSnap.TotalGB > hwSnap.Mem.TotalGB)
					{
						hwSnap.Mem = memSnap;
					}
					break;
				}
				goto case "GpuNvidia";
			case "GpuNvidia":
			case "GpuAmd":
			{
				GpuSnap gpuSnap = new GpuSnap
				{
					Name = item3.Name
				};
				ISensor[] sensors4 = item3.Sensors;
				foreach (ISensor sensor4 in sensors4)
				{
					float? value4 = sensor4.Value;
					if (value4.HasValue)
					{
						string text5 = sensor4.SensorType.ToString();
						string name2 = sensor4.Name;
						if (text5 == "Load" && name2 == "GPU Core")
						{
							gpuSnap.Load = value4.Value;
						}
						if (text5 == "SmallData" && name2 == "GPU Memory Used")
						{
							gpuSnap.MemUsedMB = value4.Value;
						}
						if (text5 == "SmallData" && name2 == "GPU Memory Total")
						{
							gpuSnap.MemTotalMB = value4.Value;
						}
						if (text5 == "Temperature" && value4 > 0f && (!gpuSnap.Temp.HasValue || name2 == "GPU Core"))
						{
							gpuSnap.Temp = value4.Value;
						}
					}
				}
				if (gpuSnap.MemTotalMB > 0f)
				{
					gpuSnap.MemPct = gpuSnap.MemUsedMB / gpuSnap.MemTotalMB * 100f;
				}
				hwSnap.Gpus.Add(gpuSnap);
				break;
			}
			}
		}
		Dictionary<int, float?> physTemps = new Dictionary<int, float?>();
		if (_diskToolkitOk)
		{
			try
			{
				foreach (Storage storage in StorageManager.Storages)
				{
					try
					{
						storage.Update();
						SmartInfo smart = storage.Smart;
						if (smart != null && smart.Temperature.HasValue)
						{
							float num8 = smart.Temperature.Value;
							if (num8 > 0f && num8 < 120f && !physTemps.ContainsKey(storage.DriveNumber))
							{
								physTemps[storage.DriveNumber] = num8;
							}
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}
		Dictionary<string, float?> dictionary = new Dictionary<string, float?>(StringComparer.OrdinalIgnoreCase);
		foreach (IHardware item4 in _pc.Hardware)
		{
			if (item4.HardwareType.ToString() != "Storage")
			{
				continue;
			}
			float? value8 = null;
			ISensor[] sensors7 = item4.Sensors;
			foreach (ISensor sensor7 in sensors7)
			{
				if (sensor7.SensorType.ToString() == "Temperature" && sensor7.Value.HasValue && sensor7.Value > 0f && (!value8.HasValue || sensor7.Name == "Temperature"))
				{
					value8 = sensor7.Value;
				}
			}
			if (value8.HasValue)
			{
				dictionary[item4.Name] = value8;
			}
		}
		if (dictionary.Count > 0)
		{
			try
			{
				using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT Index, Model FROM Win32_DiskDrive");
				foreach (ManagementObject item5 in managementObjectSearcher.Get())
				{
					int key = Convert.ToInt32(item5["Index"]);
					string text7 = ((item5["Model"] != null) ? item5["Model"].ToString() : "").Trim();
					foreach (KeyValuePair<string, float?> item6 in dictionary)
					{
						string text8 = item6.Key.Trim();
						if (string.Equals(text7, text8, StringComparison.OrdinalIgnoreCase) || text7.IndexOf(text8, StringComparison.OrdinalIgnoreCase) >= 0 || text8.IndexOf(text7, StringComparison.OrdinalIgnoreCase) >= 0)
						{
							if (!physTemps.ContainsKey(key))
							{
								physTemps[key] = item6.Value;
							}
							break;
						}
					}
				}
			}
			catch
			{
			}
		}
		Dictionary<string, float?> dictionary2 = new Dictionary<string, float?>(StringComparer.OrdinalIgnoreCase);
		try
		{
			Dictionary<string, int> driveLetterToPhysicalIndex = GetDriveLetterToPhysicalIndex();
			foreach (int item7 in driveLetterToPhysicalIndex.Values.Distinct())
			{
				if (!physTemps.ContainsKey(item7))
				{
					float? value9 = ReadDiskTempByIndex(item7);
					if (value9.HasValue)
					{
						physTemps[item7] = value9;
					}
				}
			}
			if (driveLetterToPhysicalIndex.Values.Distinct().Any((int idx) => !physTemps.ContainsKey(idx)))
			{
				Dictionary<int, float?> smartTemps = GetSmartTemps();
				foreach (KeyValuePair<int, float?> item8 in smartTemps)
				{
					if (!physTemps.ContainsKey(item8.Key))
					{
						physTemps[item8.Key] = item8.Value;
					}
				}
			}
			foreach (KeyValuePair<string, int> item9 in driveLetterToPhysicalIndex)
			{
				physTemps.TryGetValue(item9.Value, out var value10);
				dictionary2[item9.Key] = value10;
			}
		}
		catch
		{
		}
		List<DiskSnap> list = new List<DiskSnap>();
		DriveInfo[] drives = DriveInfo.GetDrives();
		foreach (DriveInfo driveInfo in drives)
		{
			if (!driveInfo.IsReady)
			{
				continue;
			}
			try
			{
				long totalSize = driveInfo.TotalSize;
				long totalFreeSpace = driveInfo.TotalFreeSpace;
				long num11 = totalSize - totalFreeSpace;
				string text9 = driveInfo.RootDirectory.FullName.TrimEnd('\\', '/');
				string key2 = ((text9.Length >= 2) ? text9.Substring(0, 2).ToUpper() : text9);
				float readBps = 0f;
				float writeBps = 0f;
				if (_diskRd.TryGetValue(key2, out var value11))
				{
					try
					{
						readBps = value11.NextValue();
					}
					catch
					{
					}
				}
				if (_diskWr.TryGetValue(key2, out var value12))
				{
					try
					{
						writeBps = value12.NextValue();
					}
					catch
					{
					}
				}
				float? value13 = null;
				dictionary2.TryGetValue(key2, out value13);
				list.Add(new DiskSnap
				{
					Mount = text9,
					UsePct = ((totalSize > 0) ? ((float)num11 / (float)totalSize * 100f) : 0f),
					UsedGB = (float)((double)num11 / 1000000000.0),
					TotalGB = (float)((double)totalSize / 1000000000.0),
					ReadBps = readBps,
					WriteBps = writeBps,
					Temp = value13
				});
			}
			catch
			{
			}
		}
		hwSnap.Disks = list;
		lock (_lk)
		{
			_snap = hwSnap;
		}
	}

	public HwSnap Get()
	{
		lock (_lk)
		{
			return _snap;
		}
	}

	public void Dispose()
	{
		_stop = true;
		try
		{
			_pc.Close();
		}
		catch
		{
		}
	}
}
internal class SplashForm : Form
{
	private float _pulse;

	private bool _up = true;

	private float _ring;

	private float _ring2;

	private int _frame;

	private Random _rnd = new Random();

	private float[] _starX;

	private float[] _starY;

	private float[] _starPhase;

	private float[] _starSpeed;

	private const int STAR_COUNT = 25;

	private System.Windows.Forms.Timer _anim;

	public SplashForm()
	{
		base.FormBorderStyle = FormBorderStyle.None;
		base.StartPosition = FormStartPosition.CenterScreen;
		BackColor = Clr.BG;
		base.Size = new Size(180, 180);
		base.TopMost = true;
		base.ShowInTaskbar = false;
		base.Opacity = 1.0;
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.DoubleBuffer | ControlStyles.OptimizedDoubleBuffer, value: true);
		_pulse = 0.5f;
		_ring = 0f;
		_ring2 = 0f;
		_frame = 0;
		_starX = new float[25];
		_starY = new float[25];
		_starPhase = new float[25];
		_starSpeed = new float[25];
		for (int i = 0; i < 25; i++)
		{
			_starX[i] = (float)(_rnd.NextDouble() * 180.0);
			_starY[i] = (float)(_rnd.NextDouble() * 180.0);
			_starPhase[i] = (float)(_rnd.NextDouble() * Math.PI * 2.0);
			_starSpeed[i] = 0.03f + (float)(_rnd.NextDouble() * 0.07);
		}
		_anim = new System.Windows.Forms.Timer
		{
			Interval = 30
		};
		_anim.Tick += delegate
		{
			if (_up)
			{
				_pulse += 0.025f;
				if (_pulse >= 1f)
				{
					_pulse = 1f;
					_up = false;
				}
			}
			else
			{
				_pulse -= 0.025f;
				if (_pulse <= 0.25f)
				{
					_pulse = 0.25f;
					_up = true;
				}
			}
			_ring = (_ring + 3f) % 360f;
			_ring2 = (_ring2 + 1.8f) % 360f;
			_frame++;
			for (int j = 0; j < 25; j++)
			{
				_starPhase[j] += _starSpeed[j];
			}
			Invalidate();
		};
		_anim.Start();
		base.Load += delegate
		{
			nint hRgn = CreateEllipticRgn(0, 0, base.Width + 1, base.Height + 1);
			SetWindowRgn(base.Handle, hRgn, redraw: true);
		};
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear(Clr.BG);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
		int num = base.Width / 2;
		int num2 = base.Height / 2;
		for (int i = 0; i < 25; i++)
		{
			float num3 = (float)(Math.Sin(_starPhase[i]) * 0.5 + 0.5);
			int alpha = 25 + (int)(num3 * 102f);
			float num4 = 1.5f + num3 * 2f;
			Color baseColor = Color.FromArgb(255, 255, 230, 140);
			if (num3 > 0.4f)
			{
				int alpha2 = (int)(num3 * 60f);
				float num5 = num4 * 3f;
				using Pen pen = new Pen(Color.FromArgb(alpha2, baseColor), 0.7f);
				graphics.DrawLine(pen, _starX[i] - num5, _starY[i], _starX[i] + num5, _starY[i]);
				graphics.DrawLine(pen, _starX[i], _starY[i] - num5, _starX[i], _starY[i] + num5);
			}
			if (num3 > 0.5f)
			{
				float num6 = num4 * 2.5f;
				using SolidBrush brush = new SolidBrush(Color.FromArgb((int)(num3 * 30f), baseColor));
				graphics.FillEllipse(brush, _starX[i] - num6 / 2f, _starY[i] - num6 / 2f, num6, num6);
			}
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(alpha, baseColor));
			graphics.FillEllipse(brush2, _starX[i] - num4 / 2f, _starY[i] - num4 / 2f, num4, num4);
		}
		int num7 = num + 38;
		int num8 = num2 - 38;
		int num9 = 18;
		using (GraphicsPath graphicsPath = new GraphicsPath())
		{
			graphicsPath.AddEllipse(num7 - num9, num8 - num9, num9 * 2, num9 * 2);
			using PathGradientBrush pathGradientBrush = new PathGradientBrush(graphicsPath);
			pathGradientBrush.CenterPoint = new PointF(num7 - 3, num8 - 3);
			pathGradientBrush.CenterColor = Color.FromArgb(25, 220, 230, 240);
			pathGradientBrush.SurroundColors = new Color[1] { Color.FromArgb(0, 220, 230, 240) };
			graphics.FillPath(pathGradientBrush, graphicsPath);
		}
		using (Pen pen2 = new Pen(Color.FromArgb(18, 200, 210, 220), 0.8f))
		{
			graphics.DrawEllipse(pen2, num7 - num9, num8 - num9, num9 * 2, num9 * 2);
		}
		int num10 = 68;
		using (Pen pen3 = new Pen(Color.FromArgb((int)(140f * _pulse), Clr.BLUE), 2.5f)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round
		})
		{
			graphics.DrawArc(pen3, num - num10, num2 - num10, num10 * 2, num10 * 2, _ring, 100f);
		}
		using (Pen pen4 = new Pen(Color.FromArgb((int)(90f * _pulse), Clr.PURPLE), 2f)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round
		})
		{
			graphics.DrawArc(pen4, num - num10, num2 - num10, num10 * 2, num10 * 2, 0f - _ring2, 80f);
		}
		int num11 = 72;
		using (Pen pen5 = new Pen(Color.FromArgb((int)(50f * _pulse), Clr.CYAN), 1.2f)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round
		})
		{
			graphics.DrawArc(pen5, num - num11, num2 - num11, num11 * 2, num11 * 2, _ring * 0.7f + 60f, 60f);
		}
		int num12 = 52;
		using (GraphicsPath graphicsPath2 = new GraphicsPath())
		{
			graphicsPath2.AddEllipse(num - num12, num2 - num12, num12 * 2, num12 * 2);
			using PathGradientBrush pathGradientBrush2 = new PathGradientBrush(graphicsPath2);
			pathGradientBrush2.CenterColor = Color.FromArgb((int)(50f * _pulse), Clr.BLUE);
			pathGradientBrush2.SurroundColors = new Color[1] { Color.FromArgb(0, Clr.BLUE) };
			graphics.FillPath(pathGradientBrush2, graphicsPath2);
		}
		int num13 = 35;
		using (GraphicsPath graphicsPath3 = new GraphicsPath())
		{
			graphicsPath3.AddEllipse(num - num13, num2 - num13, num13 * 2, num13 * 2);
			using PathGradientBrush pathGradientBrush3 = new PathGradientBrush(graphicsPath3);
			pathGradientBrush3.CenterColor = Color.FromArgb((int)(30f * _pulse), Clr.PURPLE);
			pathGradientBrush3.SurroundColors = new Color[1] { Color.FromArgb(0, Clr.PURPLE) };
			graphics.FillPath(pathGradientBrush3, graphicsPath3);
		}
		int alpha3 = (int)(255f * Math.Max(0.5f, _pulse));
		using (Font font = new Font("Consolas", 56f, FontStyle.Bold))
		{
			using SolidBrush brush3 = new SolidBrush(Color.FromArgb(alpha3, Clr.BLUE));
			using StringFormat format = new StringFormat
			{
				Alignment = StringAlignment.Center,
				LineAlignment = StringAlignment.Center
			};
			RectangleF layoutRectangle = new RectangleF(0f, 4f, base.Width, base.Height);
			graphics.DrawString("Y", font, brush3, layoutRectangle, format);
		}
		using Font font2 = new Font("Segoe UI", 8.5f);
		using SolidBrush brush4 = new SolidBrush(Color.FromArgb((int)(160f * _pulse), Clr.FG2));
		using StringFormat format2 = new StringFormat
		{
			Alignment = StringAlignment.Center
		};
		graphics.DrawString("YUNSEUL-S", font2, brush4, new RectangleF(0f, base.Height - 32, base.Width, 20f), format2);
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		if (_anim != null)
		{
			_anim.Stop();
			_anim.Dispose();
		}
		base.OnFormClosing(e);
	}

	[DllImport("gdi32.dll")]
	private static extern nint CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

	[DllImport("gdi32.dll")]
	private static extern nint CreateEllipticRgn(int x1, int y1, int x2, int y2);

	[DllImport("user32.dll")]
	private static extern int SetWindowRgn(nint hwnd, nint hRgn, bool redraw);
}
internal class YunaS : Form
{
	private class GW
	{
		public Label Name;

		public Label Pct;

		public Label MPct;

		public Label VTag;

		public Label MInfo;

		public Label Temp;
	}

	private class DW
	{
		public Label Drive;

		public Label Pct;

		public Label Size;

		public Label IO;

		public Label Temp;
	}

	private const int W = 400;

	private const int HPAD = 6;

	private const int CW = 388;

	private const string POS = "pos.json";

	private HwCollector _hw;

	private System.Windows.Forms.Timer _timer;

	private NotifyIcon _tray;

	private Point _drag;

	private bool _sized;

	private int _btnL;

	private Panel _tb;

	private Panel _content;

	private Label _clock;

	private Panel _cpuHdr;

	private Panel _cpuCard;

	private Panel _gpuHdr;

	private Panel _gpuPanel;

	private Panel _diskHdr;

	private Panel _diskPanel;

	private Label _cpuName;

	private Label _cpuCore;

	private Label _cpuFreq;

	private Label _cpuTemp;

	private List<GW> _gpuW = new List<GW>();

	private Dictionary<string, DW> _diskW = new Dictionary<string, DW>();

	public event Action DataReady;

	public YunaS()
	{
		_hw = new HwCollector();
		base.FormBorderStyle = FormBorderStyle.None;
		BackColor = Clr.BG;
		base.TopMost = true;
		base.Opacity = 1.0;
		base.StartPosition = FormStartPosition.Manual;
		base.ShowInTaskbar = false;
		base.AutoScaleMode = AutoScaleMode.None;
		base.ClientSize = new Size(400, 600);
		BuildTray();
		Build();
		string cpuName = _hw.GetCpuName();
		if (cpuName.Length > 0)
		{
			_cpuName.Text = ((cpuName.Length > 40) ? (cpuName.Substring(0, 38) + "..") : cpuName);
		}
		int[] array = LoadPos();
		SetDesktopLocation(array[0], array[1]);
		base.Load += delegate
		{
			Round();
		};
		base.Resize += delegate
		{
			Round();
		};
		_timer = new System.Windows.Forms.Timer
		{
			Interval = 1000
		};
		_timer.Tick += delegate
		{
			Tick();
		};
		_timer.Start();
	}

	private void BuildTray()
	{
		_tray = new NotifyIcon
		{
			Text = "YUNSEUL-S",
			Visible = false
		};
		_tray.Icon = MakeTrayIcon();
		_tray.MouseClick += delegate(object? s, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				ShowFromTray();
			}
		};
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
		contextMenuStrip.Items.Add("YUNSEUL-S 열기", null, delegate
		{
			ShowFromTray();
		});
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.Add("종료", null, delegate
		{
			CleanExit();
		});
		_tray.ContextMenuStrip = contextMenuStrip;
	}

	private void HideToTray()
	{
		SavePos(base.Left, base.Top);
		Hide();
		_tray.Visible = true;
	}

	private void ShowFromTray()
	{
		_tray.Visible = false;
		int[] array = LoadPos();
		SetDesktopLocation(array[0], array[1]);
		Show();
		base.WindowState = FormWindowState.Normal;
		Activate();
		Round();
	}

	private void CleanExit()
	{
		if (_timer != null)
		{
			_timer.Stop();
		}
		if (_hw != null)
		{
			_hw.Dispose();
		}
		if (_tray != null)
		{
			_tray.Visible = false;
			_tray.Dispose();
		}
		Close();
	}

	private static Icon MakeTrayIcon()
	{
		Bitmap bitmap = new Bitmap(16, 16);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.Clear(Color.Transparent);
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(13, 17, 23)))
			{
				graphics.FillRectangle(brush, 0, 0, 16, 16);
			}
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(88, 166, 255));
			graphics.FillPolygon(brush2, new Point[8]
			{
				new Point(3, 2),
				new Point(7, 7),
				new Point(11, 2),
				new Point(13, 2),
				new Point(8, 9),
				new Point(8, 14),
				new Point(6, 14),
				new Point(6, 9)
			});
		}
		return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
	}

	private void Build()
	{
		_tb = new Panel
		{
			Height = 26,
			BackColor = Color.FromArgb(1, 4, 9),
			Dock = DockStyle.Top
		};
		_tb.MouseDown += delegate(object? s, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				_drag = e.Location;
			}
		};
		_tb.MouseMove += delegate(object? s, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				Point point = PointToScreen(e.Location);
				SetDesktopLocation(point.X - _drag.X, point.Y - _drag.Y);
				SavePos(base.Left, base.Top);
			}
		};
		base.Controls.Add(_tb);
		Lbl(_tb, "  [▓]  YUNSEUL-S", Clr.BLUE, new Font("Consolas", 9f, FontStyle.Bold), 0, 5);
		Label label = new Label
		{
			Text = "  X  ",
			BackColor = Color.FromArgb(110, 26, 26),
			ForeColor = Clr.FG,
			Font = new Font("Consolas", 9f, FontStyle.Bold),
			AutoSize = true,
			Cursor = Cursors.Hand,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right),
			Top = 2
		};
		label.Left = 400 - label.PreferredWidth - 2;
		label.Click += delegate
		{
			CleanExit();
		};
		_tb.Controls.Add(label);
		Label label2 = new Label
		{
			Text = "  —  ",
			BackColor = Color.FromArgb(45, 51, 59),
			ForeColor = Clr.FG,
			Font = new Font("Consolas", 9f, FontStyle.Bold),
			AutoSize = true,
			Cursor = Cursors.Hand,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right),
			Top = 2
		};
		label2.Left = label.Left - label2.PreferredWidth - 2;
		label2.Click += delegate
		{
			HideToTray();
		};
		_tb.Controls.Add(label2);
		_btnL = label2.Left;
		_clock = new Label
		{
			BackColor = Color.Transparent,
			ForeColor = Clr.FG,
			Font = new Font("Consolas", 8f),
			AutoSize = true,
			Top = 6
		};
		_tb.Controls.Add(_clock);
		_content = new Panel
		{
			BackColor = Clr.BG,
			Left = 0,
			Top = _tb.Height,
			Width = 400
		};
		base.Controls.Add(_content);
		_cpuHdr = SecHdr("CPU", Clr.BLUE);
		_cpuCard = BuildCpu();
		_gpuHdr = SecHdr("GPU", Clr.GREEN);
		_gpuPanel = new Panel
		{
			BackColor = Clr.BG,
			Width = 388,
			Height = 0
		};
		_diskHdr = SecHdr("DISK", Clr.ORANGE);
		_diskPanel = new Panel
		{
			BackColor = Clr.BG,
			Width = 388,
			Height = 0
		};
		Control[] array = new Control[6] { _cpuHdr, _cpuCard, _gpuHdr, _gpuPanel, _diskHdr, _diskPanel };
		foreach (Control value in array)
		{
			_content.Controls.Add(value);
		}
		Layout2();
		EnableDrag(this);
	}

	private void EnableDrag(Control parent)
	{
		foreach (Control c in parent.Controls)
		{
			if (c.Cursor == Cursors.Hand) continue;
			c.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) _drag = PointToClient(Cursor.Position); };
			c.MouseMove += (s, e) => {
				if (e.Button != MouseButtons.Left) return;
				Point cur = Cursor.Position;
				SetDesktopLocation(cur.X - _drag.X, cur.Y - _drag.Y);
				SavePos(Left, Top);
			};
			EnableDrag(c);
		}
	}

	private void Layout2()
	{
		int num = 6;
		Place(_cpuHdr, num);
		num += _cpuHdr.Height + 2;
		Place(_cpuCard, num);
		num += _cpuCard.Height + 5;
		Place(_gpuHdr, num);
		num += _gpuHdr.Height + 2;
		Place(_gpuPanel, num);
		num += Math.Max(0, _gpuPanel.Height) + 5;
		Place(_diskHdr, num);
		num += _diskHdr.Height + 2;
		Place(_diskPanel, num);
		num += Math.Max(0, _diskPanel.Height) + 4;
		_content.Height = num + 2;
	}

	private void Place(Control c, int y)
	{
		c.Left = 6;
		c.Top = y;
		c.Width = 388;
	}

	private Panel SecHdr(string title, Color col)
	{
		Panel panel = new Panel
		{
			BackColor = Clr.BG,
			Height = 18,
			Width = 388
		};
		panel.Controls.Add(new Panel
		{
			BackColor = col,
			Left = 0,
			Top = 3,
			Width = 3,
			Height = 12
		});
		panel.Controls.Add(new Label
		{
			Text = title,
			ForeColor = col,
			BackColor = Color.Transparent,
			Font = new Font("Segoe UI", 9f, FontStyle.Bold),
			AutoSize = true,
			Left = 8,
			Top = 1
		});
		return panel;
	}

	private Panel BuildCpu()
	{
		Panel panel = Card(388);
		int num = 8;
		int num2 = 6;
		_cpuName = new Label
		{
			Text = "",
			ForeColor = Clr.FG2,
			BackColor = Clr.CARD,
			Font = new Font("Segoe UI", 8f),
			AutoSize = true,
			Left = num,
			Top = num2
		};
		panel.Controls.Add(_cpuName);
		Panel panel2 = TempBox(panel, 325, num2, out _cpuTemp);
		num2 += _cpuName.PreferredHeight + 4;
		_cpuCore = new Label
		{
			Text = "C--  --%",
			ForeColor = Clr.FG,
			BackColor = Color.Transparent,
			Font = new Font("Consolas", 11f, FontStyle.Bold),
			AutoSize = true,
			Left = 4,
			Top = 1
		};
		Panel panel3 = new Panel
		{
			BackColor = Clr.BORDER,
			Left = num,
			Top = num2
		};
		panel3.Controls.Add(_cpuCore);
		panel3.Width = _cpuCore.PreferredWidth + 8;
		panel3.Height = _cpuCore.PreferredHeight + 2;
		panel.Controls.Add(panel3);
		_cpuFreq = new Label
		{
			Text = "-- GHz",
			ForeColor = Clr.CYAN,
			BackColor = Color.Transparent,
			Font = new Font("Consolas", 11f),
			AutoSize = true,
			Left = 4,
			Top = 1
		};
		int left = num + panel3.Width + 5;
		int width = _cpuFreq.PreferredWidth + 53;
		Panel panel4 = new Panel
		{
			BackColor = Clr.BORDER,
			Left = left,
			Top = num2,
			Width = width,
			Height = panel3.Height
		};
		panel4.Controls.Add(_cpuFreq);
		panel.Controls.Add(panel4);
		num2 += panel3.Height + 5;
		panel.Height = num2;
		panel2.Top = (panel.Height - panel2.Height) / 2;
		return panel;
	}

	private void EnsureGpu(int n)
	{
		if (_gpuW.Count >= n)
		{
			return;
		}
		while (_gpuW.Count < n)
		{
			if (_gpuW.Count > 0)
			{
				_gpuPanel.Controls.Add(new Panel
				{
					BackColor = Clr.BORDER,
					Left = 6,
					Top = _gpuPanel.Height,
					Width = 376,
					Height = 1
				});
				_gpuPanel.Height += 3;
			}
			Panel panel = Card(388);
			panel.Top = _gpuPanel.Height;
			int num = 10;
			int num2 = 4;
			Label val;
			Panel panel2 = TempBox(panel, 325, num2, out val);
			Label label = Lbl(panel, "GPU", Clr.FG2, new Font("Segoe UI", 8f), num, num2 + 1);
			num2 = label.Top + label.PreferredHeight + 3;
			int left = num;
			int x = num + 55;
			int x2 = num + 110;
			int x3 = num + 148;
			int width = TextRenderer.MeasureText("100%", new Font("Consolas", 11f, FontStyle.Bold)).Width;
			Label label2 = new Label
			{
				Text = "0%",
				ForeColor = Clr.FG,
				BackColor = Color.Transparent,
				Font = new Font("Consolas", 11f, FontStyle.Bold),
				Width = width,
				TextAlign = ContentAlignment.MiddleRight,
				Left = left,
				Top = num2
			};
			panel.Controls.Add(label2);
			Label mPct = Lbl(panel, "0%", Clr.CYAN, new Font("Consolas", 11f, FontStyle.Bold), x, num2);
			Label vTag = Lbl(panel, "VRAM", Clr.FG2, new Font("Segoe UI", 8f), x2, num2 + 4);
			Label mInfo = Lbl(panel, "", Clr.FG2, new Font("Segoe UI", 8f), x3, num2 + 4);
			num2 = (panel.Height = num2 + (label2.PreferredHeight + 5));
			panel2.Top = (panel.Height - panel2.Height) / 2;
			_gpuPanel.Controls.Add(panel);
			_gpuPanel.Height += num2 + 1;
			_gpuW.Add(new GW
			{
				Name = label,
				Pct = label2,
				MPct = mPct,
				VTag = vTag,
				MInfo = mInfo,
				Temp = val
			});
		}
		Layout2();
	}

	private void EnsureDisk(string mount)
	{
		if (!_diskW.ContainsKey(mount))
		{
			Color foreColor = Clr.DISK[_diskW.Count % Clr.DISK.Length];
			Font font = new Font("Consolas", 8.5f);
			Font font2 = new Font("Consolas", 8.5f, FontStyle.Bold);
			int height = _diskPanel.Height;
			int num = TextRenderer.MeasureText("Wg", font).Height + 4;
			int num2 = 4;
			Label label = new Label
			{
				Text = mount,
				ForeColor = foreColor,
				BackColor = Color.Transparent,
				Font = font2,
				AutoSize = true,
				Left = num2,
				Top = height + 2
			};
			_diskPanel.Controls.Add(label);
			num2 += TextRenderer.MeasureText("W:", font2).Width + 3;
			int num3 = 30;
			Label label2 = new Label
			{
				Text = "0%",
				ForeColor = Clr.FG,
				BackColor = Color.Transparent,
				Font = font2,
				Width = num3,
				Height = num,
				TextAlign = ContentAlignment.MiddleRight,
				Left = num2,
				Top = height + 1
			};
			_diskPanel.Controls.Add(label2);
			num2 += num3;
			int num4 = 125;
			Label label3 = new Label
			{
				Text = "",
				ForeColor = Clr.FG2,
				BackColor = Color.Transparent,
				Font = font,
				Width = num4,
				Height = num,
				TextAlign = ContentAlignment.MiddleRight,
				Left = num2,
				Top = height + 1
			};
			_diskPanel.Controls.Add(label3);
			num2 += num4 + 3;
			Label label4 = new Label
			{
				Text = "",
				ForeColor = Clr.CYAN,
				BackColor = Color.Transparent,
				Font = font,
				AutoSize = true,
				Left = num2,
				Top = height + 2
			};
			_diskPanel.Controls.Add(label4);
			Label label5 = new Label
			{
				Text = "",
				ForeColor = Clr.GREEN,
				BackColor = Color.Transparent,
				Font = font,
				AutoSize = true,
				Left = 338,
				Top = height + 2
			};
			_diskPanel.Controls.Add(label5);
			_diskPanel.Height += num;
			_diskW[mount] = new DW
			{
				Drive = label,
				Pct = label2,
				Size = label3,
				IO = label4,
				Temp = label5
			};
			Layout2();
		}
	}

	private void Tick()
	{
		if (!base.TopMost)
		{
			base.TopMost = true;
		}
		_clock.Text = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
		_clock.Left = _btnL - _clock.PreferredWidth - 6;
		HwSnap hwSnap = _hw.Get();
		if (hwSnap.Cpu == null)
		{
			return;
		}
		CpuSnap cpu = hwSnap.Cpu;
		if (!string.IsNullOrEmpty(cpu.Name) && _cpuName.Text.Length == 0)
		{
			_cpuName.Text = ((cpu.Name.Length > 40) ? (cpu.Name.Substring(0, 38) + "..") : cpu.Name);
		}
		if (cpu.CoreLoad != null && cpu.TopCore.HasValue)
		{
			float num = cpu.CoreLoad[cpu.TopCore.Value];
			_cpuCore.ForeColor = Clr.Usage(num);
			_cpuCore.Text = "C" + cpu.TopCore.Value.ToString("D2") + "  " + (int)num + "%";
		}
		if (cpu.Freq.HasValue)
		{
			_cpuFreq.Text = (cpu.BoostCore.HasValue ? ("C" + cpu.BoostCore.Value.ToString("D2") + "  " + cpu.Freq.Value.ToString("0.00") + "GHz") : (cpu.Freq.Value.ToString("0.00") + " GHz"));
		}
		_cpuTemp.ForeColor = Clr.Temp(cpu.Temp);
		_cpuTemp.Text = ((cpu.Temp.HasValue && cpu.Temp > 0f) ? ((int)cpu.Temp.Value).ToString() : "--");
		EnsureGpu(hwSnap.Gpus.Count);
		for (int i = 0; i < hwSnap.Gpus.Count; i++)
		{
			GpuSnap gpuSnap = hwSnap.Gpus[i];
			GW gW = _gpuW[i];
			gW.Name.Text = gpuSnap.Name;
			gW.Pct.ForeColor = Clr.Usage(gpuSnap.Load);
			gW.Pct.Text = (int)gpuSnap.Load + "%";
			gW.MPct.Text = (int)gpuSnap.MemPct + "%";
			gW.Temp.ForeColor = Clr.Temp(gpuSnap.Temp);
			gW.Temp.Text = ((gpuSnap.Temp.HasValue && gpuSnap.Temp > 0f) ? ((int)gpuSnap.Temp.Value).ToString() : "--");
			if (gpuSnap.MemTotalMB > 0f)
			{
				gW.MInfo.Text = Fmt.Bytes(gpuSnap.MemUsedMB * 1024f * 1024f) + "/" + Fmt.Bytes(gpuSnap.MemTotalMB * 1024f * 1024f);
			}
		}
		foreach (DiskSnap disk in hwSnap.Disks)
		{
			EnsureDisk(disk.Mount);
			DW dW = _diskW[disk.Mount];
			dW.Pct.ForeColor = Clr.Usage(disk.UsePct);
			dW.Pct.Text = (int)disk.UsePct + "%";
			dW.Size.Text = Fmt.Bytes((double)disk.UsedGB * 1000000000.0) + "/" + Fmt.Bytes((double)disk.TotalGB * 1000000000.0);
			dW.IO.Text = "R:" + Fmt.Speed(disk.ReadBps) + " W:" + Fmt.Speed(disk.WriteBps);
			bool ioActive = disk.ReadBps > 0 || disk.WriteBps > 0;
			dW.IO.BackColor = ioActive ? Color.FromArgb(255, 200, 180, 0) : Color.Transparent;
			dW.IO.ForeColor = ioActive ? Color.Black : Clr.CYAN;
			if (disk.Temp.HasValue && disk.Temp > 0f)
			{
				dW.Temp.Text = (int)disk.Temp.Value + "°C";
				dW.Temp.ForeColor = Clr.Temp(disk.Temp);
				dW.Temp.Visible = true;
			}
			else
			{
				dW.Temp.Visible = false;
			}
		}
		if (!_sized && hwSnap.Disks.Count > 0)
		{
			_sized = true;
			FitHeight();
			this.DataReady?.Invoke();
		}
	}

	private void FitHeight()
	{
		Layout2();
		int num = _tb.Height + _content.Height + 4;
		int height = Screen.PrimaryScreen.Bounds.Height;
		if (num > height - 10)
		{
			num = height - 10;
		}
		int num2 = base.Top;
		if (num2 + num > height - 4)
		{
			num2 = Math.Max(0, height - num - 4);
		}
		SetDesktopLocation(base.Left, num2);
		base.Height = num;
		SavePos(base.Left, base.Top);
		Round();
	}

	private Panel Card(int w)
	{
		return new Panel
		{
			BackColor = Clr.CARD,
			Width = w,
			Height = 1
		};
	}

	private Panel TempBox(Control parent, int x, int y, out Label val)
	{
		Panel panel = new Panel
		{
			BackColor = Clr.BORDER,
			Left = x,
			Top = y,
			Width = 59
		};
		val = new Label
		{
			Text = "--",
			ForeColor = Clr.GREEN,
			BackColor = Color.Transparent,
			Font = new Font("Consolas", 15f, FontStyle.Bold),
			AutoSize = true,
			Left = 3,
			Top = 2
		};
		panel.Controls.Add(val);
		panel.Controls.Add(new Label
		{
			Text = "°C",
			ForeColor = Clr.FG2,
			BackColor = Color.Transparent,
			Font = new Font("Segoe UI", 7f),
			AutoSize = true,
			Left = val.PreferredWidth + 4,
			Top = val.Top + 4
		});
		panel.Height = val.PreferredHeight + 5;
		parent.Controls.Add(panel);
		return panel;
	}

	private Label Lbl(Control parent, string text, Color fg, Font font, int x, int y)
	{
		Label label = new Label
		{
			Text = text,
			ForeColor = fg,
			BackColor = Color.Transparent,
			Font = font,
			AutoSize = true,
			Left = x,
			Top = y
		};
		parent.Controls.Add(label);
		return label;
	}

	[DllImport("gdi32.dll")]
	private static extern nint CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

	[DllImport("user32.dll")]
	private static extern int SetWindowRgn(nint hwnd, nint hRgn, bool redraw);

	[DllImport("user32.dll")]
	private static extern nint GetAncestor(nint hwnd, uint flags);

	private void Round()
	{
		try
		{
			nint num = GetAncestor(base.Handle, 2u);
			if (num == IntPtr.Zero)
			{
				num = base.Handle;
			}
			SetWindowRgn(num, CreateRoundRectRgn(0, 0, base.Width + 1, base.Height + 1, 16, 16), redraw: true);
		}
		catch
		{
		}
	}

	private int[] LoadPos()
	{
		try
		{
			string input = File.ReadAllText(Path.Combine(Program.BASE, POS));
			Match match = Regex.Match(input, "\"x\"\\s*:\\s*(-?\\d+)");
			Match match2 = Regex.Match(input, "\"y\"\\s*:\\s*(-?\\d+)");
			if (match.Success && match2.Success)
			{
				int num = int.Parse(match.Groups[1].Value);
				int num2 = int.Parse(match2.Groups[1].Value);
				int width = Screen.PrimaryScreen.Bounds.Width;
				int height = Screen.PrimaryScreen.Bounds.Height;
				if (num >= 0 && num2 >= 0 && num + 400 <= width + 50 && num2 + 200 <= height + 50)
				{
					return new int[2] { num, num2 };
				}
			}
		}
		catch
		{
		}
		return new int[2] { 20, 80 };
	}

	private static void SavePos(int x, int y)
	{
		try
		{
			File.WriteAllText(Path.Combine(Program.BASE, POS), "{\"x\":" + x + ",\"y\":" + y + "}");
		}
		catch
		{
		}
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		if (_timer != null)
		{
			_timer.Stop();
		}
		if (_hw != null)
		{
			_hw.Dispose();
		}
		if (_tray != null)
		{
			_tray.Visible = false;
			_tray.Dispose();
		}
		base.OnFormClosing(e);
	}
}
