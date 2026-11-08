using UnityEngine;

namespace RTS
{
    public class TeamColor : MonoBehaviour
    {
        [SerializeField] Color teamColor = new Color32(21, 69, 148, 255);
        [SerializeField] Renderer[] renderers;

        // Materials the renderers had before any team color was applied.
        // Null entries mark renderers that were skipped in Awake.
        Material[] baseMaterials;

        void Reset()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        void Awake()
        {
            if (renderers == null)
                renderers = new Renderer[0];

            baseMaterials = new Material[renderers.Length];

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                Material material = renderer.sharedMaterial;
                if (material == null)
                    continue;

                if (!material.HasProperty("_TeamColor"))
                {
                    Debug.LogWarning($"TeamColor: material '{material.name}' on renderer '{renderer.name}' has no _TeamColor property, renderer skipped.", renderer);
                    continue;
                }

                baseMaterials[i] = material;
            }
        }

        void Start()
        {
            Apply();
        }

        public void SetTeamColor(Color color)
        {
            teamColor = color;
            Apply();
        }

        void Apply()
        {
            if (baseMaterials == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Material baseMaterial = baseMaterials[i];
                if (baseMaterial == null || renderers[i] == null)
                    continue;

                renderers[i].sharedMaterial = TeamColorMaterialCache.Get(baseMaterial, teamColor);
            }
        }

        void OnValidate()
        {
            if (Application.isPlaying && isActiveAndEnabled)
                Apply();
        }
    }
}
