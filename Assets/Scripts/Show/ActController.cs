using UnityEngine;
using UnityEngine.SceneManagement;

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
    ///
    /// In edit mode (to build a Timeline without Play) it binds to the act scene opened next to Soledad_Main and
    /// pushes only the values that change on the controller (Timeline preview, inspector), each time they change: a
    /// value set on the act itself stays until the controller's own value changes.
    /// </summary>
    [ExecuteAlways]
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

        /// <summary>Edit mode: takes the current controller values as already pushed.</summary>
        protected virtual void Remember() { }

        /// <summary>Edit mode: copies into the targets the values that changed since the last call. True if any did.</summary>
        protected virtual bool PushChanged() => false;

        protected virtual void Awake() => _show = GetComponent<ShowController>();

        protected virtual void OnEnable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.update += EditorTick;
#endif
        }

        protected virtual void Update()
        {
            if (!Application.isPlaying) return;
            if (!CheckBinding()) return;
            Push();
        }

        protected virtual void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
            if (!_bound) return;
            _bound = false;
            _root = null;
            Unbind();
        }

        // Drops the targets when the act is gone, binds to the act when it appears. True when bound.
        bool CheckBinding()
        {
            if (_bound && _root == null)
            {
                _bound = false;
                Unbind();
            }
            if (_bound) return true;
            _root = FindActRoot();
            if (_root == null) return false;
            Bind(_root);
            _bound = true;
            if (!Application.isPlaying) Remember();
            return true;
        }

#if UNITY_EDITOR
        double _nextSearch;

        // Edit mode, every editor update: push what the Timeline preview or the inspector changed on the controller.
        void EditorTick()
        {
            if (this == null || Application.isPlaying) return;
            if (!_bound)
            {
                // The act scene is opened and closed by hand: look for it twice a second
                double now = UnityEditor.EditorApplication.timeSinceStartup;
                if (now < _nextSearch) return;
                _nextSearch = now + 0.5;
            }
            if (!CheckBinding()) return;
            if (PushChanged()) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();   // act scripts and views update
        }
#endif

        // Play mode: act roots are moved under ShowController.actsParent (Root Scene) when loaded. Edit mode: the
        // root of the act scene opened next to Soledad_Main.
        Transform FindActRoot()
        {
            string actName = ActName;
            Transform parent = _show != null && _show.actsParent != null ? _show.actsParent : transform.parent;
            if (parent != null)
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);
                    if (child.name == actName && child.TryGetComponent<Act>(out _)) return child;
                }
            if (Application.isPlaying) return null;

            Scene scene = SceneManager.GetSceneByName(actName);
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == actName && root.TryGetComponent<Act>(out _)) return root.transform;
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
