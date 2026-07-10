using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Linq;

namespace TurzxPatcher
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--worker")
            {
                string dir = args.Length > 1 ? args[1] : Directory.GetCurrentDirectory();
                RunWorker(dir);
                return;
            }

            // Terminate L-Connect processes to prevent conflicts
            TerminateLianLiProcesses();

            string turzxDir = null;

            if (args.Length > 0 && args[0] == "--dir" && args.Length > 1)
            {
                turzxDir = Path.GetFullPath(args[1]);
            }
            else if (args.Length > 0 && args[0] == "--help")
            {
                PrintHelp();
                return;
            }
            else
            {
                string selfDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string exeFile = File.Exists(Path.Combine(selfDir, "TURZX.exe")) ? "TURZX.exe" : "TURZX.exe.bak2";
                if (File.Exists(Path.Combine(selfDir, exeFile)))
                {
                    turzxDir = selfDir;
                }
                else
                {
                    Console.Error.WriteLine("Usage: TurzxPatcher.exe --dir \"C:\\Path\\To\\TURZX\"");
                    Console.Error.WriteLine("   Or: Place TurzxPatcher.exe next to TURZX.exe");
                    Console.Error.WriteLine("   Or: TurzxPatcher.exe --help");
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Error: TURZX.exe not found in current directory.");
                    Console.Error.WriteLine("       Specify the directory with --dir.");
                    Console.ReadLine();
                    return;
                }
            }

            string selfPath = Assembly.GetExecutingAssembly().Location;
            string selfName = Path.GetFileName(selfPath);
            string targetPath = Path.Combine(turzxDir, selfName);

            if (!File.Exists(targetPath))
                File.Copy(selfPath, targetPath, true);

            foreach (var dep in Directory.GetFiles(Path.GetDirectoryName(selfPath), "*.dll"))
            {
                string targetFile = Path.Combine(turzxDir, Path.GetFileName(dep));
                if (!File.Exists(targetFile))
                    File.Copy(dep, targetFile, true);
            }

            var psi = new ProcessStartInfo
            {
                FileName = targetPath,
                Arguments = "--worker \"" + turzxDir + "\"",
                WorkingDirectory = turzxDir,
                UseShellExecute = false
            };
            Process.Start(psi);
        }

        static void PrintHelp()
        {
            Console.WriteLine("TurzxPatcher - A088 Display Recognition Patch for TURZX");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  TurzxPatcher.exe --dir \"C:\\Path\\To\\TURZX\"");
            Console.WriteLine("    Run from anywhere, specify TURZX directory.");
            Console.WriteLine();
            Console.WriteLine("  (place TurzxPatcher.exe next to TURZX.exe, then just double-click)");
            Console.WriteLine("    Auto-detects TURZX.exe in its own directory.");
            Console.WriteLine();
            Console.WriteLine("What it does:");
            Console.WriteLine("  - Patches the A088 display template's resolution to 4801920");
            Console.WriteLine("    so the Monitor correctly assigns a theme to it.");
            Console.WriteLine("  - Fixes assembly version mismatches in .turtheme files.");
            Console.WriteLine("  - Runs TURZX with correct base path (fixes theme directory lookup).");
            Console.WriteLine();
            Console.WriteLine("Note: Run TURZX.exe as Administrator for sensor data access.");
        }

      static void TerminateLianLiProcesses()
        {
            // Terminate L-Connect processes
            string[] processNames = { "L-Connect 3", "LConnect3" };
            
            foreach (var procName in processNames)
            {
                try
                {
                    var processes = Process.GetProcessesByName(procName);
                    foreach (var process in processes)
                    {
                        Console.WriteLine($"Terminating L-Connect process: {procName} (ID: {process.Id})");
                        process.Kill();
                        process.WaitForExit(5000);
                        if (!process.HasExited)
                        {
                            Console.WriteLine($"Failed to terminate {procName}, forcing...");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error terminating {procName}: {ex.Message}");
                }
            }
            
            // Terminate L-Connect services
            string[] serviceNames = { "LConnectService", "LConnectServiceWatcher" };
            
            foreach (var serviceName in serviceNames)
            {
                try
                {
                    var services = System.ServiceProcess.ServiceController.GetServices()
                        .Where(s => s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
                    
                    foreach (var service in services)
                    {
                        Console.WriteLine($"Stopping L-Connect service: {serviceName}");
                        try
                        {
                            service.Stop();
                            service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                            Console.WriteLine($"Successfully stopped service: {serviceName}");
                        }
                        catch (Exception stopEx)
                        {
                            Console.WriteLine($"Failed to stop service {serviceName}: {stopEx.Message}");
                            
                            // Try sc.exe as fallback
                            try
                            {
                                var psi = new ProcessStartInfo
                                {
                                    FileName = "sc.exe",
                                    Arguments = $"stop {serviceName}",
                                    RedirectStandardOutput = true,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                };
                                
                                using (var proc = Process.Start(psi))
                                {
                                    proc.WaitForExit(10000);
                                    var output = proc.StandardOutput.ReadToEnd();
                                    if (proc.ExitCode == 0)
                                    {
                                        Console.WriteLine($"Service {serviceName} stopped via sc.exe");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"sc.exe failed to stop {serviceName}: {output}");
                                    }
                                }
                            }
                            catch (Exception scEx)
                            {
                                Console.WriteLine($"Failed to stop service via sc.exe: {scEx.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error accessing service {serviceName}: {ex.Message}");
                }
            }
            
            Console.WriteLine("L-Connect termination complete");
        }

        static void LoadAndApplyPlugins(Assembly turzxAssembly, string turzxDir)
        {
            string patchesDir = Path.Combine(turzxDir, "patches");
            if (!Directory.Exists(patchesDir))
            {
                Console.WriteLine("Kein 'patches' Ordner gefunden (" + patchesDir + ") - keine externen Plugins geladen.");
                return;
            }

            var dllFiles = Directory.GetFiles(patchesDir, "*.dll");
            if (dllFiles.Length == 0)
            {
                Console.WriteLine("'patches' Ordner ist leer - keine externen Plugins geladen.");
                return;
            }

            Console.WriteLine("Suche Plugins in: " + patchesDir + " (" + dllFiles.Length + " DLL(s) gefunden)");

            foreach (var dllPath in dllFiles)
            {
                try
                {
                    Console.WriteLine("Lade Plugin-Assembly: " + Path.GetFileName(dllPath));
                    var pluginAsm = Assembly.LoadFrom(dllPath);

                    var pluginTypes = pluginAsm.GetTypes()
                        .Where(t => typeof(TurzxShared.Plugins.ITurzxPatch).IsAssignableFrom(t)
                                    && !t.IsInterface
                                    && !t.IsAbstract)
                        .ToList();

                    if (pluginTypes.Count == 0)
                    {
                        Console.WriteLine("  -> Keine ITurzxPatch-Implementierung in " + Path.GetFileName(dllPath) + " gefunden. Ueberspringe.");
                        continue;
                    }

                    foreach (var pluginType in pluginTypes)
                    {
                        try
                        {
                            var instance = (TurzxShared.Plugins.ITurzxPatch)Activator.CreateInstance(pluginType);
                            Console.WriteLine("  -> Wende Plugin an: " + instance.Name + " (InterfaceVersion=" + instance.InterfaceVersion + ")");

                            const int HostInterfaceVersion = 1;
                            if (instance.InterfaceVersion != HostInterfaceVersion)
                            {
                                Console.WriteLine("  -> WARNUNG: Plugin-Interface-Version (" + instance.InterfaceVersion +
                                                   ") weicht von Host-Version (" + HostInterfaceVersion + ") ab. " +
                                                   "Fahre trotzdem fort, aber es koennen Inkompatibilitaeten auftreten.");
                            }

                            instance.Apply(turzxAssembly, turzxDir);
                            Console.WriteLine("  -> Plugin '" + instance.Name + "' erfolgreich angewendet.");
                        }
                        catch (Exception applyEx)
                        {
                            Console.WriteLine("  -> FEHLER beim Anwenden von Plugin-Typ " + pluginType.FullName + ": " + applyEx.Message);
                            if (applyEx.InnerException != null)
                                Console.WriteLine("     Inner: " + applyEx.InnerException.Message);
                        }
                    }
                }
                catch (Exception loadEx)
                {
                    Console.WriteLine("FEHLER beim Laden von " + Path.GetFileName(dllPath) + ": " + loadEx.Message);
                }
            }
        }

        static void RunWorker(string dir)
        {
            // Mutex: Prevent multiple TURZX host processes simultaneously
            const string MutexName = @"Global\TurzxHostActive";
            bool mutexCreated;
            var hostMutex = new System.Threading.Mutex(true, MutexName, out mutexCreated);
            if (!mutexCreated)
            {
                Console.Error.WriteLine("Ein anderer TURZX-Host-Prozess laeuft bereits (Mutex '" + MutexName + "' ist belegt).");
                Console.Error.WriteLine("Bitte schliesse zuerst den anderen TurzxPatcher- oder TurzxSensorLauncher-Prozess,");
                Console.Error.WriteLine("bevor du TURZX erneut startest. Gleichzeitiges Hosten von TURZX.exe durch");
                Console.Error.WriteLine("mehrere Loader-Prozesse ist nicht unterstuetzt und fuehrt zu USB-Konflikten.");
                Console.ReadLine();
                return;
            }
            // hostMutex bewusst NICHT disposen/releasen bis Prozessende -
            // AppDomain-Unload/Prozessende gibt den Mutex automatisch frei.
            string exePath = Path.Combine(dir, "TURZX.exe");
            if (!File.Exists(exePath))
            {
                exePath = Path.Combine(dir, "TURZX.exe.bak2");
                if (!File.Exists(exePath))
                {
                    Console.Error.WriteLine("Error: TURZX.exe not found in " + dir);
                    Console.ReadLine();
                    return;
                }
            }

            Environment.CurrentDirectory = dir;
            Directory.SetCurrentDirectory(dir);

            var asm = Assembly.LoadFrom(exePath);

            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                try
                {
                    var an = new AssemblyName(e.Name);
                    if (an.Name == "UsbMonitorL")
                        return asm;
                    var path = Path.Combine(dir, an.Name + ".dll");
                    if (File.Exists(path)) return Assembly.LoadFrom(path);
                    path = Path.Combine(dir, an.Name);
                    if (File.Exists(path)) return Assembly.LoadFrom(path);
                }
                catch { }
                return null;
            };

            try
            {
                var customManager = new AppDomainManager();
                var eaField = typeof(AppDomainManager).GetField("m_entryAssembly",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                eaField.SetValue(customManager, asm);

                var dmField = typeof(AppDomain).GetField("_domainManager",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                dmField.SetValue(AppDomain.CurrentDomain, customManager);

                Console.WriteLine("Entry: " + Assembly.GetEntryAssembly().GetName().Name);
            }
            catch { }

            try
            {
                var verType = asm.GetType("UsbMonitorL.Version");
                var sdProp = verType.GetProperty("ScreenDevs",
                    BindingFlags.Static | BindingFlags.Public);
                var sdList = sdProp.GetValue(null) as System.Collections.IList;
                foreach (var sd in sdList)
                {
                    var dc = sd.GetType().GetProperty("dev_code").GetValue(sd) as string;
                    if (dc == "VID_1CBE&PID_A088")
                    {
                        Console.WriteLine("Found A088 device template");
                        
                        // Get all properties to understand the structure
                        var sdType = sd.GetType();
                        var props = sdType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                        
                        Console.WriteLine("Device template properties:");
                        foreach (var prop in props)
                        {
                            try
                            {
                                var val = prop.GetValue(sd);
                                Console.WriteLine($"  {prop.Name}: {val}");
                            }
                            catch { }
                        }
                        
                      // Set the resolution
                        sd.GetType().GetProperty("res").SetValue(sd, "4801920");
                        Console.WriteLine("Patched A088 -> res=4801920");
                        
                        // Fix width/height for portrait orientation (swap them)
                        var widthProp = sdType.GetProperty("width");
                        var heightProp = sdType.GetProperty("height");
                        
                        if (widthProp != null && heightProp != null)
                        {
                            var currentWidth = widthProp.GetValue(sd);
                            var currentHeight = heightProp.GetValue(sd);
                            
                            Console.WriteLine($"Current dimensions: width={currentWidth}, height={currentHeight}");
                            
                            // Swap width and height for portrait orientation
                            if (currentWidth.ToString() == "1920" && currentHeight.ToString() == "480")
                            {
                                try
                                {
                                    widthProp.SetValue(sd, 480);
                                    heightProp.SetValue(sd, 1920);
                                    Console.WriteLine("Fixed dimensions: width=480, height=1920 (portrait)");
                                }
                                catch (Exception dimEx)
                                {
                                    Console.WriteLine($"Failed to set dimensions: {dimEx.Message}");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("Width/Height properties not found");
                        }
                        
                        // Set IsPotrit to True for portrait orientation
                        var isPotritProp = sdType.GetProperty("IsPotrit");
                        if (isPotritProp != null)
                        {
                            var currentVal = isPotritProp.GetValue(sd);
                            Console.WriteLine($"Current IsPotrit value: {currentVal}");
                            
                            if (currentVal.ToString() != "True")
                            {
                                try
                                {
                                    isPotritProp.SetValue(sd, true);
                                    Console.WriteLine("Set IsPotrit to True (portrait mode)");
                                }
                                catch (Exception potritEx)
                                {
                                    Console.WriteLine($"Failed to set IsPotrit: {potritEx.Message}");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("IsPotrit property not found");
                        }
                        
                        // Look for background rotation properties to fix 90-degree offset
                        var bgProps = sdType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(p => p.Name.ToLower().Contains("background") || p.Name.ToLower().Contains("bg"))
                            .ToList();
                        
                        if (bgProps.Any())
                        {
                            Console.WriteLine("Found background-related properties:");
                            foreach (var prop in bgProps)
                            {
                                try
                                {
                                    var val = prop.GetValue(sd);
                                    Console.WriteLine($"  {prop.Name}: {val}");
                                    
                                    // Look for rotation-related background properties
                                    if (prop.Name.ToLower().Contains("rotation") || prop.Name.ToLower().Contains("rotate"))
                                    {
                                        var currentVal = prop.GetValue(sd);
                                        Console.WriteLine($"  Found rotation property: {prop.Name} = {currentVal}");
                                        
                                        // Try setting to -90 (90 degrees counter-clockwise) to fix background offset
                                        try
                                        {
                                            prop.SetValue(sd, -90);
                                            Console.WriteLine($"Set {prop.Name} to -90 (90 degrees counter-clockwise)");
                                        }
                                        catch (Exception rotateEx)
                                        {
                                            Console.WriteLine($"Failed to set rotation: {rotateEx.Message}");
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        else
                        {
                            Console.WriteLine("No background-related properties found in device template");
                        }
                        
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Patch failed: " + ex.Message);
            }

            // Plugin-Discovery: patches\*.dll nach ITurzxPatch-Implementierungen durchsuchen
            LoadAndApplyPlugins(asm, dir);

            try
            {
                var ep = asm.EntryPoint;
                ep.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                if (ex.InnerException != null)
                    Console.WriteLine("Inner: " + ex.InnerException.Message);
            }

            // Add watchdog for display reset handling
          try
            {
                var watchThread = new System.Threading.Thread(() =>
                {
                    int bootCount = 0;
                    bool inBootState = false;
                    DateTime lastBootTime = DateTime.MinValue;
                    bool displayActive = false;
                    DateTime lastDisplayUpdate = DateTime.MinValue;

                    while (true)
                    {
                        try
                        {
                            var verType = asm.GetType("UsbMonitorL.Version");
                            var sdProp = verType.GetProperty("ScreenDevs",
                                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                            var sdList = sdProp.GetValue(null) as System.Collections.IList;

                            foreach (var sd in sdList)
                            {
                                var devCode = sd.GetType().GetProperty("dev_code").GetValue(sd) as string;
                                if (devCode == "VID_1CBE&PID_A088")
                                {
                                    var res = sd.GetType().GetProperty("res").GetValue(sd) as string;
                                    var isPotrit = sd.GetType().GetProperty("IsPotrit").GetValue(sd) as bool?;
                                    
                                    Console.WriteLine($"Watchdog: A088 state - res={res}, IsPotrit={isPotrit}");

                                    // Check if display is showing something (not completely black)
                                    if (res != null && res != "0000000")
                                    {
                                        displayActive = true;
                                        lastDisplayUpdate = DateTime.Now;
                                    }
                                    
                                    // If display has been inactive for too long, force re-initialization
                                    if (!displayActive && (DateTime.Now - lastDisplayUpdate).TotalSeconds > 30)
                                    {
                                        Console.WriteLine("Watchdog: Display appears inactive, attempting re-initialization...");
                                        try
                                        {
                                            // Try to trigger a device re-enumeration
                                            var monitorType = asm.GetType("UsbMonitorL.Monitor");
                                            if (monitorType != null)
                                            {
                                                var scanMethod = monitorType.GetMethod("ScanMonitor",
                                                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                                                if (scanMethod != null)
                                                {
                                                    Console.WriteLine("Watchdog: Calling ScanMonitor for re-init...");
                                                    scanMethod.Invoke(null, null);
                                                    
                                                    // Wait for re-initialization to complete
                                                    System.Threading.Thread.Sleep(5000);
                                                    
                                                    // Check if display is now active
                                                    var newRes = sd.GetType().GetProperty("res").GetValue(sd) as string;
                                                    if (newRes != null && newRes != "0000000")
                                                    {
                                                        Console.WriteLine("Watchdog: Re-initialization successful");
                                                        displayActive = true;
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception reInitEx)
                                        {
                                            Console.WriteLine("Watchdog: Re-initialization failed: " + reInitEx.Message);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Watchdog error: " + ex.Message);
                        }

                        System.Threading.Thread.Sleep(3000);
                    }
                });
                watchThread.IsBackground = true;
                watchThread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Watchdog setup failed: " + ex.Message);
            }
        }
    }
}
