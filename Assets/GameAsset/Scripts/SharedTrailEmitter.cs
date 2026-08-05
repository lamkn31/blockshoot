using UnityEngine;

namespace Wayfu.Lamkn
{
    // RIG DÙNG CHUNG cho FX BÁM VẬT BAY (vd đạn nước Projectiles_water bám viên đạn).
    // Giữ ĐÚNG 1 bộ ParticleSystem sống bền ở World space; mọi viên đạn Emit vào đây -> draw call ~CỐ ĐỊNH
    // dù bao nhiêu đạn bay cùng lúc (thay vì mỗi đạn 1 instance = N renderer = N draw call).
    //
    // Bám vật bay: particle World-space sau khi emit ĐỨNG YÊN, nên phải cấp vận tốc = vận tốc đạn qua
    // EmitParams.velocity thì nó mới "bay theo". Để KHÔNG mất cảm giác toả nước của FX gốc, mỗi particle
    // được cộng thêm 1 vector toả ngẫu nhiên có độ lớn = startSpeed gốc của system (droplets/bubbles = 3):
    //   velocity particle = vận tốc đạn (bám đạn) + toả ngẫu nhiên (giữ look splash gốc).
    // startSpeed đạn >> 3 nên chuyển động chính là bám đạn, phần toả chỉ tạo bề dày vệt.
    public sealed class SharedTrailEmitter : MonoBehaviour
    {
        private struct Sys
        {
            public ParticleSystem ps;
            public float perDistance;   // rateOverDistance gốc (particle / 1 đơn vị quãng đường)
            public int burstTotal;      // tổng particle của các Bursts (bắn 1 phát lúc phóng)
            public float startSpeed;    // startSpeed gốc -> độ lớn vector toả ngẫu nhiên (giữ look gốc)
            public Vector3 localOffset; // vị trí local so với root -> giữ đúng bố cục FX gốc
        }

        private Sys[] _systems;

        // Dựng rig từ chính các ParticleSystem con của instance này (chuyển World space, tắt auto-emit).
        public void Init()
        {
            ParticleSystem[] list = GetComponentsInChildren<ParticleSystem>(true);
            _systems = new Sys[list.Length];

            for (int i = 0; i < list.Length; i++)
            {
                ParticleSystem ps = list[i];

                // Đọc rateOverDistance + Bursts TRƯỚC khi tắt emission (giữ đúng thông số gốc để tái tạo).
                ParticleSystem.EmissionModule emission = ps.emission;
                float perDist = emission.rateOverDistance.constantMax;

                int burst = 0;
                int n = emission.burstCount;
                if (n > 0)
                {
                    var bursts = new ParticleSystem.Burst[n];
                    emission.GetBursts(bursts);
                    for (int b = 0; b < n; b++)
                        burst += Mathf.CeilToInt(bursts[b].count.constantMax);
                }

                ParticleSystem.MainModule main = ps.main;
                float startSpeed = main.startSpeed.constantMax;
                main.simulationSpace = ParticleSystemSimulationSpace.World; // particle giữ nguyên tại world pos đã emit
                main.playOnAwake = false;

                emission.enabled = false; // KHÔNG tự bắn; chỉ Emit thủ công
                ps.Clear();
                ps.Play(); // giữ Play để particle Emit thủ công được mô phỏng

                Vector3 offset = transform.InverseTransformPoint(ps.transform.position);
                _systems[i] = new Sys
                {
                    ps = ps, perDistance = perDist, burstTotal = burst,
                    startSpeed = startSpeed, localOffset = offset
                };
            }
        }

        // Bắn các system BURST 1 phát (head, core) tại vị trí phóng, bay theo vận tốc phóng. Gọi 1 lần lúc rời nòng.
        public void EmitBurst(Vector3 worldPos, Vector3 velocity)
        {
            if (_systems == null) return;

            for (int i = 0; i < _systems.Length; i++)
            {
                Sys s = _systems[i];
                if (s.ps == null || s.burstTotal <= 0) continue;
                EmitParticles(s, worldPos, velocity, s.burstTotal);
            }
        }

        // Rải các system RATE-OVER-DISTANCE (droplets, bubbles) cho đoạn bay 'fromPos'->'toPos'.
        // Số particle mỗi system = ceil(rateOverDistance * distance) -> đúng mật độ rate-over-distance gốc.
        // QUAN TRỌNG: particle được RẢI ĐỀU DỌC đoạn, KHÔNG dồn hết vào điểm cuối. ParticleSystem gốc chạy
        // rate-over-distance thì sinh hạt liên tục theo transform di chuyển; nếu ta Emit tất cả tại 1 điểm
        // (mỗi bước trailStepDistance) thì vệt vỡ thành từng CỤM cách đều -> hạt cườm/đứt đoạn. Lerp vị trí
        // theo k làm vệt liền mạch như bản gốc, độc lập với việc trailStepDistance đặt thô hay mịn.
        public void EmitTrail(Vector3 fromPos, Vector3 toPos, Vector3 velocity)
        {
            if (_systems == null) return;
            float distance = Vector3.Distance(fromPos, toPos);
            if (distance <= 0f) return;

            for (int i = 0; i < _systems.Length; i++)
            {
                Sys s = _systems[i];
                if (s.ps == null || s.perDistance <= 0f) continue;
                int count = Mathf.CeilToInt(s.perDistance * distance);
                if (count <= 0) continue;
                EmitAlongSegment(s, fromPos, toPos, velocity, count);
            }
        }

        // Rải 'count' particle của 1 system ĐỀU dọc đoạn fromPos->toPos (mẫu tại giữa mỗi khoảng con để
        // không dồn về 2 đầu). Mỗi hạt = vận tốc đạn + toả ngẫu nhiên theo startSpeed (giữ vẻ toả nước gốc).
        private void EmitAlongSegment(Sys s, Vector3 fromPos, Vector3 toPos, Vector3 velocity, int count)
        {
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            for (int k = 0; k < count; k++)
            {
                float f = (k + 0.5f) / count;
                ep.position = Vector3.Lerp(fromPos, toPos, f) + s.localOffset;
                ep.velocity = s.startSpeed > 0.0001f
                    ? velocity + Random.onUnitSphere * s.startSpeed
                    : velocity;
                s.ps.Emit(ep, 1);
            }
        }

        // Emit 'count' particle của 1 system tại worldPos: mỗi particle = vận tốc đạn + toả ngẫu nhiên theo startSpeed.
        // Emit từng particle (Emit 1) để mỗi hạt có hướng toả riêng -> giữ được vẻ toả nước của FX gốc.
        private void EmitParticles(Sys s, Vector3 worldPos, Vector3 velocity, int count)
        {
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            Vector3 pos = worldPos + s.localOffset;

            if (s.startSpeed <= 0.0001f)
            {
                // Không toả (head/core): 1 lần Emit cả cụm cho rẻ.
                ep.position = pos;
                ep.velocity = velocity;
                s.ps.Emit(ep, count);
                return;
            }

            for (int k = 0; k < count; k++)
            {
                ep.position = pos;
                ep.velocity = velocity + Random.onUnitSphere * s.startSpeed;
                s.ps.Emit(ep, 1);
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
