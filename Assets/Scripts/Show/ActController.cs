using UnityEngine;

namespace Soledad
{
    /// <summary>
    /// Stable show parameters of one act. Lives on Show (Soledad_Main), so it is never unloaded: its fields keep the
    /// same Chataigne address and the same Timeline tracks whichever act is loaded.
    ///
    /// The subclasses (Act1Controller to Act5Controller, in Generated/) are written by the act controller generator
    /// from the [Animate] members of the act scripts; do not write them by hand. The controller is the source of
    /// truth: in Play mode, once its act root appears under Root Scene it finds its target components once, then
    /// pushes its values into them every frame (also while the act is preloaded and inactive). Values set while the
    /// act is not loaded are applied as soon as it arrives. When the act is destroyed, the references are dropped.
    /// </summary>
    public abstract class ActController : MonoBehaviour
    {
        ShowController _show;
        Transform _root;
        bool _bound;

        /// <summary>Name of the act scene, which is also the name of its root object.</summary>
        protected abstract string ActName { get; }

        /// <summary>Finds the target components under the act root. Runs once per loaded act.</summary>
        protected abstract void Bind(Transform root);

        /// <summary>Drops the target references.</summary>
        protected abstract void Unbind();

        /// <summary>Copies the controller values into the targets. No allocation.</summary>
        protected abstract void Push();

        protected virtual void Awake() => _show = GetComponent<ShowController>();

        protected virtual void Update()
        {
            if (_bound && _root == null)
            {
                _bound = false;
                Unbind();
            }
            if (!_bound)
            {
                _root = FindActRoot();
                if (_root == null) return;
                Bind(_root);
                _bound = true;
            }
            Push();
        }

        protected virtual void OnDisable()
        {
            if (!_bound) return;
            _bound = false;
            _root = null;
            Unbind();
        }

        // Act roots are moved under ShowController.actsParent (Root Scene) when loaded
        Transform FindActRoot()
        {
            Transform parent = _show != null && _show.actsParent != null ? _show.actsParent : transform.parent;
            if (parent == null) return null;
            string actName = ActName;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == actName && child.TryGetComponent<Act>(out _)) return child;
            }
            return null;
        }

        /// <summary>The target component of type T under the act root. Warns when there are several.</summary>
        protected T Find<T>(Transform root) where T : Component
        {
            T[] found = root.GetComponentsInChildren<T>(true);
            if (found.Length == 0) return null;
            if (found.Length > 1)
                Debug.LogWarning($"{GetType().Name}: {found.Length} {typeof(T).Name} components in {root.name}, only the first one is driven.", found[0]);
            return found[0];
        }
    }
}
