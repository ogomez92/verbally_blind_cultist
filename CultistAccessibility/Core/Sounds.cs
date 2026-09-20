using System;
using UnityEngine;

namespace CultistAccessibility.Core
{
    /// <summary>
    /// Short tones made in code (no audio files). The actionable cue marks a focused item that Enter can act on
    /// in lists where only some items can (cards that a verb would take, slots with a card that fits, Start).
    /// Played by the mod's own AudioSource outside the game's mixer, so the game's volume settings and pause
    /// do not silence it; its volume is the SoundVolume setting.
    /// </summary>
    internal static class Sounds
    {
        private static AudioSource _source;
        private static AudioClip _actionable;

        public static void Initialize(GameObject host)
        {
            try
            {
                _source = host.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.spatialBlend = 0f;
                _source.ignoreListenerPause = true;
                _source.bypassEffects = true;
                _source.bypassListenerEffects = true;
                _actionable = Tone("CultistAccessibility.actionable", 880f, 0.06f);
            }
            catch (Exception ex)
            {
                Plugin.LogWarning("Sounds unavailable: " + ex.Message);
            }
        }

        public static void Actionable()
        {
            if (_source == null || _actionable == null || !ModConfig.ActionableSound.Value) return;
            try { _source.PlayOneShot(_actionable, Mathf.Clamp01(ModConfig.SoundVolume.Value / 100f)); }
            catch (Exception ex) { Plugin.LogDebug("Actionable sound failed: " + ex.Message); }
        }

        /// <summary>A sine tone with 5 ms fades at both ends (no click).</summary>
        private static AudioClip Tone(string name, float frequency, float seconds)
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            int count = Mathf.Max(1, (int)(rate * seconds));
            int fade = Mathf.Max(1, rate / 200);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float envelope = Mathf.Min(1f, Mathf.Min(i, count - 1 - i) / (float)fade);
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / rate) * envelope;
            }
            var clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
