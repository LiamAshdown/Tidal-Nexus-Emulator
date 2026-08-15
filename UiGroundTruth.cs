using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace TidalNexus.StandaloneServer
{

    public sealed class UiGroundTruth : MonoBehaviour
    {
        public const KeyCode DumpKey = KeyCode.B;

        private static readonly string[] ColorProps =
        {
            "_InnerColor", "_BorderColor", "_PatternColor", "_VignetteColor", "_Color",
        };

        private static readonly string[] FloatProps =
        {
            "_PatternTiling", "_PatternMplier", "_Constant_InnerMultiplier",
            "_Constant_BorderMultiplier",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var host = new GameObject("~UiGroundTruth");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<UiGroundTruth>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(DumpKey))
            {
                StartCoroutine(DumpAtEndOfFrame());
            }
        }

        private System.Collections.IEnumerator DumpAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();

            Texture2D frame = null;
            try
            {
                frame = ScreenCapture.CaptureScreenshotAsTexture();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[UiGroundTruth] frame capture failed: {e.Message}");
            }

            Dump(frame);

            if (frame != null)
            {
                Destroy(frame);
            }
        }

        private static void Dump(Texture2D frame)
        {
            var sb = new StringBuilder(1 << 16);
            sb.AppendLine($"screen {Screen.width}x{Screen.height}");

            foreach (Canvas canvas in FindObjectsByType<Canvas>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas)
                {
                    continue;
                }

                var scaler = canvas.GetComponent<CanvasScaler>();
                sb.AppendLine(
                    $"canvas '{canvas.name}' mode={canvas.renderMode} " +
                    $"scaleFactor={canvas.scaleFactor:F3}" +
                    (scaler == null
                        ? " (no scaler)"
                        : $" scalerMode={scaler.uiScaleMode} " +
                          $"refRes={scaler.referenceResolution.x}x{scaler.referenceResolution.y} " +
                          $"match={scaler.matchWidthOrHeight:F2}"));

                foreach (Graphic g in canvas.GetComponentsInChildren<Graphic>(false))
                {
                    Rect rect = ScreenRect(g.rectTransform);
                    if (rect.width * rect.height < 1000f)
                    {
                        continue;
                    }

                    Material m = g.materialForRendering;
                    string sprite = g is Image img && img.sprite != null ? img.sprite.name : "-";

                    sb.Append($"  {Path(g.transform)} | {g.GetType().Name}")
                      .Append($" | rect={rect.x:F0},{rect.y:F0} {rect.width:F0}x{rect.height:F0}")
                      .Append($" | px={Pixel(frame, rect)}")
                      .Append($" | col={Fmt(g.color)}")
                      .Append($" | sprite={sprite}")
                      .Append($" | mat='{(m != null ? m.name : "-")}'")
                      .Append($" shader='{(m != null && m.shader != null ? m.shader.name : "-")}'");

                    if (m != null)
                    {
                        foreach (string p in ColorProps)
                        {
                            if (m.HasProperty(p))
                            {
                                sb.Append($" {p}={Fmt(m.GetColor(p))}");
                            }
                        }

                        foreach (string p in FloatProps)
                        {
                            if (m.HasProperty(p))
                            {
                                sb.Append($" {p}={m.GetFloat(p).ToString("F3", CultureInfo.InvariantCulture)}");
                            }
                        }
                    }

                    sb.AppendLine();
                }
            }

            string file = System.IO.Path.Combine(
                Application.dataPath, "..", "uidump-ours.txt");
            System.IO.File.WriteAllText(file, sb.ToString());
            Debug.Log($"[UiGroundTruth] wrote {file}");
        }

        private static Rect ScreenRect(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            float xMin = corners[0].x, yMin = corners[0].y;
            float xMax = corners[2].x, yMax = corners[2].y;
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private static string Pixel(Texture2D frame, Rect rect)
        {
            if (frame == null)
            {
                return "-";
            }

            int x = Mathf.Clamp((int)rect.center.x, 0, frame.width - 1);
            int y = Mathf.Clamp((int)rect.center.y, 0, frame.height - 1);
            Color32 c = frame.GetPixel(x, y);
            return $"{c.r},{c.g},{c.b}";
        }

        private static string Fmt(Color c)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "({0:F3},{1:F3},{2:F3},a{3:F3})", c.r, c.g, c.b, c.a);
        }

        private static string Path(Transform t)
        {
            string path = t.name;
            int guard = 0;
            while (t.parent != null && guard++ < 24)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
        }
    }
}
