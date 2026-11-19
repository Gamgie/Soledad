using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Soledad
{
    /// <summary>
    /// An act script that lights the act with the Sun of Soledad_Main (Acts 2 to 5). It never keeps the Sun's own
    /// values itself: it registers with <see cref="SunControl"/> while enabled and sets the Sun only when asked.
    /// </summary>
    public interface ISunDriver
    {
        /// <summary>False to leave the Sun alone (the act's overrideSun).</summary>
        bool DrivesSun { get; }
        /// <summary>Set the Sun for this act. Called every frame while this driver has the Sun.</summary>
        void DriveSun(Light sun, HDAdditionalLightData data);
    }

    /// <summary>
    /// The one keeper of the Sun's own values. Several acts may be enabled at once (two act scenes open in the
    /// Editor, a crossfade in Play mode): each one used to keep "the Sun's original values" itself, so one act took
    /// the other's light for the original and left it on the Sun, which then got saved into Soledad_Main.
    /// Here the Sun's own values are taken once, when the first driver takes the Sun, and given back when the last one
    /// lets it go, and around every scene save. One driver at a time sets the Sun: the one whose act has the highest
    /// weight (the act fading in wins halfway through a crossfade), the last enabled on a tie. When the driver
    /// changes, the Sun's own values come back first, so nothing set by the previous act (shadow resolution...)
    /// stays. The values live on a hidden, never saved <see cref="SunOriginal"/> on the Sun, so a script reload keeps
    /// them.
    /// </summary>
    public static class SunControl
    {
        static readonly List<ISunDriver> _drivers = new List<ISunDriver>();
        static readonly List<Act> _acts = new List<Act>();
        static ISunDriver _current;
        static Light _sun;
        static bool _saving;

        /// <summary>Call from the driver's OnEnable.</summary>
        public static void Register(ISunDriver driver)
        {
            int index = _drivers.IndexOf(driver);
            if (index >= 0)
            {
                _drivers.RemoveAt(index);
                _acts.RemoveAt(index);
            }
            _drivers.Add(driver);
            _acts.Add(driver is Component component ? component.GetComponentInParent<Act>(true) : null);
            Drive(driver);
        }

        /// <summary>Call from the driver's OnDisable: the Sun gets its own values back when no driver is left.</summary>
        public static void Unregister(ISunDriver driver)
        {
            int index = _drivers.IndexOf(driver);
            if (index >= 0)
            {
                _drivers.RemoveAt(index);
                _acts.RemoveAt(index);
            }
            if (_current == driver)
            {
                Restore(false);
                _current = null;
            }
            if (_drivers.Count == 0) Restore(true);
            else Drive(null);
        }

        /// <summary>Call from the driver's Update (and after changing its values): sets the Sun if this driver has it.</summary>
        public static void Drive(ISunDriver caller)
        {
            if (_saving) return;
            var active = Active();
            if (active == null)
            {
                Restore(true);
                _current = null;
                return;
            }
            if (caller != null && caller != active) return;
            var sun = FindSun();
            if (sun == null) return;
            var original = Capture(sun);
            if (_current != active)
            {
                // Another act takes the Sun: back to its own values first
                if (_current != null) original.ApplyTo(sun);
                _current = active;
            }
            active.DriveSun(sun, original.Data);
        }

        static ISunDriver Active()
        {
            ISunDriver best = null;
            float bestWeight = float.NegativeInfinity;
            for (int i = 0; i < _drivers.Count; i++)
            {
                var driver = _drivers[i];
                if (driver == null || (driver is Object o && o == null) || !driver.DrivesSun) continue;
                float weight = _acts[i] != null ? _acts[i].weight : 1f;
                if (weight >= bestWeight)
                {
                    best = driver;
                    bestWeight = weight;
                }
            }
            return best;
        }

        static SunOriginal Capture(Light sun)
        {
            var original = sun.GetComponent<SunOriginal>();
            if (original == null)
            {
                original = sun.gameObject.AddComponent<SunOriginal>();
                original.hideFlags = HideFlags.HideAndDontSave;
                original.CaptureFrom(sun);
            }
            return original;
        }

        // Give the Sun its own values back; forget them too when no act drives it any more
        static void Restore(bool release)
        {
            var sun = _sun != null ? _sun : FindSun();
            if (sun == null) return;
            var original = sun.GetComponent<SunOriginal>();
            if (original == null) return;
            original.ApplyTo(sun);
            if (!release) return;
            if (Application.isPlaying) Object.Destroy(original);
            else Object.DestroyImmediate(original);
            _current = null;
        }

        // The directional light named "Sun" (Soledad_Main), else the first directional light
        static Light FindSun()
        {
            if (_sun != null && _sun.isActiveAndEnabled) return _sun;
            Light first = null;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                if (light.name == "Sun") { first = light; break; }
                if (first == null) first = light;
            }
            _sun = first;
            return _sun;
        }

#if UNITY_EDITOR
        // Never save an act's light into Soledad_Main: the Sun's own values while a scene is written
        [UnityEditor.InitializeOnLoadMethod]
        static void HookSaves()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += (scene, path) =>
            {
                Restore(false);
                _current = null;
                _saving = true;
            };
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += scene =>
            {
                _saving = false;
                Drive(null);
            };
            // Entering Play mode, Unity keeps a copy of the open scenes to bring back afterwards, and the hidden
            // SunOriginal is not in it: give the Sun its own values first, or it would come back with the act's light
            // and the act would take that for the original (then saved into Soledad_Main)
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.ExitingEditMode)
                {
                    Restore(true);
                    _saving = true;   // no driving until Play mode starts (the drivers register again there)
                }
                else if (state == UnityEditor.PlayModeStateChange.EnteredPlayMode || state == UnityEditor.PlayModeStateChange.EnteredEditMode)
                {
                    _saving = false;
                }
            };
        }
#endif
    }
}
