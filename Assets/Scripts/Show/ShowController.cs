using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Soledad
{
    // Runs the acts of the show. Each act is its own Unity scene (Assets/Scenes/Acts), with one root object that
    // carries an Act component.
    //
    // OSCQuery only exposes one scene (Soledad_Main, under its Root Scene object), so an act scene is loaded
    // additively, its root object is moved under actsParent in the main scene, and the emptied act scene is
    // unloaded right away. Chataigne sees the act's objects after its next sync. Leaving an act destroys them.
    //
    // Driven by Chataigne (or a Timeline when rendering the film) through the act field: changing it crossfades
    // from the shown act to the new one over transitionDuration, then preloads the following act, inactive.
    public class ShowController : MonoBehaviour
    {
        [OSCQuery.DoNotExpose]
        [Tooltip("Act scenes in show order. They must be in the build scene list.")]
        public string[] actScenes =
        {
            "Act1_BeyondTheVeil",
            "Act2_Flythrough",
            "Act3_Abyss",
            "Act4_SeeThrough",
            "Act5_MementoMori",
        };

        [OSCQuery.DoNotExpose]
        [Tooltip("Where loaded acts are parented: the OSCQuery root object (Root Scene), so Chataigne sees them.")]
        public Transform actsParent;

        [Tooltip("Act to show: 1 to 5, 0 for none.")]
        public int act;

        [Tooltip("Crossfade between two acts, in seconds.")]
        [Range(0f, 60f)]
        public float transitionDuration = 4f;

        [Tooltip("Once an act is shown, load the next one in the background (inactive) so the switch has no hitch.")]
        public bool preloadNextAct = true;

        readonly Dictionary<int, Act> _loaded = new Dictionary<int, Act>();
        readonly HashSet<int> _loading = new HashSet<int>();
        readonly List<int> _toUnload = new List<int>();
        Scene _mainScene;
        Act _shownAct;
        int _shownIndex;
        bool _switching;

        public void NextAct() => act = Mathf.Min(act + 1, actScenes.Length);

        public void PreviousAct() => act = Mathf.Max(act - 1, 0);

        void Start()
        {
            _mainScene = gameObject.scene;
            AdoptOpenActScenes();
        }

        void Update()
        {
            if (!_switching && act != _shownIndex)
                StartCoroutine(SwitchTo(Mathf.Clamp(act, 0, actScenes.Length)));
        }

        // In the Editor, an act scene opened next to Soledad_Main (to work on it in context) is taken over on Play
        // and shown at once.
        void AdoptOpenActScenes()
        {
            for (int s = SceneManager.sceneCount - 1; s >= 0; s--)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (scene == _mainScene || !scene.isLoaded) continue;

                int index = System.Array.IndexOf(actScenes, scene.name) + 1;
                if (index == 0) continue;

                Act adopted = MoveIntoMainScene(scene, index);
                SceneManager.UnloadSceneAsync(scene);

                if (_shownAct == null)
                {
                    adopted.weight = 1f;
                    adopted.gameObject.SetActive(true);
                    _shownAct = adopted;
                    _shownIndex = index;
                    act = index;
                }
            }
        }

        IEnumerator SwitchTo(int target)
        {
            _switching = true;

            Act next = null;
            if (target > 0)
            {
                yield return Load(target);
                _loaded.TryGetValue(target, out next);
            }

            Act previous = _shownAct;
            if (next != null && next != previous)
            {
                next.weight = 0f;
                next.gameObject.SetActive(true);
            }

            if (next != previous)
            {
                float time = 0f;
                while (time < transitionDuration)
                {
                    time += Time.deltaTime;
                    float k = Mathf.Clamp01(time / transitionDuration);
                    if (next != null) next.weight = k;
                    if (previous != null) previous.weight = 1f - k;
                    yield return null;
                }
            }

            if (next != null) next.weight = 1f;
            _shownAct = next;
            _shownIndex = target;

            // Keep only the shown act and the next one.
            _toUnload.Clear();
            foreach (int index in _loaded.Keys)
                if (index != target && index != target + 1)
                    _toUnload.Add(index);
            foreach (int index in _toUnload)
            {
                Destroy(_loaded[index].gameObject);
                _loaded.Remove(index);
            }
            if (_toUnload.Count > 0)
                Resources.UnloadUnusedAssets();

            _switching = false;

            if (preloadNextAct && target > 0 && target < actScenes.Length)
                StartCoroutine(Load(target + 1));
        }

        // Loads act `index` (1-based) into the main scene, inactive. Waits if it is already loading.
        IEnumerator Load(int index)
        {
            if (_loaded.ContainsKey(index)) yield break;

            if (_loading.Contains(index))
            {
                while (_loading.Contains(index)) yield return null;
                yield break;
            }

            string sceneName = actScenes[index - 1];
            AsyncOperation loading = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (loading == null)
            {
                Debug.LogError($"ShowController: cannot load {sceneName}, add it to the build scene list.");
                yield break;
            }

            _loading.Add(index);
            yield return loading;

            Scene scene = SceneManager.GetSceneByName(sceneName);
            MoveIntoMainScene(scene, index);
            yield return SceneManager.UnloadSceneAsync(scene);
            _loading.Remove(index);
        }

        // Moves the act's root object into the main scene. Extra root objects are parented under it; a scene
        // without an Act root gets one, named after the scene.
        Act MoveIntoMainScene(Scene scene, int index)
        {
            GameObject[] roots = scene.GetRootGameObjects();

            Act actRoot = null;
            foreach (GameObject root in roots)
                if (root.TryGetComponent(out actRoot)) break;

            if (actRoot == null)
            {
                Debug.LogWarning($"ShowController: {scene.name} has no root object with an Act component, one is added.");
                GameObject created = new GameObject(scene.name);
                SceneManager.MoveGameObjectToScene(created, scene);
                actRoot = created.AddComponent<Act>();
            }

            foreach (GameObject root in roots)
                if (root != actRoot.gameObject)
                    root.transform.SetParent(actRoot.transform, true);

            actRoot.gameObject.SetActive(false);
            actRoot.weight = 0f;
            SceneManager.MoveGameObjectToScene(actRoot.gameObject, _mainScene);
            if (actsParent != null)
                actRoot.transform.SetParent(actsParent, true);
            _loaded[index] = actRoot;
            return actRoot;
        }
    }
}
