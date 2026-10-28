using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Inspection.UI
{
    /// <summary>
    /// 为 uGUI Image 生成九宫格圆角精灵：纹理在运行时按半径生成并缓存，
    /// 不引入外部美术资源，也不改变布局与射线区域。
    /// 编辑器里只序列化半径，Awake 时才真正替换精灵。
    /// </summary>
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public sealed class RoundedImage : MonoBehaviour
    {
        [SerializeField] private float radius = 12f;

        private static readonly Dictionary<int, Sprite> Sprites = new Dictionary<int, Sprite>();

        /// <summary>圆角半径（UI 设计单位，未乘以 Canvas 缩放）。</summary>
        public float Radius
        {
            get => radius;
            set
            {
                radius = Mathf.Max(2f, value);
                if (Application.isPlaying)
                {
                    Apply();
                }
            }
        }

        private void Awake()
        {
            Apply();
        }

        private void Apply()
        {
            var image = GetComponent<Image>();
            if (image == null)
            {
                return;
            }
            image.sprite = GetSprite(Mathf.RoundToInt(radius));
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
        }

        private static Sprite GetSprite(int radius)
        {
            if (Sprites.TryGetValue(radius, out var cached) && cached != null)
            {
                return cached;
            }

            // 画布边缘留 2px 直边，避免九宫格中心为 0 时拉伸抖动
            const int padding = 2;
            var size = radius * 2 + padding * 2;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = $"RoundedRect_{radius}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[size * size];
            var half = size * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // 圆角矩形的有符号距离场；0.5 - d 得到 1px 抗锯齿覆盖
                    var qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    var qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    var outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    var distance = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    var alpha = (byte)(Mathf.Clamp01(0.5f - distance) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            sprite.name = $"RoundedRect_{radius}";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            Sprites[radius] = sprite;
            return sprite;
        }
    }
}
