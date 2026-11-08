using System.Security.Cryptography;
using System.Text.Json;
using BepInEx;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using Gatekeeper.General.Audio;
using Gatekeeper.General.Audio.LevelMusic;
using Gatekeeper.Mods;
using HarmonyLib;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static class ExpansionMusic
{
    private sealed record Track(string file, string sha256);
    private static readonly ExpansionPlaylist playlist = new(new System.Random());
    private static Task<string[]>? files;
    private static BaseGameMusicController? owner;
    private static EventInstance native;
    private static Bus bus;
    private static ChannelGroup group;
    private static Sound stream;
    private static Channel voice;
    private static bool locked, opening, custom, failed, nativePaused;
    private static float nativeGain = 1, blend, stockRemaining, nextPoll;
    private static int current = -1;
    private const float FadeSeconds = 1.5f;

    internal static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(BaseGameMusicController), "PlayMainLevelTrack"),
            postfix: new HarmonyMethod(typeof(ExpansionMusic), nameof(TrackStarted)));
        harmony.Patch(AccessTools.Method(typeof(BaseGameMusicController), "StopTracks"),
            prefix: new HarmonyMethod(typeof(ExpansionMusic), nameof(TracksStopping)));
        harmony.Patch(AccessTools.Method(typeof(BaseGameMusicController), "SetIntenseInternal"),
            postfix: new HarmonyMethod(typeof(ExpansionMusic), nameof(IntensityChanged)));
        files = Task.Run(Extract); // File hashing/extraction never runs in the frame loop.
    }

    private static string[] Extract()
    {
        var assembly = typeof(ExpansionMusic).Assembly;
        const string prefix = "Gatekeeper.CustomMods.Assets.Music.";
        using var manifest = assembly.GetManifestResourceStream(prefix + "tracks.json")
            ?? throw new InvalidDataException("Missing expansion music list.");
        var tracks = JsonSerializer.Deserialize<Track[]>(manifest)
            ?? throw new InvalidDataException("Invalid expansion music list.");
        if (tracks.Length != 7) throw new InvalidDataException("Expansion requires seven songs.");
        var folder = Path.Combine(Paths.CachePath, Plugin.Id, "music");
        Directory.CreateDirectory(folder);
        return tracks.Select(track =>
        {
            if (Path.GetFileName(track.file) != track.file || track.sha256.Length != 64)
                throw new InvalidDataException("Invalid expansion music cache entry.");
            var path = Path.Combine(folder, Path.GetFileNameWithoutExtension(track.file) + "-" + track.sha256 + ".ogg");
            if (File.Exists(path))
            {
                using var existing = File.OpenRead(path);
                using var hash = SHA256.Create();
                if (Convert.ToHexString(hash.ComputeHash(existing)).Equals(track.sha256, StringComparison.OrdinalIgnoreCase)) return path;
            }
            using var source = assembly.GetManifestResourceStream(prefix + track.file)
                ?? throw new InvalidDataException("Missing song " + track.file);
            var temporary = path + ".tmp";
            using (var output = File.Create(temporary)) source.CopyTo(output);
            using (var written = File.OpenRead(temporary))
            using (var hash = SHA256.Create())
                if (!Convert.ToHexString(hash.ComputeHash(written)).Equals(track.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Music integrity check failed: " + track.file);
            File.Move(temporary, path, true);
            return path;
        }).ToArray();
    }

    private static void Check(RESULT result, string operation)
    { if (result != RESULT.OK) throw new InvalidOperationException(operation + ": " + result); }

    private static void TrackStarted(BaseGameMusicController __instance, bool __0)
    {
        // Leave special-scene/boss/arena controllers, ambience and their stingers native.
        if (!__0 || __instance.TryCast<LevelMusicController>() == null) return;
        try
        {
            Clear();
            native = __instance.SoundTrack;
            if (!native.isValid()) return;
            owner = __instance;
            Check(native.getVolume(out nativeGain, out _), "Read native music gain");
            Check(native.getPaused(out nativePaused), "Read native music pause");
            stockRemaining = StockDuration();
            current = playlist.Next();
            nextPoll = 0;
            Plugin.Logger.LogInfo(current < 0 ? "Music rotation: original gameplay soundtrack." : $"Music rotation: queued Forge DLC Song {current + 1}.");
        }
        catch (Exception ex) { Disable(ex); }
    }

    private static float StockDuration()
    {
        // FMOD's adaptive events can report zero length. Give those a full 3-minute turn.
        if (native.getDescription(out var description) == RESULT.OK &&
            description.getLength(out var milliseconds) == RESULT.OK && milliseconds >= 30000 && milliseconds <= 1200000)
            return milliseconds / 1000f;
        return 180;
    }

    private static void TracksStopping(BaseGameMusicController __instance)
    { if (owner?.Pointer == __instance.Pointer) Clear(); }

    private static void IntensityChanged(BaseGameMusicController __instance, LevelSoundIntense __0)
    {
        if (owner?.Pointer != __instance.Pointer || __0 != LevelSoundIntense.Boss) return;
        // A boss taking over the same controller must also regain its original music.
        Clear();
    }

    private static bool PrepareBus()
    {
        if (locked) return true;
        if (!RuntimeManager.IsInitialized || FmodManager._instance == null) return false;
        bus = FmodManager._instance._musicBus;
        if (!bus.isValid()) return false;
        Check(bus.lockChannelGroup(), "Lock music bus"); locked = true;
        Check(RuntimeManager.StudioSystem.flushCommands(), "Prepare music bus");
        Check(bus.getChannelGroup(out group), "Get music channel group");
        return true;
    }

    private static void OpenSong()
    {
        if (files == null || !files.IsCompleted) return;
        if (!PrepareBus()) return;
        var path = files.GetAwaiter().GetResult()[current];
        Check(RuntimeManager.CoreSystem.createSound(path, MODE._2D | MODE.CREATESTREAM | MODE.NONBLOCKING | MODE.LOOP_OFF, out stream), "Open expansion music stream");
        opening = true;
    }

    private static void StartSong()
    {
        Check(RuntimeManager.CoreSystem.playSound(stream, group, true, out voice), "Play expansion music");
        Check(voice.setVolume(0), "Fade expansion music in");
        Check(voice.setPaused(false), "Start expansion music");
        opening = false; custom = true;
        Plugin.Logger.LogInfo($"Music rotation: playing Forge DLC Song {current + 1} through the native music bus.");
    }

    private static void ReturnToStock()
    {
        ReleaseStream();
        custom = false; current = playlist.Next();
        native.setPaused(nativePaused);
        stockRemaining = StockDuration();
        Plugin.Logger.LogInfo("Music rotation: original gameplay soundtrack.");
    }

    internal static void Tick()
    {
        if (owner is null) return;
        try
        {
            if (owner == null || !owner.isActiveAndEnabled || !native.isValid()) { Clear(); return; }
            // Music shares the native bus, so master/music volume, mute and snapshots apply.
            var target = custom ? 1f : 0f;
            if (blend != target)
            {
                if (!custom) native.setPaused(nativePaused);
                blend = Mathf.MoveTowards(blend, target, Time.unscaledDeltaTime / FadeSeconds);
                Check(native.setVolume(nativeGain * (1 - blend)), "Crossfade original music");
                if (voice.hasHandle()) Check(voice.setVolume(blend), "Crossfade expansion music");
                if (custom && blend == 1) Check(native.setPaused(true), "Suspend original music during added song");
            }
            if (Time.unscaledTime < nextPoll) return;
            var elapsed = nextPoll == 0 ? 0 : Time.unscaledTime - (nextPoll - .2f);
            nextPoll = Time.unscaledTime + .2f;
            if (failed) return;
            if (native.getPlaybackState(out var state) != RESULT.OK || state is PLAYBACK_STATE.STOPPED or PLAYBACK_STATE.STOPPING)
            { Clear(); return; }
            if (custom)
            {
                if (voice.isPlaying(out var playing) != RESULT.OK || !playing) ReturnToStock();
            }
            else if (opening)
            {
                Check(stream.getOpenState(out var state2, out _, out _, out _), "Read music stream state");
                if (state2 == OPENSTATE.ERROR) throw new InvalidDataException("FMOD could not decode the expansion song.");
                if (state2 == OPENSTATE.READY) StartSong();
            }
            else if (current >= 0) OpenSong();
            else if (blend == 0)
            {
                if (native.getPaused(out var paused) == RESULT.OK && !paused) stockRemaining -= elapsed;
                if (stockRemaining <= 0) { current = playlist.Next(); if (current < 0) stockRemaining = StockDuration(); }
            }
        }
        catch (Exception ex) { Disable(ex); }
    }

    private static void ReleaseStream()
    {
        if (voice.hasHandle()) voice.stop();
        voice = default;
        if (stream.hasHandle()) stream.release();
        stream = default; opening = false;
    }

    private static void Disable(Exception ex)
    {
        Plugin.Current?.Log.LogWarning("Added music unavailable; retaining original soundtrack: " + ex.Message);
        Clear(); failed = true;
    }

    internal static void Clear()
    {
        try
        {
            ReleaseStream();
            if (native.isValid()) { native.setVolume(nativeGain); native.setPaused(nativePaused); }
            if (locked && bus.isValid()) bus.unlockChannelGroup();
        }
        catch (Exception ex) { Plugin.Current?.Log.LogWarning("Music cleanup: " + ex.Message); }
        finally
        {
            owner = null; native = default; bus = default; group = default;
            locked = custom = opening = failed = false; nativeGain = 1; nativePaused = false;
            blend = stockRemaining = nextPoll = 0; current = -1;
        }
    }
}
