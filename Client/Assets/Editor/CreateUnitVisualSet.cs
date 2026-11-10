using System.Collections.Generic;
using Multiplayer.Game;
using UnityEditor;
using UnityEngine;

namespace Multiplayer.EditorTools
{
    public static class CreateUnitVisualSet
    {
        const string FolderPath = "Assets/Resources";
        const string AssetPath = FolderPath + "/" + UnitVisualSet.ResourceName + ".asset";

        [MenuItem("Tools/RTS/Create Unit Visual Set")]
        public static void Run()
        {
            UnitVisualEntry[] defaults = BuildDefaults();

            if (!AssetDatabase.IsValidFolder(FolderPath))
                AssetDatabase.CreateFolder("Assets", "Resources");

            UnitVisualSet set = AssetDatabase.LoadAssetAtPath<UnitVisualSet>(AssetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<UnitVisualSet>();
                set.Entries = defaults;
                AssetDatabase.CreateAsset(set, AssetPath);
                Debug.Log("Created " + AssetPath);
            }
            else
            {
                List<UnitVisualEntry> entries = new List<UnitVisualEntry>();
                if (set.Entries != null)
                    entries.AddRange(set.Entries);

                foreach (UnitVisualEntry d in defaults)
                {
                    UnitVisualEntry existing = set.Find(d.Name);
                    if (existing == null)
                    {
                        entries.Add(d);
                        continue;
                    }

                    existing.Prefab = d.Prefab;
                    if (string.IsNullOrEmpty(existing.TurretPivotName))
                        existing.TurretPivotName = d.TurretPivotName;
                    if (existing.TurretTurnRate <= 0f)
                        existing.TurretTurnRate = d.TurretTurnRate;
                    if (existing.DeathClipSpeed <= 0f)
                        existing.DeathClipSpeed = d.DeathClipSpeed;
                    if (existing.CorpseHoldSeconds <= 0f)
                        existing.CorpseHoldSeconds = d.CorpseHoldSeconds;
                    if (existing.RunClipSpeed <= 0f)
                        existing.RunClipSpeed = d.RunClipSpeed;
                    if (existing.IconZoom <= 0f)
                    {
                        existing.IconZoom = d.IconZoom;
                        existing.IconYaw = d.IconYaw;
                    }
                }

                set.Entries = entries.ToArray();
                EditorUtility.SetDirty(set);
                Debug.Log("Refreshed prefab references in " + AssetPath);
            }

            AssetDatabase.SaveAssets();
            Selection.activeObject = set;
        }

        static UnitVisualEntry[] BuildDefaults()
        {
            return new[]
            {
                Entry("Soldier", "PF_Soldier", 1.1f, false, new Vector3(0f, 0.5f, 0f), new Vector3(0.45f, 1f, 0.45f), "", Vector3.zero, "", 3f, 15f),
                Entry("Tank", "PF_Tank", 2.6f, true, Vector3.zero, Vector3.zero, "TurretPivot", new Vector3(0f, 0.13f, 0.6f), "TurretPivot", 0f, -35f),
                Entry("HQ", "PF_HQ", 5f, true, Vector3.zero, Vector3.zero, "", Vector3.zero, "", 0f, -35f),
                Entry("Tower", "PF_Tower", 3f, true, Vector3.zero, Vector3.zero, "TurretPivot", new Vector3(0f, 0.15f, 0.56f), "TurretPivot", 0f, -35f),
                Entry("Wall", "PF_Wall", 5f, true, Vector3.zero, Vector3.zero, "", Vector3.zero, "", 0f, -35f)
            };
        }

        static UnitVisualEntry Entry(string name, string prefabName, float scale, bool colliderFromBounds, Vector3 center, Vector3 size, string attach, Vector3 offset, string turretPivot, float corpseHold, float iconYaw)
        {
            string path = RtsPrefabReport.FindPrefabPath(prefabName);

            UnitVisualEntry entry = new UnitVisualEntry();
            entry.Name = name;
            entry.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            entry.Scale = scale;
            entry.ColliderCenter = center;
            entry.ColliderSize = size;
            entry.MuzzleAttach = attach;
            entry.MuzzleOffset = offset;
            entry.TurretPivotName = turretPivot;
            entry.TurretTurnRate = 180f;
            entry.DeathClipSpeed = 1f;
            entry.CorpseHoldSeconds = corpseHold;
            entry.IconYaw = iconYaw;
            entry.IconZoom = 1f;
            entry.RunClipSpeed = name == "Soldier" ? 1.3f : 1f;

            if (colliderFromBounds)
            {
                Bounds bounds = RtsPrefabReport.CombinedRendererBounds(path);
                entry.ColliderCenter = bounds.center;
                entry.ColliderSize = bounds.size;
            }

            return entry;
        }
    }
}
