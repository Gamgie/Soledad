using System.Collections.Generic;
using UnityEngine;

namespace Soledad
{
    /// <summary>
    /// Act 3: light bulbs rising from the deep. Launches <see cref="RisingBulb"/>s at <see cref="spawnRate"/> per second,
    /// each from one of the spawn points (the children of this object, placed by hand on the cave floor: move, add or
    /// remove them freely) offset by up to <see cref="spawnRadius"/>, with its own life, rise speed and drift drawn
    /// between the min and max values. The bulbs are pooled (created when the act starts, up to <see cref="maxBulbs"/>,
    /// reused, never destroyed during the show). Random draws come from a seeded generator and time from Time.deltaTime,
    /// so a Recorder render repeats. Play mode only.
    /// </summary>
    [DefaultExecutionOrder(-10)]   // before the bulbs, so a look change shows on the same frame
    public class BulbSpawner : MonoBehaviour
    {
        [Tooltip("The bulb to launch (Prefabs/Rising Bulb).")]
        public RisingBulb bulbPrefab;

        [Header("Spawn")]
        [Animate] [Tooltip("Bulbs launched per second.")]
        [Range(0f, 20f)] public float spawnRate = 0.5f;
        [Tooltip("Random offset around the spawn point (m, horizontal).")]
        public float spawnRadius = 1.5f;
        [Tooltip("Most bulbs alive at once, live. When reached, launches wait. Lowered: bulbs in flight end their life.")]
        [Min(0)] public int maxBulbs = 48;
        public int seed = 0;

        [Header("Random per bulb (min, max)")]
        [Animate] [Tooltip("Seconds from launch to the end of the fade out.")]
        public float lifeMin = 25f;
        [Animate] public float lifeMax = 45f;
        [Animate] [Tooltip("Rising speed (m/s).")]
        public float riseSpeedMin = 1.5f;
        [Animate] public float riseSpeedMax = 3.5f;
        [Animate] [Tooltip("Sideways drift (m, on X and Z).")]
        public float driftMin = 0.5f;
        [Animate] public float driftMax = 3f;

        [Header("Look (all bulbs, live)")]
        [ColorUsage(false)] public Color color = new Color(1f, 0.38f, 0.16f);
        [Tooltip("Luminous power of each bulb (lumen) at full life.")]
        public float intensity = 6000f;
        [Tooltip("Distance each light reaches (m).")]
        public float range = 25f;
        [Tooltip("Bulb diameter (m) at full life.")]
        public float size = 0.5f;
        [Tooltip("Bulb brightness for the light's colour.")]
        public float meshEmission = 6f;
        public bool volumetric = true;
        public float volumetricIntensity = 1.5f;
        [Tooltip("Costly: each shadowed point light redraws the scene 6 times.")]
        public bool shadows = false;

        readonly List<RisingBulb> _all = new List<RisingBulb>();
        readonly List<RisingBulb> _free = new List<RisingBulb>();
        readonly List<Transform> _points = new List<Transform>();
        System.Action<RisingBulb> _onFinished;
        Transform _pool;
        Unity.Mathematics.Random _random;
        float _pending;
        int _launched;
        bool _ready;

        void OnEnable()
        {
            _random = new Unity.Mathematics.Random((uint)(seed * 7919 + 1));
            _pending = 0f;
            _launched = 0;
            _onFinished = Return;
            if (!_ready) Prewarm();
            // The spawn points' markers (Prefabs/Spawn Point: an emissive sphere) only show in edit mode
            for (int i = 0; i < transform.childCount; i++)
                if (transform.GetChild(i) != _pool) HideMarkers(transform.GetChild(i));
        }

        static void HideMarkers(Transform point)
        {
            for (int j = 0; j < point.childCount; j++)
            {
                GameObject marker = point.GetChild(j).gameObject;
                if (marker.activeSelf) marker.SetActive(false);
            }
        }

        // The pool: created once, inactive, under a child "Bulbs (generated)" (never saved)
        void Prewarm()
        {
            if (bulbPrefab == null) return;
            _pool = new GameObject("Bulbs (generated)").transform;
            _pool.gameObject.hideFlags = HideFlags.DontSave;
            _pool.SetParent(transform, false);
            for (int i = 0; i < maxBulbs; i++) CreateBulb();
            _ready = true;
        }

        void CreateBulb()
        {
            RisingBulb bulb = Instantiate(bulbPrefab, _pool);
            bulb.name = "Bulb " + (_all.Count + 1);
            bulb.loop = false;
            bulb.finished = _onFinished;
            bulb.gameObject.SetActive(false);
            _all.Add(bulb);
            _free.Add(bulb);
        }

        void Update()
        {
            if (!_ready) return;
            // The look is the spawner's: pushed to every bulb in flight, before they update
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].gameObject.activeSelf) ApplyLook(_all[i]);
            if (spawnRate <= 0f) return;
            _pending += spawnRate * Time.deltaTime;
            while (_pending >= 1f)
            {
                _pending -= 1f;
                if (!Launch()) { _pending = 0f; break; }
            }
        }

        bool Launch()
        {
            // maxBulbs caps the bulbs in flight, live: lowered, launches wait until enough have finished (the ones in
            // flight end their life); raised above the pool, the pool grows (allocates once, only then)
            if (_all.Count - _free.Count >= maxBulbs) return false;
            if (_free.Count == 0) CreateBulb();
            // The spawn points are the other active children (read live: move, add or remove them while playing)
            _points.Clear();
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != _pool && child.gameObject.activeSelf) { _points.Add(child); HideMarkers(child); }
            }
            Vector3 start = transform.position;
            if (_points.Count > 0) start = _points[_random.NextInt(_points.Count)].position;
            Vector2 offset = _random.NextFloat2Direction() * (spawnRadius * Mathf.Sqrt(_random.NextFloat()));
            start += new Vector3(offset.x, 0f, offset.y);

            RisingBulb bulb = _free[_free.Count - 1];
            _free.RemoveAt(_free.Count - 1);
            bulb.life = _random.NextFloat(Mathf.Min(lifeMin, lifeMax), Mathf.Max(lifeMin, lifeMax));
            bulb.riseSpeed = _random.NextFloat(Mathf.Min(riseSpeedMin, riseSpeedMax), Mathf.Max(riseSpeedMin, riseSpeedMax));
            bulb.drift = _random.NextFloat(Mathf.Min(driftMin, driftMax), Mathf.Max(driftMin, driftMax));
            bulb.finished = _onFinished;
            ApplyLook(bulb);
            bulb.gameObject.SetActive(true);
            bulb.Launch(start, seed * 1000 + _launched++);
            return true;
        }

        void ApplyLook(RisingBulb bulb)
        {
            bulb.color = color;
            bulb.intensity = intensity;
            bulb.range = range;
            bulb.size = size;
            bulb.meshEmission = meshEmission;
            bulb.volumetric = volumetric;
            bulb.volumetricIntensity = volumetricIntensity;
            bulb.shadows = shadows;
        }

        void Return(RisingBulb bulb)
        {
            bulb.gameObject.SetActive(false);
            _free.Add(bulb);
        }

        // The spawn points in the Scene view: their spread and the start of the rise
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.9f);
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == _pool || !child.gameObject.activeSelf) continue;
                Vector3 p = child.position;
                Gizmos.DrawSphere(p, 0.4f);
                Gizmos.DrawWireSphere(p, spawnRadius);
                Gizmos.DrawLine(p, p + Vector3.up * 6f);
            }
        }
    }
}
