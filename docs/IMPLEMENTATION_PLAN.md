# TurzxPatcher — Plugin-System Implementierungsplan (für lokale LLM)

**Zielgruppe dieses Dokuments:** Eine lokale LLM (z. B. via Ollama/LM Studio o. ä.), die diesen Plan Schritt für Schritt autonom abarbeitet. Jeder Schritt ist so konkret formuliert, dass er ohne Rückfrage an einen Menschen umsetzbar ist. Wo Unsicherheit besteht, ist das explizit als "ENTSCHEIDUNGSPUNKT" markiert mit klaren Kriterien zur Selbstentscheidung.

**Wichtig:** Dies ist NICHT der Plan für neue Sensor-Features. Sensor-Logik gehört ausschließlich in das Schwester-Repo `TurzxSensorBridge` (siehe `../../TurzxSensorBridge/docs/IMPLEMENTATION_PLAN.md`). Dieses Dokument behandelt AUSSCHLIESSLICH die additive Erweiterung von `TurzxPatcher` um einen generischen Plugin-Lademechanismus, damit externe Patch-Module (wie das aus TurzxSensorBridge) im selben TURZX-Host-Prozess geladen werden können.

## Kontext / Bestehender Code

- Repo-Root: `C:\Users\Nutzer\SynologyDrive\Code\TurzxPatcher\`
- Hauptdatei: `src\Program.cs` (aktuell ca. 484 Zeilen, siehe unten für relevante Ausschnitte)
- Projektdatei: `src\TurzxPatcher.csproj` (Target: `net48`, `UseWPF=true`, `PlatformTarget=x64`)
- Bereits vorhanden: `src\Plugins\ITurzxPatch.cs` (Interface-Kontrakt, siehe diese Datei — NICHT verändern, außer in Abstimmung mit `TurzxSensorBridge\shared\ITurzxPatch.cs`, beide Dateien müssen byte-identisch bleiben)

### Relevanter Ist-Zustand von `Program.cs` (Stand vor diesem Plan)

Die Methode `RunWorker(string dir)` in `Program.cs`:
1. Lädt `TURZX.exe` per `Assembly.LoadFrom(exePath)` in Variable `asm`
2. Registriert `AppDomain.CurrentDomain.AssemblyResolve` Handler
3. Setzt `AppDomainManager.m_entryAssembly` per Reflection (EntryAssembly-Fix)
4. Patcht das A088-Device-Template (`res`, `width`/`height`, `IsPotrit`, Background-Rotation) über `UsbMonitorL.Version.ScreenDevs`
5. Ruft `asm.EntryPoint.Invoke(null, null)` auf → TURZX startet
6. Startet einen Watchdog-Thread

**Der neue Plugin-Discovery-Code muss zwischen Schritt 4 und Schritt 5 eingefügt werden** — also NACHDEM der A088-Patch angewendet wurde, aber BEVOR TURZX' EntryPoint aufgerufen wird. Das entspricht in der aktuellen Datei der Stelle unmittelbar vor:

```csharp
try
{
    var ep = asm.EntryPoint;
    ep.Invoke(null, null);
}
```

(diese Zeilen befinden sich aktuell um Zeile 380-384, aber verlasse dich nicht auf die exakte Zeilennummer — suche stattdessen nach dem String `asm.EntryPoint` im File.)

## Aufgabe 1: Named Mutex für Kollisionsschutz

**Warum:** Es kann künftig mehrere Host-Loader geben, die TURZX.exe hosten wollen (`TurzxPatcher.exe` selbst, sowie künftig `TurzxSensorLauncher.exe` aus dem Schwester-Repo für Nutzer ohne A088-Bedarf). Es darf niemals mehr als ein Host-Prozess gleichzeitig TURZX.exe laden — das würde zu doppelten Watchdogs, doppelten EntryPoint-Aufrufen und USB-Race-Conditions führen.

**Umsetzung:**

1. Öffne `src\Program.cs`.
2. Füge am Anfang der Methode `RunWorker(string dir)` (also direkt nach der öffnenden `{` der Methode, vor der ersten bestehenden Zeile) folgenden Code ein:

```csharp
const string MutexName = "Global\\TurzxHostActive";
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
// Ein expliziter Release wuerde riskieren, dass ein anderer Prozess
// den Mutex uebernimmt, WAEHREND TURZX noch aktiv im selben Prozess laeuft.
```

3. **Abnahmekriterium:** Starte den Worker zweimal parallel (z. B. zwei PowerShell-Fenster, beide rufen `TurzxPatcher.exe --worker "C:\Pfad\Zu\TURZX"` auf). Der zweite Aufruf muss die Fehlermeldung ausgeben und sich beenden, ohne TURZX zu laden. Der erste Aufruf muss normal funktionieren wie bisher.
4. **Wichtiger Hinweis für spätere Wartung:** Falls `TurzxPatcher.exe` abstürzt (nicht regulär beendet wird), wird der Mutex vom Betriebssystem automatisch freigegeben, sobald der Prozess endet — kein manuelles Cleanup nötig.

## Aufgabe 2: Plugin-Discovery-Mechanismus

**Ziel:** Beim Start soll `RunWorker` einen Unterordner `patches\` relativ zum TURZX-Verzeichnis (`dir`-Parameter) scannen, dort `*.dll`-Dateien laden, per Reflection nach Typen suchen, die `TurzxShared.Plugins.ITurzxPatch` implementieren, Instanzen erzeugen und deren `Apply(asm, dir)`-Methode aufrufen.

**Umsetzung:**

1. Stelle sicher, dass `src\Plugins\ITurzxPatch.cs` im Projekt enthalten ist (bei SDK-style `.csproj` mit Standard-Globbing automatisch der Fall, da `**/*.cs` standardmäßig inkludiert wird — prüfe das mit `Get-Content src\TurzxPatcher.csproj` und stelle sicher, es gibt KEIN explizites `<Compile Remove="Plugins\**" />` oder ähnliches, das die Datei ausschließen würde).

2. Füge in `Program.cs` folgende neue private static Methode hinzu (z. B. direkt unterhalb von `RunWorker`, oder an beliebiger Stelle innerhalb der `Program`-Klasse):

```csharp
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
                    // Bewusst kein "throw" - ein fehlerhaftes Plugin darf den Host
                    // und andere Plugins nicht zum Absturz bringen.
                }
            }
        }
        catch (Exception loadEx)
        {
            Console.WriteLine("FEHLER beim Laden von " + Path.GetFileName(dllPath) + ": " + loadEx.Message);
            // Weiter mit der naechsten DLL-Datei.
        }
    }
}
```

3. Füge oben in `Program.cs` bei den `using`-Direktiven hinzu, falls nicht vorhanden:
```csharp
using System.Linq;
```
(ist laut Bestandscode bereits vorhanden — prüfen, nicht doppelt einfügen)

4. Rufe `LoadAndApplyPlugins(asm, dir);` in `RunWorker` an genau der Stelle auf, die oben in "Kontext" beschrieben ist: NACH dem A088-Patch-Block (nach der schließenden `}` des `try`-Blocks, der `catch (Exception ex) { Console.WriteLine("Patch failed: " + ex.Message); }` enthält), aber VOR dem Block, der `asm.EntryPoint.Invoke(null, null)` aufruft.

Suche im File nach dieser Zeile als Ankerpunkt:
```csharp
Console.WriteLine("Patch failed: " + ex.Message);
```
Direkt nach dem Ende des umschließenden try/catch-Blocks (also nach der nächsten `}`), aber vor dem Kommentar `// Add watchdog` bzw. vor dem `try { var ep = asm.EntryPoint;` Block, folgenden Aufruf einfügen:

```csharp
LoadAndApplyPlugins(asm, dir);
```

5. **Abnahmekriterium:**
   - Ohne `patches\`-Ordner: Konsolen-Ausgabe "Kein 'patches' Ordner gefunden..." erscheint, TURZX startet normal wie bisher (Regressionstest — bestehende A088-Funktionalität darf NICHT beeinträchtigt werden).
   - Mit `patches\`-Ordner, aber leer: entsprechende Meldung, TURZX startet normal.
   - Mit einer Test-DLL, die eine simple `ITurzxPatch`-Implementierung enthält (siehe Testszenario unten): Plugin wird geladen, `Apply()` wird aufgerufen, Konsolen-Ausgabe zeigt "Plugin ... erfolgreich angewendet".
   - Mit einer absichtlich fehlerhaften Test-DLL (z. B. `Apply()` wirft eine Exception): Fehler wird abgefangen und geloggt, TURZX startet trotzdem normal weiter (kein Absturz des Hosts).

### Testszenario für Plugin-Discovery (zum Selbst-Verifizieren)

Erstelle temporär ein minimales Test-Plugin-Projekt (kann nach erfolgreichem Test wieder gelöscht werden, oder dauerhaft unter `test\TestPlugin\` im Repo verbleiben als Beispiel für Community-Entwickler):

```csharp
// test\TestPlugin\TestPatch.cs
using System;
using System.Reflection;
using TurzxShared.Plugins;

namespace TestPlugin
{
    public class TestPatch : ITurzxPatch
    {
        public string Name => "Test-Plugin (Diagnose)";
        public int InterfaceVersion => 1;

        public void Apply(Assembly turzxAssembly, string turzxDir)
        {
            Console.WriteLine("TestPatch.Apply() wurde aufgerufen. turzxDir=" + turzxDir);
        }
    }
}
```

Kompiliere dies als eigene `net48` Class Library (`TargetFramework=net48`, referenziert `TurzxPatcher\src\Plugins\ITurzxPatch.cs` entweder als Link oder über eine kleine Shared-Projektreferenz), kopiere die resultierende `.dll` nach `<TURZX-Verzeichnis>\patches\TestPlugin.dll` und starte `TurzxPatcher.exe`. Prüfe die Konsolenausgabe auf die erwarteten Meldungen.

## Aufgabe 3: README aktualisieren

1. Öffne `README.md` im Repo-Root.
2. Füge einen neuen Abschnitt `## Plugin-System (für Erweiterungen wie zusätzliche Sensordaten)` ein, z. B. nach dem Abschnitt "Watchdog-Feature" und vor "Known Issues". Inhalt:

```markdown
## Plugin-System (für Erweiterungen wie zusätzliche Sensordaten)

Ab Version 2.1.0 unterstützt TurzxPatcher ein einfaches Plugin-System, über das
externe Tools zusätzliche Laufzeit-Patches auf TURZX anwenden können, ohne
diesen Patcher selbst verändern zu müssen.

### Funktionsweise

1. Lege eine `.dll`-Datei, die das `TurzxShared.Plugins.ITurzxPatch`-Interface
   implementiert, in einen Unterordner `patches\` neben `TURZX.exe`.
2. Starte `TurzxPatcher.exe` wie gewohnt.
3. Nach dem A088-Display-Patch, aber vor dem Start von TURZX, werden alle
   gefundenen Plugins automatisch geladen und angewendet.

### Bekannte Plugins

- **TurzxSensorBridge** (separates Projekt): Ermöglicht die Nutzung beliebiger
  HWiNFO-Sensoren (inkl. Aquacomputer Wassertemperatur, Durchflussrate,
  Wasserqualität) als Datenquelle im TURZX Theme-Editor.
  Siehe: https://github.com/<dein-github-user>/TurzxSensorBridge

### Eigene Plugins entwickeln

Das Interface `ITurzxPatch` (Version 1) ist in `src\Plugins\ITurzxPatch.cs`
dokumentiert. Ein Plugin ist eine .NET Framework 4.8 Class Library, die
mindestens eine öffentliche, nicht-abstrakte Klasse enthält, die dieses
Interface implementiert.

### Kollisionsschutz

TurzxPatcher belegt beim Start einen systemweiten Mutex (`Global\TurzxHostActive`),
um zu verhindern, dass mehrere Host-Prozesse (z.B. TurzxPatcher UND ein anderer
TURZX-Loader) gleichzeitig TURZX.exe laden. Ein zweiter Startversuch wird mit
einer klaren Fehlermeldung abgelehnt.
```

3. Aktualisiere den Abschnitt "Version History" (aktuell zeigt `v2.0.0` als "Current"): Füge einen neuen Eintrag `### v2.1.0 (Current)` hinzu mit den Punkten "Added plugin system (ITurzxPatch interface + patches\ folder discovery)", "Added Global\TurzxHostActive mutex for multi-loader collision protection". Ändere den bisherigen `v2.0.0`-Eintrag von "(Current)" zu einer normalen Versionszeile ohne "(Current)"-Suffix.

## Aufgabe 4: Git-Repository initialisieren und auf GitHub veröffentlichen

**Voraussetzung:** `gh` CLI muss installiert sein. Falls `gh --version` fehlschlägt:
```powershell
winget install --id GitHub.cli -e
```
Danach `gh auth login` interaktiv ausführen (erfordert menschliche Interaktion für den Browser-Login-Flow — falls das nicht möglich ist, diesen Schritt der lokalen LLM überspringen und dem Menschen melden, dass `gh auth login` manuell nachgeholt werden muss).

**Schritte** (auszuführen im Verzeichnis `C:\Users\Nutzer\SynologyDrive\Code\TurzxPatcher`):

```powershell
git init
git add .
git commit -m "Initial commit: A088 display fix, orientation patch, plugin system"
```

Prüfe vorher den Inhalt der bestehenden `.gitignore` (bereits vorhanden laut Repo-Struktur) — sie sollte mindestens `bin/`, `obj/` enthalten, damit keine Build-Artefakte committed werden. Falls nicht vorhanden, ergänzen:
```
bin/
obj/
*.user
```

Dann:
```powershell
gh repo create TurzxPatcher --public --license MIT --source . --remote origin --push
```

Falls `--license MIT` bei `gh repo create` in Kombination mit `--source .` nicht wie erwartet funktioniert (kann je nach `gh`-Version variieren), stattdessen manuell eine `LICENSE`-Datei mit dem Standard-MIT-Lizenztext (Copyright-Jahr 2026, Rechteinhaber-Name vom Repo-Owner erfragen falls nicht bekannt — als Platzhalter `[COPYRIGHT HOLDER]` verwenden und den Menschen bitten, das zu vervollständigen) im Repo-Root anlegen, committen und regulär pushen:
```powershell
git add LICENSE
git commit -m "Add MIT license"
git push
```

**Abnahmekriterium:** `gh repo view TurzxPatcher` zeigt das Repo als öffentlich sichtbar mit korrekter Lizenz an.

## Reihenfolge der Aufgaben

Bearbeite die Aufgaben in dieser Reihenfolge: 1 (Mutex) → 2 (Plugin-Discovery) → Build & lokale Tests (siehe Abnahmekriterien je Aufgabe) → 3 (README) → 4 (Git/GitHub).

## Build-Befehl zur Verifikation nach jeder Aufgabe

```powershell
dotnet build "C:\Users\Nutzer\SynologyDrive\Code\TurzxPatcher\src\TurzxPatcher.csproj" -c Release
```

Muss ohne Fehler durchlaufen. Warnungen sind akzeptabel, sofern sie bereits vor diesem Plan bestanden (Regressionsprüfung: vergleiche Warnungsanzahl vor/nach den Änderungen).
