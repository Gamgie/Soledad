using UnityEngine;

namespace Soledad
{
    public class PhoneScreen : MonoBehaviour
    {
        public float minZ, maxZ;
        [Animate] [Range(0,1)] public float Zdepth;
        [Animate] [Range(0,1)] public float alpha;

        MeshRenderer meshRenderer;

        void Start()
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        // Update is called once per frame
        void Update()
        {
            transform.localPosition = new Vector3(transform.localPosition.x, transform.localPosition.y, Mathf.Lerp(maxZ, minZ, Zdepth));
            
            // sharedMaterial: no per-object material copy. It edits the material asset itself, so only write on change.
            if (meshRenderer != null && meshRenderer.sharedMaterial != null)
            {
                var color = new Color(1, 1, 1, alpha);
                if (meshRenderer.sharedMaterial.color != color) meshRenderer.sharedMaterial.color = color;
            }

        }
    }
}
