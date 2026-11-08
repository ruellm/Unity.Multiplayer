using System.Collections.Generic;
using UnityEngine;

namespace RTS
{
    // One material per (base material, team color). Renderers that share a base
    // material and a color share the material instance returned here.
    public static class TeamColorMaterialCache
    {
        static readonly int TeamColorId = Shader.PropertyToID("_TeamColor");

        static readonly Dictionary<(Material, int), Material> cache = new Dictionary<(Material, int), Material>();

        public static Material Get(Material baseMaterial, Color teamColor)
        {
            var key = (baseMaterial, Pack(teamColor));

            if (cache.TryGetValue(key, out Material material) && material != null)
                return material;

            material = new Material(baseMaterial)
            {
                name = baseMaterial.name + "_" + ColorUtility.ToHtmlStringRGBA(teamColor)
            };
            material.SetColor(TeamColorId, teamColor);
            cache[key] = material;
            return material;
        }

        // Color32 has no custom equality, so the four bytes are packed into
        // an int to serve as the dictionary key.
        static int Pack(Color color)
        {
            Color32 c = color;
            return (c.r << 24) | (c.g << 16) | (c.b << 8) | c.a;
        }

        // Cached materials do not survive a domain reload or a new play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Clear()
        {
            foreach (Material material in cache.Values)
            {
                if (material != null)
                    Object.Destroy(material);
            }
            cache.Clear();
        }
    }
}
