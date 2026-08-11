using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Quản lý slot gun. Ưu tiên dùng các slot ĐẶT SẴN TRÊN SCENE (gán vào <see cref="sceneSlots"/>
    /// hoặc tự tìm trong scene, sắp theo SlotIndex). Level điền danh sách gun cho từng slot;
    /// số slot có gun = số slot được active, các slot dư bị tắt (yêu cầu #6, #4).
    /// Nếu scene không có slot nào → fallback tự tạo 1 hàng slot mặc định.
    /// </summary>
    public class SlotManager : Singleton<SlotManager>
    {
        [Tooltip("Các slot đặt sẵn trên scene (Slot0..4). Bỏ trống sẽ tự tìm GunSlot trong scene.")]
        [SerializeField] private List<GunSlot> sceneSlots = new List<GunSlot>();
        [Tooltip("Prefab ConnectLine nối các gun CONNECT trong slot (cung Bezier, màu 2 đầu theo gun). Bỏ trống = không vẽ.")]
        [SerializeField] private ConnectLine connectLinePrefab;

        private readonly List<GunSlot> _activeSlots = new List<GunSlot>();
        private readonly List<GameObject> _fallbackCreated = new List<GameObject>();
        private readonly HashSet<Gun> _movingToLoop = new HashSet<Gun>();
        private const float ConnectCycleBarrierTimeout = 1.5f;

        // 1 nhóm gun connect (cùng ConnectGroup id, ở các slot khác nhau). Mỗi CẶP gun kề = 1 ConnectLine
        // (2 gun → 1 line; 3 gun → 2 line nối chuỗi).
        private class ConnectGroup
        {
            public List<Gun> Members = new List<Gun>();
            public List<ConnectLine> Lines = new List<ConnectLine>();
            public readonly HashSet<Gun> CycleReady = new HashSet<Gun>();
            public int CycleRelease;
        }
        private readonly List<ConnectGroup> _connectGroups = new List<ConnectGroup>();

        /// <summary>Bắn ra khi 1 gun VỪA được người chơi chọn và deploy khỏi slot (dùng cho Tutorial —
        /// vd bước "chọn gun đầu tiên" hoàn tất khi gun đó rời slot). Tham số = gun vừa deploy.</summary>
        public static event Action<Gun> GunDeployed;

        /// <summary>Gun đầu (FrontGun) của slot active ĐẦU TIÊN còn gun — "gun đầu tiên" mà Tutorial chỉ vào.
        /// null nếu chưa có slot/gun nào.</summary>
        public Gun FirstGun
        {
            get
            {
                foreach (var slot in _activeSlots)
                    if (slot != null && slot.FrontGun != null) return slot.FrontGun;
                return null;
            }
        }

        /// <summary>True once no gun remains in any active slot; queued/path guns do not count.</summary>
        public bool AreAllSlotsEmpty
        {
            get
            {
                foreach (var slot in _activeSlots) if (slot != null && slot.Count > 0) return false;
                return true;
            }
        }

        /// <summary>Add every gun owned by the current level's slots/transit state.</summary>
        public void CollectRuntimeGuns(HashSet<Gun> result)
        {
            if (result == null) return;
            foreach (var slot in _activeSlots)
            {
                if (slot?.Guns == null) continue;
                foreach (var gun in slot.Guns)
                    if (gun != null && !gun.IsDead) result.Add(gun);
            }
            foreach (var gun in _movingToLoop)
                if (gun != null && !gun.IsDead) result.Add(gun);
        }

        public void Build(LevelData level)
        {
            Clear();
            var gs = GameSettings.Instance;
            float spacing = gs != null ? gs.SlotGunSpacing : 1f;   // config chung
            var fire = GunFireConfig.FromSettings(gs);
            fire.Range = level.ResolveGunFireRange(gs);

            FillSlots(level, spacing, fire);
            BuildConnectGroups();
        }

        private void FillSlots(LevelData level, float spacing, GunFireConfig fire)
        {
            // ƯU TIÊN: script Map trên map prefab (MapController spawn) — mỗi mốc = nơi sinh GUN 0 của 1 slot.
            var map = MapController.IsActive ? MapController.Instance.CurrentMapScript : null;
            if (map != null && map.SlotCount > 0)
            {
                for (int i = 0; i < level.Slots.Count; i++)
                {
                    var sd = level.Slots[i];
                    if (sd?.Guns == null || sd.Guns.Count == 0) continue;
                    if (!map.TryGetSlotPosition(i, out var pos)) continue; // map không có mốc cho slot i
                    var slot = CreateRuntimeSlot(i, pos);
                    slot.Fill(sd.Guns, spacing, fire);
                    _activeSlots.Add(slot);
                }
                return;
            }

            // Không có Map → slot ĐẶT SẴN trên scene (bật/tắt theo số slot có gun).
            var slots = ResolveSceneSlots();
            if (slots.Count > 0)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    bool active = i < level.Slots.Count
                                  && level.Slots[i]?.Guns != null
                                  && level.Slots[i].Guns.Count > 0;
                    slots[i].gameObject.SetActive(active);
                    if (active)
                    {
                        slots[i].Fill(level.Slots[i].Guns, spacing, fire);
                        _activeSlots.Add(slots[i]);
                    }
                }
            }
            else
            {
                // Fallback cuối: chưa có Map lẫn slot scene → tạo 1 hàng slot mặc định.
                float gap = Mathf.Max(1.5f, spacing * 3f);
                for (int i = 0; i < level.Slots.Count; i++)
                {
                    var sd = level.Slots[i];
                    if (sd?.Guns == null || sd.Guns.Count == 0) continue;
                    var slot = CreateRuntimeSlot(i, new Vector3(i * gap, 0f, 0f));
                    slot.Fill(sd.Guns, spacing, fire);
                    _activeSlots.Add(slot);
                }
            }
        }

        // Tạo 1 GunSlot runtime tại vị trí cho trước (dùng cho map Map-script và fallback); tự theo dõi để Clear.
        private GunSlot CreateRuntimeSlot(int index, Vector3 pos)
        {
            var go = new GameObject("Slot" + index);
            go.transform.SetParent(transform);
            var slot = go.AddComponent<GunSlot>();
            slot.SlotIndex = index;
            slot.SetPosition(pos);
            _fallbackCreated.Add(go);
            return slot;
        }

        public void Clear()
        {
            foreach (var s in _activeSlots) if (s != null) s.Clear();
            _activeSlots.Clear();
            _movingToLoop.Clear();
            foreach (var go in _fallbackCreated) if (go != null) Destroy(go);
            _fallbackCreated.Clear();
            foreach (var grp in _connectGroups)
                foreach (var line in grp.Lines) if (line != null) Destroy(line.gameObject);
            _connectGroups.Clear();
        }

        /// <summary>
        /// Barrier cho các gun cùng ConnectGroup khi hoàn tất GoIn ở cuối một vòng path.
        /// Mỗi gun chờ tại đây cho tới khi mọi member còn sống trên path cùng tới barrier.
        /// </summary>
        public IEnumerator WaitForConnectCycle(Gun gun)
        {
            ConnectGroup group = null;
            foreach (var candidate in _connectGroups)
                if (candidate.Members.Contains(gun)) { group = candidate; break; }

            if (group == null || group.Members.Count < 2) yield break;

            int release = group.CycleRelease + 1;
            group.CycleReady.Add(gun);
            float timeoutAt = Time.time + ConnectCycleBarrierTimeout;

            bool complete = true;
            foreach (var member in group.Members)
            {
                if (member != null && !member.IsDead && member.IsOnPath && !group.CycleReady.Contains(member))
                {
                    complete = false;
                    break;
                }
            }
            if (complete)
            {
                group.CycleReady.Clear();
                group.CycleRelease = release;
            }

            while (group.CycleRelease < release)
            {
                if (gun == null || gun.IsDead) yield break;
                // A missing/stalled member must not strand every remaining gun at
                // path_0 forever. The current gun may safely continue its loop;
                // the next completed cycle will rebuild the barrier normally.
                if (Time.time >= timeoutAt)
                {
                    group.CycleReady.Remove(gun);
                    yield break;
                }
                yield return null;
            }
        }

        // Gom các gun cùng ConnectGroup id (≠0) thành nhóm; tạo LineRenderer cho nhóm ≥2 gun.
        private void BuildConnectGroups()
        {
            var byId = new Dictionary<int, ConnectGroup>();
            foreach (var slot in _activeSlots)
            {
                if (slot?.Guns == null) continue;
                foreach (var g in slot.Guns)
                {
                    if (g == null || g.Data == null || g.Data.ConnectGroup == 0) continue;
                    if (!byId.TryGetValue(g.Data.ConnectGroup, out var grp))
                    { grp = new ConnectGroup(); byId[g.Data.ConnectGroup] = grp; _connectGroups.Add(grp); }
                    grp.Members.Add(g);
                }
            }
            foreach (var grp in _connectGroups)
                if (grp.Members.Count >= 2 && connectLinePrefab != null)
                    for (int i = 0; i + 1 < grp.Members.Count; i++) // mỗi cặp gun kề = 1 line
                    {
                        var line = Instantiate(connectLinePrefab, transform);
                        line.SetTargets(grp.Members[i].transform, grp.Members[i + 1].transform);
                        line.SetColors(GlobalConfigManager.ColorOf(grp.Members[i].Color),
                                       GlobalConfigManager.ColorOf(grp.Members[i + 1].Color));
                        grp.Lines.Add(line);
                    }
        }

        private void Update() => UpdateConnectLines();

        // Đường connect tự bám target (ConnectLine.Update); ở đây BẬT/TẮT từng đoạn line[i] (nối member i↔i+1):
        // - Cả 2 trong slot, hoặc bất kỳ member nào còn ở queue trong khi nhóm đi vào loop → nối.
        // - Cả 2 đã chạy trên path CÙNG VÒNG → nối.
        // - Cả 2 cùng ở transition tại path_0/path_end → tắt dây (không vắt qua tunnel).
        // - 1 gun vừa lap qua path0 mà gun kia CHƯA (khác LapCount) → tắt đoạn đó (dây sẽ vắt hết vòng path),
        //   tới khi gun kia cũng lap về path0 (cùng vòng) thì nối tiếp.
        private void UpdateConnectLines()
        {
            foreach (var grp in _connectGroups)
                for (int i = 0; i < grp.Lines.Count; i++)
                {
                    var line = grp.Lines[i];
                    if (line == null) continue;
                    bool vis = ConnectVisible(grp.Members[i], grp.Members[i + 1]);
                    if (line.gameObject.activeSelf != vis) line.gameObject.SetActive(vis);
                }
        }

        private static bool ConnectVisible(Gun a, Gun b)
        {
            if (a == null || b == null || a.IsDead || b.IsDead) return false;
            bool aSlot = a.Slot != null, bSlot = b.Slot != null;
            if (aSlot && bSlot) return true;                        // cả 2 còn trong slot → nối
            // Keep the wire while a CONNECT group is split between the waiting
            // queue and the loop.  The gate admits members one at a time, so this
            // mixed state is expected rather than a reason to disconnect.
            if ((a.IsQueued || a.IsOnPath) && (b.IsQueued || b.IsOnPath)
                && (a.IsQueued || b.IsQueued)) return true;
            if (!aSlot && !bSlot && a.IsOnPath && b.IsOnPath)
            {
                // PathEntryAnimating covers the endpoint transitions: entering/leaving
                // path_0 and GoIn at path_end.  When both members are there, hiding
                // the line prevents it from being drawn through the tunnel.
                if (a.PathEntryAnimating && b.PathEntryAnimating) return false;
                return a.LapCount == b.LapCount;                    // cả 2 trên path → nối khi CÙNG vòng
            }
            return false;                                           // hai gun đang ở trạng thái khác nhau → tạm tắt
        }

        public void OnGunClicked(Gun gun)
        {
            if (gun == null) return;

            // Gun CONNECT: chỉ deploy khi CẢ NHÓM đang ở index 0, và cả nhóm vào path cùng lúc.
            if (gun.Data != null && gun.Data.ConnectGroup != 0) { TryDeployConnectGroup(gun.Data.ConnectGroup); return; }

            var slot = gun.Slot;
            if (slot == null || slot.FrontGun != gun) return;              // chỉ gun đầu slot
            if (PathManager.Instance == null) return;
            if (!PathManager.Instance.CanAcceptCount(_movingToLoop.Count + 1))
            {
                // Chỉ báo click khi gun chưa thể rời slot vì path đã đầy.
                gun.TryPlayClickThen(null);
                return;
            }

            int slotIndex = slot.SlotIndex;
            if (gun == null || gun.IsDead) return;
            slot.RemoveFront();
            SendGunToLoop(slotIndex, gun);
            GunDeployed?.Invoke(gun); // Tutorial nghe: gun vừa được chọn & rời slot
            GameController.Instance?.OnBoardChanged();
        }

        // Gun connect hết đạn: chỉ HỦY CẢ NHÓM khi MỌI member đã hết đạn (lúc đó path giảm count đồng loạt).
        // Chưa đủ → gun này đứng chờ trên path (vẫn chiếm chỗ) tới khi member cuối bắn hết.
        public void OnConnectGunEmptied(Gun gun)
        {
            ConnectGroup grp = null;
            foreach (var g in _connectGroups) if (g.Members.Contains(gun)) { grp = g; break; }
            if (grp == null) { gun.Kill(); return; } // không thuộc nhóm → hủy như gun thường

            foreach (var m in grp.Members) if (m != null && m.HasBullets) return; // còn member chưa hết đạn → chờ
            foreach (var m in grp.Members) if (m != null) m.Kill();               // cả nhóm hết đạn → hủy đồng loạt
            foreach (var line in grp.Lines) if (line != null) Destroy(line.gameObject); // dọn dây (khỏi bám gun tái dùng)
            grp.Lines.Clear();
        }

        // Deploy CẢ NHÓM connect: mọi member phải đang ở index 0 (front) slot; path phải đủ chỗ cho cả nhóm.
        // Vượt sức chứa → không move; nếu bế tắc (gun trên path không bắn được) → thua.
        private void TryDeployConnectGroup(int id)
        {
            var members = new List<Gun>();
            var memberIndexes = new Dictionary<Gun, int>();
            foreach (var slot in _activeSlots)
            {
                if (slot?.Guns == null) continue;
                for (int i = 0; i < slot.Guns.Count; i++)
                {
                    var g = slot.Guns[i];
                    if (g == null || g.Data == null || g.Data.ConnectGroup != id) continue;
                    members.Add(g);
                    memberIndexes[g] = i;
                }
            }
            if (members.Count == 0) return;

            // Trong mỗi slot, cả phần của group phải nằm liền nhau từ index 0.
            var memberSet = new HashSet<Gun>(members);
            var removeCounts = new Dictionary<GunSlot, int>();
            foreach (var slot in _activeSlots)
            {
                if (slot?.Guns == null) continue;

                int countInSlot = 0;
                foreach (var g in slot.Guns)
                    if (g != null && memberSet.Contains(g)) countInSlot++;
                if (countInSlot == 0) continue;

                // Members in the same slot may deploy together when they occupy the
                // entire contiguous prefix (indexes 0..N-1). A non-member in front
                // still blocks the connected group as usual.
                for (int i = 0; i < countInSlot; i++)
                    if (!memberSet.Contains(slot.Guns[i])) return;

                removeCounts[slot] = countInSlot;
            }

            // PathManager serves _queue as FIFO.  Enqueue the highest slot first so
            // connected guns leave in the same, deterministic high-to-low slot order.
            members.Sort((a, b) =>
            {
                int slotOrder = b.Slot.SlotIndex.CompareTo(a.Slot.SlotIndex);
                return slotOrder != 0 ? slotOrder : memberIndexes[a].CompareTo(memberIndexes[b]);
            });

            var pm = PathManager.Instance;
            if (pm == null || !pm.CanAcceptCount(_movingToLoop.Count + members.Count))
            {
                GameController.Instance?.NotifyConnectStuck(); // vượt sức chứa → bế tắc thì thua
                return;
            }

            // Remove every member before it is visible to PathManager, then queue the
            // complete ordered group in one operation.  SimulateQueueCrowd therefore
            // sees only the final ranks, never a transient single-gun queue.
            foreach (var pair in removeCounts)
                for (int i = 0; i < pair.Value; i++) pair.Key.RemoveFront();
            pm.RequestDeployGroup(members);
            GameController.Instance?.OnBoardChanged();
        }

        // Map prefab có thể định nghĩa route riêng cho từng slot. Sau khi đi hết route, gun mới
        // được đưa vào queue/path loop bình thường; map không có route giữ nguyên hành vi cũ.
        private void SendGunToLoop(int slotIndex, Gun gun)
        {
            // Bỏ Slot Move Paths: gun không đi theo route riêng của slot nữa mà ĐI THẲNG ra cửa PathManager.
            // RequestDeploy tự MoveTo gun tới điểm chờ NGOÀI cửa tunnel rồi mới điều phối transit vào path.
            // (Code Map.slotMovePaths/TryMoveGunToLoop vẫn giữ, chỉ không gọi tới — phòng khi cần dùng lại.)
            PathManager.Instance?.RequestDeploy(gun);
        }

        private List<GunSlot> ResolveSceneSlots()
        {
            var list = new List<GunSlot>();
            if (sceneSlots != null)
                foreach (var s in sceneSlots) if (s != null) list.Add(s);

            if (list.Count == 0)
                list.AddRange(FindObjectsOfType<GunSlot>(true));

            list.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
            return list;
        }
    }
}
