using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class ArenaGridGenerator : MonoBehaviour
{
    [Header("Grid Visual Settings")]
    public Color backgroundColor = new Color(0.12f, 0.14f, 0.18f); // Dark slate grey
    public Color gridLineColor = new Color(0.0f, 0.85f, 1.0f);     // Flashy cyan grid lines
    public int textureSize = 64;
    public int lineThickness = 3;
    public float gridDensity = 1f; // 1 grid square per meter

    private void Awake()
    {
        GenerateGridMaterial();
    }

    private void GenerateGridMaterial()
    {
        Texture2D tex = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point; // Keeps lines razor-sharp with zero memory cost
        tex.wrapMode = TextureWrapMode.Repeat;

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                bool isBorder = (x < lineThickness || y < lineThickness);
                tex.SetPixel(x, y, isBorder ? gridLineColor : backgroundColor);
            }
        }
        tex.Apply();

        Renderer rend = GetComponent<Renderer>();
        Material mat = new Material(rend.sharedMaterial);
        mat.mainTexture = tex;

        // Automatically scale grid tiling to match object scale
        // Unity Planes are 10x10 units by default, Cubes are 1x1
        bool isPlane = GetComponent<MeshFilter>() != null && GetComponent<MeshFilter>().sharedMesh.name.Contains("Plane");
        float multiplier = isPlane ? 10f : 1f;

        Vector2 tiling = new Vector2(transform.lossyScale.x * multiplier * gridDensity, transform.lossyScale.z * multiplier * gridDensity);
        mat.mainTextureScale = tiling;

        // Support both Built-In and URP shaders automatically
        if (mat.HasProperty("_BaseMap"))
        {
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", tiling);
        }

        rend.material = mat;
    }
}