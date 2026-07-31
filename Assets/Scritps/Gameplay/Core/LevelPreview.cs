using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Vẽ gizmo preview của 1 LevelData trong Scene: path loop, các grid block (4 góc + cell), khung camera.
    /// Chỉ chạy trong Editor qua OnDrawGizmos.
    /// </summary>
    public class LevelPreview : MonoBehaviour
    {
        public LevelData level;
        public bool drawPath = true;
        public bool drawBlocks = true;
        public bool drawCameraFrame = true;

        private void OnDrawGizmos()
        {
            if (level == null) return;

            if (drawCameraFrame && level.CameraOrthoSize > 0f)
            {
                float halfH = level.CameraOrthoSize;
                float halfW = level.ScreenAspect.y > 0f ? halfH * level.ScreenAspect.x / level.ScreenAspect.y : halfH;
                Vector3 c = level.CameraCenter;
                Gizmos.color = new Color(1f, 0.55f, 0.1f);
                Vector3 a = c + new Vector3(-halfW, 0f, -halfH);
                Vector3 b = c + new Vector3(halfW, 0f, -halfH);
                Vector3 d = c + new Vector3(halfW, 0f, halfH);
                Vector3 e = c + new Vector3(-halfW, 0f, halfH);
                Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, d); Gizmos.DrawLine(d, e); Gizmos.DrawLine(e, a);
            }

            if (drawPath && level.PathWaypoints != null && level.PathWaypoints.Count >= 2)
            {
                // Vẽ ĐÚNG đường bo góc như runtime + 2 mép theo PathWidth.
                var s = RoundedPolylinePath.BuildSamples(level.PathWaypoints, level.IsClosed,
                                                         level.CornerRadius, 8, level.PathStyle);
                if (s != null)
                {
                    float half = Mathf.Max(0f, GameSettings.Instance != null ? GameSettings.Instance.PathWidth : 1.5f) * 0.5f;
                    for (int i = 1; i < s.Length; i++)
                    {
                        Gizmos.color = Color.cyan;
                        Gizmos.DrawLine(s[i - 1], s[i]);
                        if (half <= 0f) continue;
                        Vector3 dir = s[i] - s[i - 1]; dir.y = 0f;
                        if (dir.sqrMagnitude < 1e-8f) continue;
                        Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * half;
                        Gizmos.color = new Color(0f, 0.8f, 1f);
                        Gizmos.DrawLine(s[i - 1] + side, s[i] + side);
                        Gizmos.DrawLine(s[i - 1] - side, s[i] - side);
                    }
                }
                Gizmos.color = Color.red;
                foreach (var w in level.PathWaypoints) Gizmos.DrawWireSphere(w, 0.15f);
            }

            if (drawBlocks && level.Grids != null)
            {
                float stackSpacing = GameSettings.Instance != null ? GameSettings.Instance.BlockStackSpacing : 0.5f;
                foreach (var grid in level.Grids)
                {
                    if (grid == null) continue;

                    // Viền vòng cung: 2 cạnh bên + cung trong/ngoài.
                    Gizmos.color = new Color(0.6f, 0.6f, 0.6f);
                    int last = Mathf.Max(0, grid.Rows - 1);
                    Gizmos.DrawLine(grid.CellPos(0, 0), grid.CellPos(last, grid.ElementsInRow(last) - 1));       // cạnh phải
                    Gizmos.DrawLine(grid.CellPos(0, grid.ElementsInRow(0) - 1), grid.CellPos(last, 0));         // cạnh trái

                    for (int r = 0; r < grid.Rows; r++)
                    {
                        int count = grid.ElementsInRow(r);
                        for (int e = 0; e < count; e++)
                        {
                            var cell = grid.GetCell(r, e);
                            if (cell == null || cell.BlockStackCt <= 0) continue;
                            Vector3 pos = grid.CellPos(r, e);
                            Gizmos.color = GlobalConfigManager.ColorOf(cell.Color);
                            int stack = Mathf.Max(1, cell.BlockStackCt);
                            for (int j = 0; j < stack; j++)
                                Gizmos.DrawWireCube(pos + Vector3.up * stackSpacing * j, Vector3.one * 0.45f);
                            // Hướng (rotate) của cell. Rect/Spline: do hình dạng grid quyết định → tính
                            // thẳng từ grid. Arc: đọc góc riêng đã lưu trong data.
                            Gizmos.color = Color.white;
                            Vector3 dirV = grid.CellAngleFromShape
                                ? Quaternion.Euler(0f, grid.DefaultCellAngle(r, e), 0f) * Vector3.forward
                                : Quaternion.Euler(0f, cell.SpawnerDirectionAngleZ + grid.CellDirectionOffset, 0f) * Vector3.forward;
                            Gizmos.DrawLine(pos, pos + dirV * 0.55f);
                        }
                    }
                }
            }
            // Slot vẽ bởi chính GunSlot trên scene.
        }
    }
}
