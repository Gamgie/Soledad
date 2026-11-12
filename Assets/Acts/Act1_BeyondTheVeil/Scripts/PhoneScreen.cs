using UnityEngine;

public class PhoneScreen : MonoBehaviour
{
    public float minZ, maxZ;
    [Range(0,1)] public float Zdepth;
    [Range(0,1)] public float alpha;

    MeshRenderer meshRenderer;

    void Start()
    {
        meshRenderer = GetComponent<MeshRenderer>();
    }

    // Update is called once per frame
    void Update()
    {
        transform.localPosition = new Vector3(transform.localPosition.x, transform.localPosition.y, Mathf.Lerp(maxZ, minZ, Zdepth));
        
        if(meshRenderer != null)
            meshRenderer.material.color = new Color(1, 1, 1, alpha);

    }
}
