// ============================================================================
// ITurzxPatch v1
// ============================================================================
// Vertrag zwischen einem "Host-Loader" (z.B. TurzxPatcher.exe oder
// TurzxSensorLauncher.exe) und externen Patch-Modulen (DLLs), die zur
// Laufzeit zusaetzliche Reflection-Patches auf die geladene TURZX.exe
// Assembly anwenden wollen.
//
// WICHTIG - KOLLISIONSVERMEIDUNG ZWISCHEN DEN REPOS:
// Diese Datei MUSS byte-identisch in BEIDEN Repositories vorhanden sein:
//   - TurzxPatcher\src\Plugins\ITurzxPatch.cs
//   - TurzxSensorBridge\shared\ITurzxPatch.cs   (diese Datei)
// Grund: .NET Type-Identity basiert auf Assembly + Namespace + Typname.
// Wird die Datei in einem der beiden Repos veraendert, OHNE die andere Kopie
// zu aktualisieren, koennen Patch-Module aus TurzxSensorBridge nicht mehr
// als ITurzxPatch von TurzxPatcher erkannt werden (InvalidCastException oder
// stillschweigend "0 Patches gefunden").
//
// Bei einer Aenderung dieses Interfaces:
//   1. Version-Kommentar unten erhoehen (v1 -> v2)
//   2. Aenderung nur additiv (neue optionale Methoden mit Default-Implementierung
//      via Extension-Methods, NICHT bestehende Signaturen aendern) ODER
//      bewusster Breaking Change mit Versionscheck im Loader
//   3. Kopie in BEIDEM Repos synchron aktualisieren
//   4. In beiden READMEs auf die neue Version hinweisen
//
// Namespace ist bewusst NICHT "TurzxPatcher.*" oder "TurzxSensorBridge.*"
// gewaehlt, sondern neutral, damit beide Repos exakt denselben Namespace
// verwenden koennen ohne Bezug aufeinander zu nehmen.
// ============================================================================

using System;
using System.Reflection;

namespace TurzxShared.Plugins
{
    /// <summary>
    /// Kontrakt fuer ein externes Patch-Modul, das vom Host-Loader (TurzxPatcher
    /// oder TurzxSensorLauncher) aus dem "patches\" Unterordner geladen wird.
    /// Version: 1
    /// </summary>
    public interface ITurzxPatch
    {
        /// <summary>
        /// Eindeutiger, menschenlesbarer Name des Patches fuer Konsolen-/Log-Ausgaben.
        /// Beispiel: "Aquacomputer HWiNFO Sensor Bridge Patch"
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Minimal unterstuetzte ITurzxPatch-Interface-Version, gegen die dieses
        /// Modul entwickelt wurde. Der Loader vergleicht dies mit seiner eigenen
        /// HostInterfaceVersion und WARNT (bricht aber nicht ab) bei Abweichung,
        /// damit auch bei kuenftigen Erweiterungen Abwaertskompatibilitaet moeglich ist.
        /// </summary>
        int InterfaceVersion { get; }

        /// <summary>
        /// Wird genau einmal aufgerufen, NACHDEM der Host:
        ///  - TURZX.exe per Assembly.LoadFrom geladen hat
        ///  - den AssemblyResolve-Handler fuer UsbMonitorL registriert hat
        ///  - den AppDomainManager/GetEntryAssembly()-Fix angewendet hat
        ///  - Environment.CurrentDirectory auf das TURZX-Verzeichnis gesetzt hat
        /// und BEVOR der TURZX EntryPoint aufgerufen wird.
        ///
        /// Diese Methode DARF KEINE Exception nach aussen werfen. Alle Fehler
        /// muessen intern gefangen und per Console.WriteLine protokolliert werden,
        /// damit ein fehlerhaftes Patch-Modul nicht den gesamten Host abstuerzen laesst.
        /// </summary>
        /// <param name="turzxAssembly">Die geladene TURZX.exe Assembly.</param>
        /// <param name="turzxDir">Absoluter Pfad zum TURZX-Installationsverzeichnis.</param>
        void Apply(Assembly turzxAssembly, string turzxDir);
    }
}
