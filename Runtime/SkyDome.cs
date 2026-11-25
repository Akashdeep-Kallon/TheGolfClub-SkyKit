using UnityEngine;
using UnityEngine.Rendering;

namespace PaintedSky
{
    /// <summary>
    /// Painted sky dome: palette gradient, sunrise/sunset glow, painted sun, big pale moon, stars and a faint
    /// milky way. A large sphere that follows the camera and is drawn at the far plane after the opaques, so it
    /// works with a Solid Color camera clear and no skybox. The volumetric clouds are composited on top of it.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Painted Sky/Sky Dome")]
    public sealed class SkyDome : MonoBehaviour
    {
        [Tooltip("Painted Sky/Sky shader (assigned automatically; keep the reference so builds include it)")]
        public Shader skyShader;
        [Tooltip("Camera the dome follows. Empty = this GameObject's camera, or Camera.main.")]
        public Camera followCamera;
        [Tooltip("Dome radius (m). Keep it inside the camera's far clip plane.")]
        public float radius = 500f;

        GameObject dome;
        Material material;

#if UNITY_EDITOR
        void Reset() => skyShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.paintedsky.core/Shaders/PaintedSky.shader");
#endif

        void OnEnable()
        {
            if (!skyShader) skyShader = Shader.Find("Painted Sky/Sky");
            if (!skyShader) { Debug.LogError("[Painted Sky] Shader 'Painted Sky/Sky' not found."); enabled = false; return; }
            dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "Painted Sky Dome";
            dome.hideFlags = HideFlags.DontSave;
            var col = dome.GetComponent<Collider>();
            if (col) Destroy(col);
            dome.transform.localScale = Vector3.one * radius * 2f;
            var mr = dome.GetComponent<MeshRenderer>();
            material = new Material(skyShader) { hideFlags = HideFlags.DontSave };
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        void OnDisable()
        {
            if (dome) Destroy(dome);
            if (material) Destroy(material);
        }

        void LateUpdate()
        {
            if (!dome) return;
            var cam = followCamera ? followCamera : GetComponent<Camera>();
            if (!cam) cam = Camera.main;
            if (cam) dome.transform.position = cam.transform.position;
        }
    }
}
