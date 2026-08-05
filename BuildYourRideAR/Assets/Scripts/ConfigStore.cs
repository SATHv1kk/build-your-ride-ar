using UnityEngine;

// Remembers what the user built between launches. Keyed per car, so switching
// back to a car you configured earlier brings back exactly that build rather
// than resetting to factory paint.
//
// Note: this persists the *configuration*, not the car's world position.
// Restoring a real-world position across a cold start needs a persistent or
// cloud anchor (ARCore Extensions), which this project does not include.
//
// PlayerPrefs.Save() writes to disk synchronously (~10-50ms on Android).
// Calling it on every colour change or gesture settle causes visible stutter
// when the user rapidly flicks through options. So MarkDirty is a fire-and-
// forget signal; the actual disk flush happens on app pause, quit, or after
// 2 seconds of inactivity.
public static class ConfigStore
{
    const string Prefix = "BYR.";
    const float FlushCooldown = 2f;

    static float nextFlush = float.MaxValue;

    static string CarKey(string car, string field)
    {
        return Prefix + (string.IsNullOrEmpty(car) ? "unknown" : car) + "." + field;
    }

    public static string LastCar
    {
        get { return PlayerPrefs.GetString(Prefix + "lastCar", null); }
        set { PlayerPrefs.SetString(Prefix + "lastCar", value ?? string.Empty); MarkDirty(); }
    }

    public static bool HasSavedBuild(string car)
    {
        return PlayerPrefs.HasKey(CarKey(car, "part0")) ||
               PlayerPrefs.HasKey(CarKey(car, "finish")) ||
               PlayerPrefs.HasKey(CarKey(car, "spoiler")) ||
               PlayerPrefs.HasKey(CarKey(car, "wheels"));
    }

    public static int GetPart(string car, int groupIndex, int fallback = 0)
    {
        return PlayerPrefs.GetInt(CarKey(car, "part" + groupIndex), fallback);
    }

    public static void SetPart(string car, int groupIndex, int value)
    {
        PlayerPrefs.SetInt(CarKey(car, "part" + groupIndex), value);
        MarkDirty();
    }

    public static int GetFinish(string car, int fallback = 0)
    {
        return PlayerPrefs.GetInt(CarKey(car, "finish"), fallback);
    }

    public static void SetFinish(string car, int value)
    {
        PlayerPrefs.SetInt(CarKey(car, "finish"), value);
        MarkDirty();
    }

    public static bool GetSpoiler(string car, bool fallback = false)
    {
        return PlayerPrefs.GetInt(CarKey(car, "spoiler"), fallback ? 1 : 0) != 0;
    }

    public static void SetSpoiler(string car, bool value)
    {
        PlayerPrefs.SetInt(CarKey(car, "spoiler"), value ? 1 : 0);
        MarkDirty();
    }

    public static int GetWheels(string car, int fallback = 0)
    {
        return PlayerPrefs.GetInt(CarKey(car, "wheels"), fallback);
    }

    public static void SetWheels(string car, int value)
    {
        PlayerPrefs.SetInt(CarKey(car, "wheels"), value);
        MarkDirty();
    }

    public static float Scale
    {
        get { return PlayerPrefs.GetFloat(Prefix + "scale", 1f); }
        set { PlayerPrefs.SetFloat(Prefix + "scale", value); MarkDirty(); }
    }

    public static float Yaw
    {
        get { return PlayerPrefs.GetFloat(Prefix + "yaw", float.NaN); }
        set { PlayerPrefs.SetFloat(Prefix + "yaw", value); MarkDirty(); }
    }

    // Synchronous. For the once-per-interaction moments where losing the write
    // would be worse than a hitch: placement, car swap, app pause, app quit.
    public static void Save()
    {
        FlushNow();
    }

    // For the hot paths -- every swatch tap, every finish change, every
    // gesture settle. These already called MarkDirty through the individual
    // setters, then immediately defeated it by calling Save(), so the 2-second
    // coalescing window this class exists to provide never once took effect
    // and every colour tap paid a full synchronous PlayerPrefs write
    // (10-50 ms on Android IL2CPP). Flicking through a 13-colour palette meant
    // thirteen disk writes on the main thread.
    public static void SaveDeferred()
    {
        MarkDirty();
    }

    public static void ResetAll()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
    }

    // ---- deferred flush ---------------------------------------------------

    static void MarkDirty()
    {
        nextFlush = Time.time + FlushCooldown;
    }

    // Driven by its own self-attaching pump rather than borrowing the
    // diagnostics logger's Update loop. Hanging the only flush driver off a
    // debug component means the moment that component is stripped from a
    // release build, every deferred write silently stops reaching disk until
    // the app happens to be paused or quit.
    public static void TryFlush()
    {
        if (Time.time >= nextFlush) FlushNow();
    }

    static void FlushNow()
    {
        PlayerPrefs.Save();
        nextFlush = float.MaxValue;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InstallPump()
    {
        if (Object.FindObjectOfType<ConfigStorePump>() != null) return;
        var go = new GameObject("~ConfigStorePump");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ConfigStorePump>();
    }
}
