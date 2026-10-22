using System.Collections.Generic;
using UnityEngine;

// Plain colored materials made in code, so the track and car need no assets.
//
// Trick: copy the material Unity puts on a brand-new cube. That's the right default shader for
// whatever render pipeline the project uses (Built-in or URP), so the colors show up either way.
public static class SimpleMaterials
{
    static Material template;
    static readonly Dictionary<Color, Material> cache = new Dictionary<Color, Material>();

    public static Material Get(Color color)
    {
        if (cache.TryGetValue(color, out Material existing)) return existing;

        if (template == null)
        {
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            template = probe.GetComponent<Renderer>().sharedMaterial;
            Object.Destroy(probe);
        }
        var material = new Material(template) { color = color };
        cache[color] = material;
        return material;
    }

    // A primitive with a color and no collider (the server does the physics, not Unity).
    public static GameObject Part(PrimitiveType type, string name, Transform parent,
                                  Vector3 localPosition, Vector3 localScale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        Object.Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.GetComponent<Renderer>().sharedMaterial = Get(color);
        return part;
    }
}
