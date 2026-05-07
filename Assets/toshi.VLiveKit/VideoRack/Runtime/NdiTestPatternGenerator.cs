#if UNITY_2022_3_OR_NEWER
using UnityEngine;

namespace VLiveKit.VideoRack
{
    [ExecuteAlways]
    public sealed class NdiTestPatternGenerator : MonoBehaviour
    {
        [SerializeField] private RenderTexture targetTexture;
        [SerializeField] private PatternMode patternMode = PatternMode.BarsAndGrid;
        [SerializeField, Range(0f, 4f)] private float speed = 1f;
        [SerializeField, Range(0f, 1f)] private float lineIntensity = 0.65f;

        private Material material;

        public RenderTexture TargetTexture
        {
            get => targetTexture;
            set => targetTexture = value;
        }

        public PatternMode Pattern
        {
            get => patternMode;
            set => patternMode = value;
        }

        public float Speed
        {
            get => speed;
            set => speed = Mathf.Max(0f, value);
        }

        private void OnEnable()
        {
            EnsureMaterial();
        }

        private void OnDisable()
        {
            if (material == null)
                return;

            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);

            material = null;
        }

        private void Update()
        {
            RenderPattern();
        }

        private void RenderPattern()
        {
            if (targetTexture == null)
                return;

            EnsureMaterial();
            if (material == null)
                return;

            var previous = RenderTexture.active;
            RenderTexture.active = targetTexture;

            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, targetTexture.width, targetTexture.height, 0f);
            GL.Clear(true, true, Color.black);

            material.SetPass(0);
            DrawColorBars(targetTexture.width, targetTexture.height);

            if (patternMode == PatternMode.BarsAndGrid || patternMode == PatternMode.MotionGrid)
                DrawGrid(targetTexture.width, targetTexture.height);

            DrawScanMarker(targetTexture.width, targetTexture.height);
            DrawFrame(targetTexture.width, targetTexture.height);

            GL.PopMatrix();
            RenderTexture.active = previous;
        }

        private void EnsureMaterial()
        {
            if (material != null)
                return;

            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                return;

            material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
        }

        private void DrawColorBars(int width, int height)
        {
            var colors = new[]
            {
                new Color(0.96f, 0.94f, 0.86f),
                new Color(0.96f, 0.88f, 0.18f),
                new Color(0.05f, 0.78f, 0.82f),
                new Color(0.10f, 0.80f, 0.28f),
                new Color(0.88f, 0.18f, 0.72f),
                new Color(0.88f, 0.16f, 0.14f),
                new Color(0.08f, 0.18f, 0.80f),
                new Color(0.08f, 0.08f, 0.09f)
            };

            var barWidth = width / (float)colors.Length;
            GL.Begin(GL.QUADS);
            for (var i = 0; i < colors.Length; i++)
            {
                GL.Color(colors[i]);
                var x0 = i * barWidth;
                var x1 = i == colors.Length - 1 ? width : (i + 1) * barWidth;
                VertexQuad(x0, 0f, x1, height);
            }
            GL.End();

            DrawLowerRamp(width, height);
        }

        private void DrawLowerRamp(int width, int height)
        {
            var rampHeight = Mathf.Max(48f, height * 0.16f);
            var y0 = height - rampHeight;
            var blockWidth = width / 6f;
            var colors = new[]
            {
                new Color(0.03f, 0.03f, 0.035f),
                new Color(0.18f, 0.18f, 0.18f),
                new Color(0.40f, 0.40f, 0.40f),
                new Color(0.70f, 0.70f, 0.70f),
                new Color(0.04f, 0.62f, 0.70f),
                new Color(0.92f, 0.92f, 0.88f)
            };

            GL.Begin(GL.QUADS);
            for (var i = 0; i < colors.Length; i++)
            {
                GL.Color(colors[i]);
                VertexQuad(i * blockWidth, y0, (i + 1) * blockWidth, height);
            }
            GL.End();
        }

        private void DrawGrid(int width, int height)
        {
            var phase = (float)((Application.isPlaying ? Time.time : Time.realtimeSinceStartup) * speed * 80f);
            var spacing = patternMode == PatternMode.MotionGrid ? 64f : 96f;
            var offset = patternMode == PatternMode.MotionGrid ? phase % spacing : 0f;
            var color = new Color(0f, 0f, 0f, lineIntensity);

            GL.Begin(GL.QUADS);
            GL.Color(color);
            for (var x = -offset; x < width; x += spacing)
                VertexQuad(x, 0f, x + 2f, height);

            for (var y = offset; y < height; y += spacing)
                VertexQuad(0f, y, width, y + 2f);
            GL.End();
        }

        private void DrawScanMarker(int width, int height)
        {
            var time = (float)(Application.isPlaying ? Time.time : Time.realtimeSinceStartup);
            var x = Mathf.Repeat(time * speed * width * 0.18f, width);
            var markerWidth = Mathf.Max(12f, width * 0.015f);

            GL.Begin(GL.QUADS);
            GL.Color(new Color(1f, 1f, 1f, 0.36f));
            VertexQuad(x, 0f, Mathf.Min(width, x + markerWidth), height);
            GL.Color(new Color(0f, 0f, 0f, 0.25f));
            VertexQuad(Mathf.Max(0f, x - 3f), 0f, x, height);
            GL.End();
        }

        private static void DrawFrame(int width, int height)
        {
            const float thickness = 6f;
            GL.Begin(GL.QUADS);
            GL.Color(new Color(0f, 0f, 0f, 0.75f));
            VertexQuad(0f, 0f, width, thickness);
            VertexQuad(0f, height - thickness, width, height);
            VertexQuad(0f, 0f, thickness, height);
            VertexQuad(width - thickness, 0f, width, height);
            GL.End();
        }

        private static void VertexQuad(float x0, float y0, float x1, float y1)
        {
            GL.Vertex3(x0, y0, 0f);
            GL.Vertex3(x1, y0, 0f);
            GL.Vertex3(x1, y1, 0f);
            GL.Vertex3(x0, y1, 0f);
        }

        public enum PatternMode
        {
            BarsAndGrid,
            MotionGrid
        }
    }
}
#endif
