using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace InfantPostureApp
{
    /// <summary>
    /// UI RawImage（Texture2D）を用いた簡易リアルタイム折れ線グラフ描画クラス
    /// GL描画や重い外部ライブラリを避け、テクスチャへのピクセル書き込みで軽量に実装
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class RealtimeGraphController : MonoBehaviour
    {
        public int GraphWidth = 512;
        public int GraphHeight = 256;
        
        public Color BackgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        public Color RollColor = Color.red;
        public Color PitchColor = Color.green;
        public Color YawColor = Color.cyan;

        private RawImage _rawImage;
        private Texture2D _graphTexture;
        private Color[] _pixels;

        // 過去の値を保持 (x: Roll, y: Pitch, z: Yaw)
        private List<Vector3> _history = new List<Vector3>();

        private void Start()
        {
            _rawImage = GetComponent<RawImage>();
            _graphTexture = new Texture2D(GraphWidth, GraphHeight, TextureFormat.RGBA32, false);
            _rawImage.texture = _graphTexture;
            _pixels = new Color[GraphWidth * GraphHeight];
            
            ClearGraph();
        }

        public void ClearGraph()
        {
            for (int i = 0; i < _pixels.Length; i++)
            {
                _pixels[i] = BackgroundColor;
            }
            _graphTexture.SetPixels(_pixels);
            _graphTexture.Apply();
            _history.Clear();
        }

        /// <summary>
        /// 最新のオイラー角をグラフに追加し、描画を更新する
        /// </summary>
        /// <param name="euler">オイラー角(-180 〜 180度)</param>
        public void UpdateGraph(Vector3 euler)
        {
            _history.Add(euler);
            if (_history.Count > GraphWidth)
            {
                _history.RemoveAt(0); // 幅を超えたら古いものを削除
            }

            Redraw();
        }

        private void Redraw()
        {
            // 背景クリア
            for (int i = 0; i < _pixels.Length; i++)
            {
                _pixels[i] = BackgroundColor;
            }

            // 履歴を描画
            for (int x = 0; x < _history.Count - 1; x++)
            {
                Vector3 current = _history[x];
                Vector3 next = _history[x + 1];

                // 値（-180〜180）をY座標（0〜GraphHeight）にマッピング
                int yRoll0 = MapToY(current.x);
                int yPitch0 = MapToY(current.y);
                int yYaw0 = MapToY(current.z);

                int yRoll1 = MapToY(next.x);
                int yPitch1 = MapToY(next.y);
                int yYaw1 = MapToY(next.z);

                DrawLine(x, yRoll0, x + 1, yRoll1, RollColor);
                DrawLine(x, yPitch0, x + 1, yPitch1, PitchColor);
                DrawLine(x, yYaw0, x + 1, yYaw1, YawColor);
            }

            _graphTexture.SetPixels(_pixels);
            _graphTexture.Apply();
        }

        private int MapToY(float angle)
        {
            // -180 〜 180 を 0 〜 GraphHeight に変換
            float normalized = (angle + 180f) / 360f;
            int y = Mathf.RoundToInt(normalized * GraphHeight);
            return Mathf.Clamp(y, 0, GraphHeight - 1);
        }

        /// <summary>
        /// Bresenhamアルゴリズムを用いたピクセル直線描画
        /// </summary>
        private void DrawLine(int x0, int y0, int x1, int y1, Color color)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = (dx > dy ? dx : -dy) / 2;

            while (true)
            {
                if (x0 >= 0 && x0 < GraphWidth && y0 >= 0 && y0 < GraphHeight)
                {
                    _pixels[y0 * GraphWidth + x0] = color;
                }

                if (x0 == x1 && y0 == y1) break;
                int e2 = err;
                if (e2 > -dx) { err -= dy; x0 += sx; }
                if (e2 < dy) { err += dx; y0 += sy; }
            }
        }
    }
}
