using UnityEngine;

namespace Soledad
{
    /// <summary>
    /// A float slider with a curved response: the slider position t (0 to 1) gives
    /// value = min + (max - min) * t^power, so a power above 1 spreads the small values over most of the slider
    /// (power 4: the middle of the slider is 1/16 of the range). The field stores the value itself, so Timeline keys,
    /// Chataigne and code see the real value. Copied onto the act controllers by the generator, like Range.
    /// </summary>
    public class PowerRangeAttribute : PropertyAttribute
    {
        public readonly float min, max, power;

        public PowerRangeAttribute(float min, float max, float power)
        {
            this.min = min;
            this.max = max;
            this.power = power;
        }
    }
}
