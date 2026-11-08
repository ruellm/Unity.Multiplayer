using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Multiplayer.EditorTools
{
    public static class RtsPrefabReport
    {
        static readonly string[] PrefabNames = { "PF_Tank", "PF_Tower", "PF_HQ", "PF_Wall", "PF_Soldier" };

        [MenuItem("Tools/RTS/Report Prefab Bounds")]
        public static void Run()
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("RTS prefab report " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("Root placed at origin, identity rotation, scale 1. All values in root space.");
            report.AppendLine();

            foreach (string prefabName in PrefabNames)
            {
                string path = FindPrefabPath(prefabName);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ReportPrefab(report, prefabName, path, root);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            string projectDir = Path.GetDirectoryName(Application.dataPath);
            string logsDir = Path.Combine(projectDir, "Logs");
            Directory.CreateDirectory(logsDir);
            string file = Path.Combine(logsDir, "rts_prefab_report.txt");
            File.WriteAllText(file, report.ToString());
            Debug.Log("RTS prefab report written to " + file);
        }

        // Combined renderer bounds with the root at origin, identity rotation, scale 1. Nothing is saved.
        public static Bounds CombinedRendererBounds(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform rootTransform = root.transform;
                rootTransform.localPosition = Vector3.zero;
                rootTransform.localRotation = Quaternion.identity;
                rootTransform.localScale = Vector3.one;

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                    throw new InvalidOperationException("Prefab has no renderers: " + path);

                Bounds combined = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    combined.Encapsulate(renderers[i].bounds);
                return combined;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static string FindPrefabPath(string prefabName)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab " + prefabName);
            List<string> matches = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == prefabName)
                    matches.Add(path);
            }

            if (matches.Count == 0)
                throw new InvalidOperationException("Prefab not found: " + prefabName);
            if (matches.Count > 1)
                throw new InvalidOperationException("Prefab found more than once: " + prefabName + " at " + string.Join(", ", matches));

            return matches[0];
        }

        static void ReportPrefab(StringBuilder sb, string prefabName, string path, GameObject root)
        {
            Transform rootTransform = root.transform;
            rootTransform.localPosition = Vector3.zero;
            rootTransform.localRotation = Quaternion.identity;
            rootTransform.localScale = Vector3.one;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

            sb.AppendLine("==== " + prefabName);
            sb.AppendLine("Path: " + path);

            if (renderers.Length == 0)
            {
                sb.AppendLine("No renderers.");
                sb.AppendLine();
                return;
            }

            Bounds combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                combined.Encapsulate(renderers[i].bounds);

            sb.AppendLine("Combined bounds: center " + V(combined.center) + " size " + V(combined.size));
            sb.AppendLine("Lowest y: " + F(combined.min.y) + "  highest y: " + F(combined.max.y));
            sb.AppendLine();

            sb.AppendLine("Renderers:");
            foreach (Renderer renderer in renderers)
            {
                Transform t = renderer.transform;
                sb.AppendLine("  " + PathFrom(rootTransform, t) + "  [" + renderer.GetType().Name + "]");
                sb.AppendLine("    materials: " + MaterialNames(renderer));
                sb.AppendLine("    bounds center " + V(renderer.bounds.center) + " size " + V(renderer.bounds.size));
                sb.AppendLine("    local pos " + V(t.localPosition) + " euler " + V(t.localEulerAngles) + " scale " + V(t.localScale));
                sb.AppendLine("    world pos " + V(t.position) + " euler " + V(t.eulerAngles));
            }
            sb.AppendLine();

            HashSet<Transform> listed = new HashSet<Transform>();
            listed.Add(rootTransform);
            foreach (Renderer renderer in renderers)
                AddWithAncestors(listed, renderer.transform, rootTransform);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "RifleSocket")
                    AddWithAncestors(listed, t, rootTransform);
            }

            sb.AppendLine("Hierarchy (renderer transforms, their ancestors, RifleSocket):");
            WriteHierarchy(sb, rootTransform, rootTransform, listed, 1);
            sb.AppendLine();

            sb.AppendLine("Components:");
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!listed.Contains(t))
                    continue;

                List<string> names = new List<string>();
                foreach (Component c in t.GetComponents<Component>())
                {
                    if (c == null)
                    {
                        names.Add("MISSING SCRIPT");
                        continue;
                    }

                    string name = c.GetType().Name;
                    if (c is Collider)
                        name += " <COLLIDER>";
                    if (c is Animator)
                        name += " <ANIMATOR>";
                    names.Add(name);
                }
                sb.AppendLine("  " + PathFrom(rootTransform, t) + ": " + string.Join(", ", names));
            }
            sb.AppendLine();

            sb.AppendLine("Facing hints:");
            foreach (Renderer renderer in renderers)
            {
                Transform t = renderer.transform;
                if (t == rootTransform)
                    continue;

                Bounds b = renderer.bounds;
                Vector3 pivot = t.position;
                sb.AppendLine("  " + PathFrom(rootTransform, t) + " extends from pivot: +X " + F(b.max.x - pivot.x)
                    + "  -X " + F(pivot.x - b.min.x)
                    + "  +Z " + F(b.max.z - pivot.z)
                    + "  -Z " + F(pivot.z - b.min.z)
                    + "  +Y " + F(b.max.y - pivot.y)
                    + "  -Y " + F(pivot.y - b.min.y));
            }

            if (prefabName == "PF_Soldier")
            {
                bool found = false;
                foreach (Renderer renderer in renderers)
                {
                    if (renderer.transform.name != "Rifle" && !IsUnder(renderer.transform, "Rifle"))
                        continue;

                    found = true;
                    sb.AppendLine("  Rifle renderer " + PathFrom(rootTransform, renderer.transform) + " bounds center relative to root: " + V(renderer.bounds.center));
                }
                if (!found)
                    sb.AppendLine("  Rifle renderer not found.");
            }

            if (prefabName == "PF_Wall")
            {
                string longest = combined.size.x > combined.size.z ? "X" : (combined.size.z > combined.size.x ? "Z" : "X and Z equal");
                sb.AppendLine("  Longest horizontal axis: " + longest + " (x " + F(combined.size.x) + ", z " + F(combined.size.z) + ")");
            }
            sb.AppendLine();

            Animator[] animators = root.GetComponentsInChildren<Animator>(true);
            if (animators.Length > 0)
            {
                sb.AppendLine("Animators:");
                foreach (Animator animator in animators)
                {
                    string controller = animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "(none)";
                    string avatar = animator.avatar != null ? animator.avatar.name : "(none)";
                    sb.AppendLine("  " + PathFrom(rootTransform, animator.transform) + ": controller " + controller
                        + ", avatar " + avatar
                        + ", humanoid " + (animator.avatar != null && animator.avatar.isHuman)
                        + ", applyRootMotion " + animator.applyRootMotion);
                }
                sb.AppendLine();
            }
        }

        static void AddWithAncestors(HashSet<Transform> set, Transform t, Transform root)
        {
            while (t != null)
            {
                set.Add(t);
                if (t == root)
                    return;
                t = t.parent;
            }
        }

        static void WriteHierarchy(StringBuilder sb, Transform t, Transform root, HashSet<Transform> listed, int depth)
        {
            if (!listed.Contains(t))
                return;

            sb.AppendLine(new string(' ', depth * 2) + PathFrom(root, t)
                + "  pos " + V(t.localPosition) + " euler " + V(t.localEulerAngles) + " scale " + V(t.localScale));

            for (int i = 0; i < t.childCount; i++)
                WriteHierarchy(sb, t.GetChild(i), root, listed, depth + 1);
        }

        static bool IsUnder(Transform t, string ancestorName)
        {
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                if (p.name == ancestorName)
                    return true;
            }
            return false;
        }

        static string PathFrom(Transform root, Transform t)
        {
            if (t == root)
                return root.name;

            string path = t.name;
            for (Transform p = t.parent; p != null && p != root; p = p.parent)
                path = p.name + "/" + path;
            return root.name + "/" + path;
        }

        static string MaterialNames(Renderer renderer)
        {
            Material[] materials = renderer.sharedMaterials;
            string[] names = new string[materials.Length];
            for (int i = 0; i < materials.Length; i++)
                names[i] = materials[i] != null ? materials[i].name : "(null)";
            return string.Join(", ", names);
        }

        static string V(Vector3 v)
        {
            return "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";
        }

        static string F(float f)
        {
            return f.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
