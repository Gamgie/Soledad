using System;

namespace Soledad
{
    /// <summary>
    /// Marks a public field or a parameterless public method of an act script as a show parameter. The act
    /// controller generator (Soledad > Generate Act Controllers) copies it onto the act's controller, which lives on
    /// Show in Soledad_Main and is never unloaded, so its Chataigne address and its Timeline tracks stay the same
    /// whichever act is loaded.
    ///
    /// The controller field is named <c>Type_field</c> by default. Give a fixed name to keep the address when the
    /// field is renamed, or when an act holds two components of the same type: <c>[Animate("tunnelNoise")]</c>.
    ///
    /// Supported field types: float, int, bool, enums, Color, Vector3.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class AnimateAttribute : Attribute
    {
        /// <summary>Fixed name of the controller parameter. Null = <c>Type_field</c>.</summary>
        public string Name { get; }

        public AnimateAttribute() { }

        public AnimateAttribute(string name) => Name = name;
    }
}
