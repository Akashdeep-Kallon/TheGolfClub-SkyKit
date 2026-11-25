using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PaintedSky.Editor
{
    /// <summary>One-click setup of Painted Sky in any URP project.</summary>
    public static class PaintedSkySetup
    {
        public const string PackageRoot = "Packages/com.paintedsky.core";
        public const string SkyShaderPath = PackageRoot + "/Shaders/PaintedSky.shader";
        public const string CloudsMaterialPath = PackageRoot + "/ThirdParty/VolumetricCloudsURP/VolumetricClouds.mat";

        /// <summary>
        /// Adds the day/night cycle, the sky dome and the clouds to the main camera (or the selected camera) and
        /// installs the clouds renderer feature.
        /// </summary>
        [MenuItem("Tools/Painted Sky/Add Painted Sky To Main Camera")]
        public static void AddToMainCamera()
        {
            var cam = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<Camera>() : null;
            if (!cam) cam = Camera.main;
            if (!cam) { EditorUtility.DisplayDialog("Painted Sky", "No camera selected and no camera tagged MainCamera.", "OK"); return; }
            AddTo(cam);
            InstallRendererFeature();
            Selection.activeGameObject = cam.gameObject;
        }

        /// <summary>Adds (or completes) the Painted Sky components on a camera. Returns the cycle.</summary>
        public static DayNightCycle AddTo(Camera cam)
        {
            var go = cam.gameObject;
            var cycle = go.GetComponent<DayNightCycle>();
            if (!cycle) cycle = Undo.AddComponent<DayNightCycle>(go);
            if (!cycle.targetCamera) cycle.targetCamera = cam;

            var dome = go.GetComponent<SkyDome>();
            if (!dome) dome = Undo.AddComponent<SkyDome>(go);
            if (!dome.skyShader) dome.skyShader = AssetDatabase.LoadAssetAtPath<Shader>(SkyShaderPath);

            var clouds = go.GetComponent<PaintedClouds>();
            if (!clouds) clouds = Undo.AddComponent<PaintedClouds>(go);
            if (!clouds.cycle) clouds.cycle = cycle;

            // the dome needs the far plane beyond its radius
            if (cam.farClipPlane < dome.radius * 1.2f) { Undo.RecordObject(cam, "Painted Sky"); cam.farClipPlane = dome.radius * 1.6f; }
            var acd = cam.GetUniversalAdditionalCameraData();
            if (acd && !acd.renderPostProcessing) { Undo.RecordObject(acd, "Painted Sky"); acd.renderPostProcessing = true; }

            EditorUtility.SetDirty(cycle);
            EditorUtility.SetDirty(dome);
            EditorUtility.SetDirty(clouds);
            return cycle;
        }

        /// <summary>
        /// Adds the "Volumetric Clouds URP" renderer feature to every URP renderer used by the project
        /// (graphics settings and every quality level). Ambient is set to Static: Painted Sky feeds it.
        /// </summary>
        [MenuItem("Tools/Painted Sky/Install Clouds Renderer Feature")]
        public static void InstallRendererFeature()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(CloudsMaterialPath);
            if (!mat) { Debug.LogError("[Painted Sky] Missing " + CloudsMaterialPath); return; }

            int added = 0;
            foreach (var data in AllRendererData())
            {
                VolumetricCloudsURP feature = null;
                foreach (var f in data.rendererFeatures)
                    if (f is VolumetricCloudsURP v) feature = v;
                if (!feature)
                {
                    feature = ScriptableObject.CreateInstance<VolumetricCloudsURP>();
                    feature.name = "Volumetric Clouds URP";
                    AssetDatabase.AddObjectToAsset(feature, data);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                    var so = new SerializedObject(data);
                    var list = so.FindProperty("m_RendererFeatures");
                    var map = so.FindProperty("m_RendererFeatureMap");
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    added++;
                    Debug.Log("[Painted Sky] Clouds renderer feature added to " + AssetDatabase.GetAssetPath(data));
                }
                feature.CloudsMaterial = mat;
                feature.AmbientUpdateMode = VolumetricCloudsURP.CloudsAmbientMode.Static;
                feature.SetActive(true);
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
            if (added == 0) Debug.Log("[Painted Sky] Clouds renderer feature already installed.");
        }

        /// <summary>Every URP renderer data referenced by the graphics settings and the quality levels.</summary>
        static IEnumerable<ScriptableRendererData> AllRendererData()
        {
            var seen = new HashSet<ScriptableRendererData>();
            var assets = new List<RenderPipelineAsset> { GraphicsSettings.defaultRenderPipeline };
            for (int i = 0; i < QualitySettings.count; i++) assets.Add(QualitySettings.GetRenderPipelineAssetAt(i));
            foreach (var rp in assets)
            {
                if (rp is not UniversalRenderPipelineAsset urp) continue;
                var list = new SerializedObject(urp).FindProperty("m_RendererDataList");
                if (list == null) continue;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue is ScriptableRendererData d && seen.Add(d))
                        yield return d;
            }
        }
    }
}
