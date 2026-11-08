using System.Security.Cryptography;
using BepInEx;
using FMOD;
using FMODUnity;
using Gatekeeper.Char_Scripts;
using Gatekeeper.General.Audio;
using Gatekeeper.Mods;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static class SlotAudio
{
    private static readonly string[] names = { "slot-lever", "slot-cycle", "slot-number-land", "slot-win" };
    private static readonly Sound[] sounds = new Sound[4];
    private static readonly List<Channel> voices = new();
    private static FMOD.System core;
    private static FMOD.Studio.Bus bus;
    private static ChannelGroup group;
    private static Channel cycle;
    private static bool ready, busLocked, cycleFading, cycleWanted, queuedWin, failed;
    private static float cycleGain;
    private static readonly SlotSoundTimeline timeline = new();

    private static void Check(RESULT result, string action)
    { if (result != RESULT.OK) throw new InvalidOperationException($"{action}: {result}"); }

    private static bool Prepare()
    {
        if (ready) return true;
        try
        {
            if (failed || !WorldSlots.Exists || !RuntimeManager.IsInitialized || FmodManager.Instance == null) return false;
            core = RuntimeManager.CoreSystem;
            bus = FmodManager.Instance._sfxBus;
            if (!bus.isValid()) return false;
            Check(bus.lockChannelGroup(), "Lock SFX bus"); busLocked = true;
            Check(RuntimeManager.StudioSystem.flushCommands(), "Prepare SFX bus");
            Check(bus.getChannelGroup(out group), "Get SFX channel group");
            var cache = Path.Combine(Paths.CachePath, Plugin.Id, "slot-audio");
            Directory.CreateDirectory(cache);
            for (var i = 0; i < names.Length; i++)
            {
                using var source = typeof(SlotAudio).Assembly.GetManifestResourceStream("Gatekeeper.CustomMods.Assets.Audio." + names[i] + ".wav")
                    ?? throw new InvalidOperationException("Missing slot sound " + names[i]);
                using var buffer = new MemoryStream(); source.CopyTo(buffer);
                var bytes = buffer.ToArray(); var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                var file = Path.Combine(cache, names[i] + "-" + hash + ".wav");
                if (!File.Exists(file) || !SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(SHA256.HashData(bytes))) File.WriteAllBytes(file, bytes);
                Check(core.createSound(file, MODE._2D | MODE.CREATESAMPLE | (i == 1 ? MODE.LOOP_NORMAL : MODE.LOOP_OFF), out sounds[i]), "Load " + names[i]);
            }
            ready = true;
            Plugin.Logger.LogInfo("Four prepared slot sounds loaded through the game's FMOD SFX bus.");
            return true;
        }
        catch (Exception ex) { Disable(ex); return false; }
    }

    private static float Volume()
    {
        var local = PlayableCharactersHolder.MyCharacter;
        if (local == null || !Plugin.SlotsInRestArea || !WorldSlots.Exists) return 0;
        // Attenuate from the player's position rather than the elevated camera.
        var distance = Mathf.Sqrt(WorldSlots.DistanceSquared(local));
        return Mathf.Clamp01((14 - distance) / 10);
    }
    private static Channel Play(int index, bool looping = false)
    {
        Check(core.playSound(sounds[index], group, true, out var channel), "Play " + names[index]);
        voices.Add(channel); // Also track a partially configured voice for cleanup.
        Check(channel.setVolume(Volume() * (looping ? .6f * cycleGain : 1)), "Set slot volume");
        Check(channel.setPaused(false), "Start slot sound");
        return channel;
    }
    private static void Cues(SlotSoundCue[] cues)
    {
        if (!Prepare()) return;
        try
        {
            foreach (var cue in cues)
                switch (cue)
                {
                    case SlotSoundCue.Lever: if (Time.timeScale > 0) Play(0); break;
                    case SlotSoundCue.CycleStart:
                        cycleWanted = true; cycleFading = false;
                        if (Time.timeScale > 0) StartCycle(); break;
                    case SlotSoundCue.NumberLand: if (Time.timeScale > 0) Play(2); break;
                    case SlotSoundCue.CycleStop: cycleWanted = false; cycleFading = true; break;
                }
        }
        catch (Exception ex) { Disable(ex); }
    }
    private static void StartCycle()
    {
        if (cycle.hasHandle()) cycle.stop();
        cycleGain = (3 - timeline.Landed) / 3f;
        cycle = Play(1, true);
    }
    internal static void Begin(string id, float elapsed) => Cues(timeline.Begin(id, elapsed));
    internal static void Win(string id)
    {
        if (!timeline.ConfirmWin(id) || !Prepare()) return;
        try { if (Time.timeScale > 0) Play(3); else queuedWin = true; } catch (Exception ex) { Disable(ex); }
    }
    internal static void Tick()
    {
        if (!Plugin.SlotsInRestArea || !WorldSlots.Exists) { Clear(); return; }
        if (!Prepare()) return;
        Cues(timeline.Advance(SlotPlayer.AnimationAge));
        if (!ready) return;
        try
        {
            if (Time.timeScale > 0)
            {
                if (queuedWin) { queuedWin = false; Play(3); }
                if (cycleWanted && !cycle.hasHandle()) StartCycle();
            }
            var volume = Volume();
            for (var i = voices.Count - 1; i >= 0; i--)
            {
                var channel = voices[i];
                if (channel.isPlaying(out var playing) != RESULT.OK || !playing) { voices.RemoveAt(i); continue; }
                if (channel.handle != cycle.handle) channel.setVolume(volume);
            }
            if (cycle.hasHandle())
            {
                cycleGain = Mathf.MoveTowards(cycleGain, cycleFading ? 0 : (3 - timeline.Landed) / 3f, Time.unscaledDeltaTime * 12);
                cycle.setVolume(volume * cycleGain * .6f);
                if (cycleFading && cycleGain <= 0) { cycle.stop(); cycle.clearHandle(); }
            }
        }
        catch (Exception ex) { Disable(ex); }
    }
    private static void Disable(Exception ex)
    {
        Plugin.Logger.LogWarning("Slot audio disabled for this visit; betting remains available: " + ex);
        Clear(); failed = true;
    }
    internal static void Clear()
    {
        try
        {
            foreach (var channel in voices) if (channel.hasHandle()) channel.stop();
            for (var i = 0; i < sounds.Length; i++) if (sounds[i].hasHandle()) sounds[i].release();
            if (busLocked && bus.isValid()) bus.unlockChannelGroup();
        }
        catch (Exception ex) { Plugin.Current?.Log.LogWarning("Slot audio cleanup: " + ex.Message); }
        finally
        {
            cycle = default; Array.Clear(sounds, 0, sounds.Length); voices.Clear();
            busLocked = false; ready = false; cycleFading = false; cycleWanted = false; queuedWin = false; cycleGain = 0;
            timeline.Reset();
            if (!Plugin.RestAreaScene) failed = false;
        }
    }
}
