using UnityEngine;
using UnityEngine.Rendering;

namespace Soledad
{
    /// <summary>
    /// Act 4: shell grass on the hill. The hill's material (Soledad/HillGrass) draws the ground; this draws the hill
    /// mesh <see cref="shells"/> more times for every camera (dome, Scene view in edit mode), each pushed out along the
    /// normal by its height in the grass (_ShellHeight, per draw) and cut into blades by the shader; fewer shells
    /// as the camera rises above the ground. Height, density,
    /// colours, fade distance are on the material. Nothing is drawn when the camera is farther than
    /// <see cref="drawDistance"/> from the surface (the shader has faded the blades out long before) or underground.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Hill))]
    public class HillGrass : MonoBehaviour
    {
        [Tooltip("Layers of grass above the ground with the camera in the grass. More = smoother blades seen from the side, more GPU cost close up.")]
        [Range(0, 96)] public int shells = 64;
        [Tooltip("Layers left when the camera is high above the ground (blades seen from above need fewer).")]
        [Range(0, 96)] public int minShells = 24;
        [Tooltip("Camera height above the ground (m) from which the layers go down from shells to minShells...")]
        public float fullShellsBelow = 0.5f;
        [Tooltip("...and the height (m) where they reach minShells.")]
        public float minShellsAbove = 6f;
        [Tooltip("Camera closer than this to the surface (m): ground only. Seen almost edge-on the shells show as slices; the scan alone looks better there.")]
        public float minCameraHeight = 0.2f;
        [Tooltip("Camera farther than this from the surface (m): ground only.")]
        public float drawDistance = 60f;
        [Tooltip("Draw the shells in edit mode (Scene view and cameras).")]
        public bool drawInEditMode = true;

        static readonly int ShellHeightId = Shader.PropertyToID("_ShellHeight");

        Hill _hill;
        MaterialPropertyBlock[] _blocks;

        void OnEnable()
        {
            _hill = GetComponent<Hill>();
            RenderPipelineManager.beginCameraRendering -= Draw;
            RenderPipelineManager.beginCameraRendering += Draw;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Draw;
        }

        void Draw(ScriptableRenderContext context, Camera cam)
        {
            if (shells <= 0 || _hill == null) return;
            if (!Application.isPlaying && !drawInEditMode) return;
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            Mesh mesh = _hill.GeneratedMesh;
            Material material = _hill.material;
            if (mesh == null || material == null) return;

            float radius = _hill.radius * transform.lossyScale.y;
            float height = Vector3.Distance(cam.transform.position, _hill.CenterWorld) - radius;
            if (height > drawDistance || height < minCameraHeight) return;

            if (_blocks == null || _blocks.Length < shells)
            {
                _blocks = new MaterialPropertyBlock[shells];
                for (int i = 0; i < shells; i++)
                    _blocks[i] = new MaterialPropertyBlock();
            }
            float k = Mathf.InverseLerp(fullShellsBelow, minShellsAbove, height);
            int count = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(shells, Mathf.Min(minShells, shells), k)), 1, shells);

            var rp = new RenderParams(material)
            {
                camera = cam,
                layer = gameObject.layer,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                motionVectorMode = MotionVectorGenerationMode.Camera,
            };
            Matrix4x4 matrix = _hill.MeshToWorld;
            // Top shell first: lower shells behind it fail the depth test before their pixels are shaded
            for (int i = 0; i < count; i++)
            {
                _blocks[i].SetFloat(ShellHeightId, (count - i) / (float)count);
                rp.matProps = _blocks[i];
                Graphics.RenderMesh(rp, mesh, 0, matrix);
            }
        }
    }
}
