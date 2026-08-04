using UnityEngine;

namespace Wayfu.Lamkn
{
    // RIG DÙNG CHUNG cho MỘT loại hiệu ứng burst thuần particle (không có mảnh vỡ GameObject).
    // Thay vì mỗi "hit" spawn 1 instance mới (mỗi instance = N renderer = N draw call), ta giữ đúng
    // 1 bộ ParticleSystem sống bền ở World space; mỗi hit chỉ Emit thêm particle tại vị trí hit.
    // => Số renderer/draw call ~CỐ ĐỊNH dù có bao nhiêu hit đồng thời.
    //
    // Cách chạy: mỗi system bị TẮT emission (không tự bắn burst/rate) nhưng vẫn ở trạng thái Play
    // để mô phỏng các particle được Emit thủ công. Số lượng mỗi phát = tổng Bursts đã cấu hình sẵn.
    public sealed class SharedParticleEmitter : MonoBehaviour
    {
        private struct Sys
        {
            public ParticleSystem ps;
            public int burstTotal;      // tổng particle/1 phát, lấy theo cận trên của Bursts gốc
            public Vector3 localOffset; // vị trí local của system so với root prefab -> giữ đúng bố cục FX
        }

        private Sys[] _systems;

        // Dựng rig từ chính các ParticleSystem con của instance này.
        public void Init()
        {
            ParticleSystem[] list = GetComponentsInChildren<ParticleSystem>(true);
            _systems = new Sys[list.Length];

            for (int i = 0; i < list.Length; i++)
            {
                ParticleSystem ps = list[i];

                // Đọc Bursts TRƯỚC khi tắt emission.
                ParticleSystem.EmissionModule emission = ps.emission;
                int total = 0;
                int n = emission.burstCount;
                if (n > 0)
                {
                    var bursts = new ParticleSystem.Burst[n];
                    emission.GetBursts(bursts);
                    for (int b = 0; b < n; b++)
                        total += Mathf.CeilToInt(bursts[b].count.constantMax);
                }

                ParticleSystem.MainModule main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World; // particle đứng yên tại world pos đã emit
                main.playOnAwake = false;

                emission.enabled = false; // KHÔNG tự bắn; chỉ Emit thủ công

                ps.Clear();
                ps.Play(); // ở trạng thái Play để particle Emit thủ công được mô phỏng

                // Offset local so với root prefab (root nằm ở gốc, rotation identity) -> đúng bố cục FX gốc.
                Vector3 offset = transform.InverseTransformPoint(ps.transform.position);

                _systems[i] = new Sys { ps = ps, burstTotal = total, localOffset = offset };
            }
        }

        // Bắn một "phát" hiệu ứng tại vị trí world. Mỗi system nhả đúng số Bursts đã cấu hình,
        // shape module vẫn áp lên vị trí này (applyShapeToPosition) để giữ độ toả như gốc.
        public void EmitAt(Vector3 worldPos)
        {
            if (_systems == null) return;

            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = true };

            for (int i = 0; i < _systems.Length; i++)
            {
                Sys s = _systems[i];
                if (s.ps == null || s.burstTotal <= 0) continue;
                // Giữ đúng offset local của từng system như prefab gốc.
                ep.position = worldPos + s.localOffset;
                s.ps.Emit(ep, s.burstTotal);
            }
        }

        // Dọn sạch particle đang sống (dùng khi chuyển/đặt lại level).
        public void ClearAll()
        {
            if (_systems == null) return;
            for (int i = 0; i < _systems.Length; i++)
                if (_systems[i].ps != null) _systems[i].ps.Clear();
        }
    }
}
