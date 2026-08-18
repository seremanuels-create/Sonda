using System.Management;

namespace Sonda.Core;

/// <summary>
/// Spazio occupato dalle copie shadow (punti di ripristino, "versioni precedenti") sul volume.
/// Sta dentro System Volume Information, che senza privilegi non si legge: WMI (Win32_ShadowStorage)
/// lo dice comunque, ma di solito solo da amministratore.
/// </summary>
public static class ShadowStorage
{
    public static (long? Bytes, string Note) Query(string volRoot)
    {
        string letter = volRoot.TrimEnd('\\'); // "C:"
        try
        {
            string? deviceId = null;
            using (var s = new ManagementObjectSearcher("root\\cimv2", $"SELECT DeviceID FROM Win32_Volume WHERE DriveLetter = '{letter}'"))
            using (var col = s.Get())
            {
                foreach (ManagementObject mo in col) { deviceId = mo["DeviceID"] as string; break; }
            }
            if (deviceId is null) return (null, "Copie shadow: volume non trovato in WMI.");

            long used = 0; bool found = false;
            using (var s = new ManagementObjectSearcher("root\\cimv2", "SELECT Volume, DiffVolume, UsedSpace FROM Win32_ShadowStorage"))
            using (var col = s.Get())
            {
                foreach (ManagementObject mo in col)
                {
                    // DiffVolume = il volume che OSPITA fisicamente le copie (è lì che UsedSpace consuma spazio);
                    // Volume = quello protetto. Di norma coincidono, ma è il primo che conta per il bilancio.
                    string? volRef = (mo["DiffVolume"] ?? mo["Volume"]) as string; // Win32_Volume.DeviceID="\\\\?\\Volume{...}\\"
                    if (volRef is null) continue;
                    string norm = volRef.Replace("\\\\", "\\");
                    if (norm.Contains(deviceId.Replace("\\\\", "\\"), StringComparison.OrdinalIgnoreCase))
                    {
                        used += Convert.ToInt64(mo["UsedSpace"] ?? 0L);
                        found = true;
                    }
                }
            }
            if (!found) return (0, Native.IsAdministrator()
                ? "Nessuna copia shadow ospitata su questo volume."
                : "Nessuna copia shadow su questo volume (o dato non disponibile senza privilegi).");
            return (used, "Da WMI (Win32_ShadowStorage).");
        }
        catch (ManagementException ex)
        {
            return (null, Native.IsAdministrator()
                ? "Copie shadow: WMI non ha risposto (" + ex.Message + ")."
                : "Copie shadow: serve avviare come amministratore per leggere il dato.");
        }
        catch (UnauthorizedAccessException)
        {
            return (null, "Copie shadow: serve avviare come amministratore per leggere il dato.");
        }
    }
}
