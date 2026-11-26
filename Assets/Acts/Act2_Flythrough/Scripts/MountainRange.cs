using UnityEngine;
using UnityEngine.Splines;

namespace Soledad
{
    /// <summary>
    /// A mountain range of the Act 2 desert, drawn as one or more crest lines (the splines of the SplineContainer on
    /// the same object, under World). The ground rises to <see cref="height"/> on the crest line and back to the desert
    /// <see cref="width"/> metres away on each side; the terrain's ridged noise (TerrainShape mountainScale and
    /// mountainRoughness) carves peaks, saddles and side valleys into it. Only the splines' plan view counts (their
    /// heights are ignored). Moving a knot or changing a value rebuilds the terrain.
    /// </summary>
    [RequireComponent(typeof(SplineContainer))]
    public class MountainRange : MonoBehaviour
    {
        [Tooltip("Height of the range on its crest line (m), before the noise carves it.")]
        [Min(0f)] public float height = 1500f;
        [Tooltip("Distance (m) from the crest line where the range has sunk back into the desert, on each side.")]
        [Min(10f)] public float width = 4000f;
        [Tooltip("0 = one continuous range; towards 1 it breaks into separate massifs, then a few lone hills.")]
        [Range(0f, 1f)] public float breakup = 0f;
        [Tooltip("The ends sink back into the desert over this many widths (0 = rounded ends at full height).")]
        [Min(0f)] public float taper = 1.5f;

        SplineContainer _container;

        public SplineContainer Container => _container != null ? _container : (_container = GetComponent<SplineContainer>());
    }
}
