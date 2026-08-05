using UnityEngine;

// Drives ConfigStore's deferred flush. Deliberately tiny and separate:
// ConfigStore is a static class, so it cannot receive Update,
// OnApplicationPause or OnApplicationQuit itself, and Unity needs a
// MonoBehaviour in a file of its own name.
//
// This used to be borrowed from ARDiagnosticLog.Update(), which called
// ConfigStore.TryFlush() every frame. That works, but it makes the only path
// by which settings reach disk depend on a debug component staying in the
// build -- strip the logger and every deferred write silently stops landing
// until the app is next paused or quit. ConfigStore installs this instead, so
// persistence owns its own lifetime.
//
// Self-attaching via ConfigStore.InstallPump, so nothing needs scene wiring.
public class ConfigStorePump : MonoBehaviour
{
    void Update()
    {
        ConfigStore.TryFlush();
    }

    // Android can kill a backgrounded app without ever calling
    // OnApplicationQuit, so pause is the write that actually matters on device.
    void OnApplicationPause(bool paused)
    {
        if (paused) ConfigStore.Save();
    }

    void OnApplicationQuit()
    {
        ConfigStore.Save();
    }
}
