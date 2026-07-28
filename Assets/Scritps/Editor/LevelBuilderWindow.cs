using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Level Tool 3 khung: trái = files/GameSettings/list level; giữa = màn hình (chiếu theo camera,
    /// zoom/pan, kéo waypoint & 4 góc grid); phải = data level. Block xếp thành GRID (4 góc + cong,
    /// nhiều grid/level). Config gun/path ở GameSettings. Mở qua Wayfu ▸ Level Tool.
    /// </summary>
    public class LevelBuilderWindow : EditorWindow
    {
        private enum ListOp { None, MoveUp, MoveDown, Duplicate, Delete }

        private const string FolderPrefKey = "Wayfu.LevelTool.Folder";
        private const float Pad = 24f;
        private static readonly GUILayoutOption BtnW = GUILayout.Width(24);

        private LevelData _target;
        private SerializedObject _so;
        private SerializedObject _gsSO;
        private SerializedObject _listSO;
        private ReorderableList _levelReorder;       // list level gộp: kéo thả sort + Play/Dup/Delete
        private LevelData _pendingPlay, _pendingDelete; // thao tác ghi trong lúc vẽ dòng, xử lý sau vòng vẽ
        private int _pendingDup = -1;

        private string _levelsFolder = "Assets";
        private readonly List<LevelData> _levels = new List<LevelData>();
        private Gun _defaultGunPrefab;
        private Block _defaultBlockPrefab;

        private Vector2 _leftScroll, _rightScroll;
        private bool _showPath = true, _showSlots = true, _showBlocks = true, _showFrame = true, _showRange = true, _showDir = true;
        // Thanh cạnh: xanh = cạnh grid bắn được (ShootableEdges), đỏ = mặt chặn.
        private bool _showEdges = true;
        // Mũi tên các HƯỚNG cell có thể dồn/move (dọc; + ngang/chéo nếu Collapse2D).
        private bool _showMove = false;
        // Ghost hàng đợi của cell Spawner — tắt cho đỡ rối khi không sửa spawner.
        private bool _showQueue = true;
        // Obstacle: vẽ footprint + handle trên map.
        private bool _showObstacles = true;
        // Obstacle đang chọn để hiện thông số ở panel phải (-1 = không).
        private int _selObstacle = -1;

        // Bề rộng 2 panel — kéo được.
        private float _leftW = 250f, _rightW = 360f;
        private int _dragSplitter; // 0 none, 1 = trái, 2 = phải

        private Camera _previewCamera;

        // View transform.
        private Rect _viewContent;
        private Vector2 _viewMin;
        private float _viewScale = 1f;
        private bool _camMode;
        private float _zoom = 1f;
        private Vector2 _pan = Vector2.zero;
        private bool _panning;

        // Kéo.
        private int _dragWaypoint = -1;
        private int _dragGrid = -1, _dragHandle = -1; // handle: 0=Center, 1=Left tip, 2=Right tip
        private Vector3 _dragGridStart;               // Center lúc bắt đầu kéo — để giữ Shift khóa trục
        private bool _recomputeGridSides;             // đặt khi tạo/di chuyển grid → cuối OnGUI tự tính lại Side
        private int _dragCellGrid = -1, _dragCellIndex = -1; // xoay hướng 1 cell
        private Vector3 _dragCellCenter;
        private int _dragSplineGrid = -1, _dragSplineWp = -1; // kéo waypoint của Spline grid
        // Obstacle: kéo trên map. handle 0 = di chuyển (tâm), 1 = xoay (Y), 2 = scale (góc).
        private int _dragObstacle = -1, _dragObHandle = -1;
        private Vector3 _dragObStart;                 // Pos lúc bắt đầu kéo — để Shift khóa trục

        // Generate grid.
        private TypeColor _genColor = TypeColor.Red;
        private int _genStack = 3;
        // Màu gun mặc định khi sinh gun vào slot (chọn số slot / "+ Gun mọi slot").
        private TypeColor _slotGenColor = TypeColor.Red;
        private BlockGridShape _genShape = BlockGridShape.Rect; // loại grid khi bấm "+ Grid" (mặc định Rect; Arc đã bỏ)
        // Các shape tool cho phép chọn (Arc bị loại) — index 0 = Rect, 1 = Spline.
        private static readonly string[] ToolShapeNames = { "Rect", "Spline" };

        // Màu tô cell bằng click trong khung giữa. None → click để CHỌN xem/sửa thông số.
        private TypeColor _paintColor = TypeColor.None;

        // Erase: click/rê chuột vào block là XOÁ ngay (stack = 0 → lỗ), khỏi phải chọn rồi bấm Delete.
        // Loại trừ lẫn nhau với tô màu (bật Erase thì _paintColor về None).
        private bool _eraseMode;

        // Shoot paint: click/rê vào cell là đặt cờ Shootable = _shootValue. Loại trừ với tô màu & erase.
        // _shootValue = false → tô "KHÔNG bắn được" (gạch X); true → tô lại "bắn được".
        private bool _shootMode;
        private bool _shootValue = false;

        // Brush tô màu: giữ chuột rê qua nhiều cell thay vì chấm từng ô.
        private bool _painting;
        private Vector2 _lastPaintPos;

        // Live sync: sửa grid/path lúc đang Play → dựng lại level ngay để thấy vị trí thật.
        private bool _liveSync = true;
        private bool _liveDirty;

        // Grid đang ACTIVE để sửa: -1 = mọi grid; >=0 = CHỈ grid này nhận click/tô/xóa/quét (các grid chồng
        // nhau khác bị khóa, chỉ hiện mờ). Tránh sửa nhầm cell của grid đè lên nhau.
        private int _activeGrid = -1;

        // Đối tượng đang chọn trong khung giữa (chỉ khi Paint = None).
        private readonly List<(int grid, int cell)> _selCells = new List<(int, int)>();
        private int _selSlot = -1, _selGun = -1;    // gun (chọn 1)
        // Cell trong hàng đợi Spawner đang chọn (chỉ 1). qi < 0 = không chọn gì.
        private (int grid, int cell, int qi) _selQueue = (-1, -1, -1);

        // Giá trị áp dụng CHUNG cho nhiều cell đang chọn.
        private TypeColor _multiColor = TypeColor.Red;
        private BlockCellType _multiType = BlockCellType.Normal;
        private bool _multiShootable = true;
        private bool _multiIced;
        private int _multiIceThreshold = 10;
        private float _multiLineDir; // hướng chung đặt cho SpawnerLine đang chọn (độ quanh Y)
        // Thao tác HÀNG LOẠT cho queue của spawner: thêm N mục, đặt màu/stack cả queue.
        private int _queueAddCount = 3;
        private TypeColor _queueBulkColor = TypeColor.Red;
        private int _queueBulkStack = 3;
        private int _multiQueueCount = 3;
        private int _multiStack = 3;

        // Đường bo góc của path, dựng 1 lần mỗi lần vẽ (dùng chung với runtime qua BuildSamples).
        private Vector3[] _pathSamples;

        // Quét chọn (marquee) + vùng click của cell/gun, gom lại trong lúc vẽ.
        private Vector2 _marqueeStart, _marqueeEnd;
        private bool _marqueeOn;
        private readonly List<(Rect rect, int grid, int flat)> _hitCells = new List<(Rect, int, int)>();
        private readonly List<(Rect rect, int slot, int gun)> _hitGuns = new List<(Rect, int, int)>();
        // Ô "+" ghost sau đuôi mỗi slot: click (khi đang chọn Paint Color) để thêm gun màu đó — giống queue cell.
        private readonly List<(Rect rect, int slot)> _hitGunAdd = new List<(Rect, int)>();
        // Ghost hàng đợi của cell Spawner. qi = index trong Queue; qi == Queue.Count là ô "+" (thêm mới).
        private readonly List<(Rect rect, int grid, int flat, int qi)> _hitQueue = new List<(Rect, int, int, int)>();

        // Spline grid: ẩn/hiện đường tim spline + waypoint (mặc định ẨN cho đỡ rối). Bật ở mục SPLINE.
        private bool _showSplineLine;
        private bool _showSplineWaypoints;

        // Waypoint path đang quét chọn (để căn thẳng dọc/ngang) + vùng click của waypoint gom khi vẽ.
        private readonly List<int> _selWaypoints = new List<int>();
        private readonly List<(Rect rect, int index)> _hitWaypoints = new List<(Rect, int)>();

        private bool IsCellSelected(int gi, int flat) => _selCells.Contains((gi, flat));

        private void SelectGun(int si, int gi) { _selSlot = si; _selGun = gi; _selCells.Clear(); ClearQueueSel(); }

        private void ClearQueueSel() => _selQueue = (-1, -1, -1);
        private bool IsQueueSelected(int gi, int flat, int qi) => _selQueue == (gi, flat, qi);

        private void SelectQueue(int gi, int flat, int qi)
        {
            _selQueue = (gi, flat, qi);
            _selCells.Clear(); _selSlot = -1; _selGun = -1;
        }

        // Ctrl/Cmd = chọn thêm (bấm lại thì bỏ chọn).
        private void ToggleCell(int gi, int flat, bool additive)
        {
            _selSlot = -1; _selGun = -1; ClearQueueSel();
            var key = (gi, flat);
            if (!additive) { _selCells.Clear(); _selCells.Add(key); return; }
            if (!_selCells.Remove(key)) _selCells.Add(key);
        }

        // Foldout: thu gọn từng nhóm cho panel phải ngắn lại (chỉ GRIDS mở sẵn).
        private bool _foldMeta, _foldPath, _foldObstacles = true, _foldWaypoints, _foldSlots, _foldGrids = true;
        private readonly List<bool> _foldGrid = new List<bool>();   // theo từng grid
        private readonly List<bool> _foldSlot = new List<bool>();   // theo từng slot

        // Foldout theo index, tự nới list. Mặc định đóng để panel gọn.
        private static bool FoldAt(List<bool> flags, int i, string label)
        {
            while (flags.Count <= i) flags.Add(false);
            flags[i] = EditorGUILayout.Foldout(flags[i], label, true);
            return flags[i];
        }

        [MenuItem("Wayfu/Level Tool")]
        public static void Open()
        {
            var w = GetWindow<LevelBuilderWindow>("Level Tool");
            w.minSize = new Vector2(960, 520);
        }

        [MenuItem("CONTEXT/LevelData/Open in Level Tool")]
        private static void OpenFromContext(MenuCommand cmd)
        {
            var w = GetWindow<LevelBuilderWindow>("Level Tool");
            w.Select(cmd.context as LevelData);
        }

        private void OnEnable()
        {
            _levelsFolder = EditorPrefs.GetString(FolderPrefKey, "Assets");
            _genShape = BlockGridShape.Rect; // grid mới mặc định Rect (ép lại phòng khi window giữ state cũ)
            EnsurePreviewCamera();
            RefreshLevels();
        }

        // WYSIWYG: luôn chiếu bằng camera thật. Nếu chưa gán (null vì domain-reload/scene chưa sẵn lúc
        // OnEnable) thì resolve lại: ưu tiên Camera.main, fallback camera bất kỳ trong scene.
        private void EnsurePreviewCamera()
        {
            if (_previewCamera != null) return;
            _previewCamera = Camera.main;
            if (_previewCamera == null)
            {
                var cams = Object.FindObjectsOfType<Camera>();
                if (cams.Length > 0) _previewCamera = cams[0];
            }
        }

        private void OnGUI()
        {
            EnsurePreviewCamera();
            DrawToolbar();

            if (_target != null)
            {
                if (_so == null || _so.targetObject != _target) _so = new SerializedObject(_target);
                _so.Update();
            }

            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawSplitter(1, +1f, ref _leftW);
            DrawCenterPanel();
            DrawSplitter(2, -1f, ref _rightW);
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();

            if (_target != null && _so != null && _so.ApplyModifiedProperties()) _liveDirty = true;

            // Sau khi serialized đã áp vào object: tính lại Left/Right của grid theo path (nếu vừa
            // tạo/di chuyển). Làm ở đây để đọc geometry THẬT của grid (CellPos) sau thay đổi.
            if (_recomputeGridSides) { RecomputeGridSides(); _recomputeGridSides = false; }

            TryLiveRebuild();
        }

        private bool IsDragging => _dragGrid >= 0 || _dragWaypoint >= 0 || _dragCellGrid >= 0 || _dragObstacle >= 0 || _painting || _panning;

        /// <summary>
        /// Sửa grid/path/cell lúc đang Play → dựng lại level để thấy ngay vị trí THẬT trong runtime,
        /// khỏi phải thoát Play rồi vào lại. Đợi thả chuột mới dựng: kéo handle sinh thay đổi mỗi frame,
        /// dựng lại cả board 60 lần/giây thì giật và reset gameplay liên tục.
        /// </summary>
        private void TryLiveRebuild()
        {
            if (!_liveDirty || IsDragging) return;
            _liveDirty = false;
            if (!_liveSync || !Application.isPlaying || _target == null) return;
            // Không dùng LevelController.Instance: Singleton.Instance log error khi scene chưa có nó.
            var lc = Object.FindObjectOfType<LevelController>();
            if (lc != null && lc.Level == _target) lc.Build();
        }

        // Thanh kéo dọc để resize panel. sign = chiều tăng width theo delta.x của chuột.
        private void DrawSplitter(int id, float sign, ref float width)
        {
            var rect = GUILayoutUtility.GetRect(5f, 5f, GUILayout.Width(5f), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.35f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition)) { _dragSplitter = id; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (_dragSplitter == id)
                    {
                        width = Mathf.Clamp(width + sign * e.delta.x, 170f, position.width - 430f);
                        e.Use(); Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (_dragSplitter == id) { _dragSplitter = 0; e.Use(); }
                    break;
            }
        }

        #region Toolbar

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("PixelShoot Level Tool", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            _showPath = GUILayout.Toggle(_showPath, "Path", EditorStyles.toolbarButton);
            _showSlots = GUILayout.Toggle(_showSlots, "Slots", EditorStyles.toolbarButton);
            _showBlocks = GUILayout.Toggle(_showBlocks, "Grids", EditorStyles.toolbarButton);
            _showDir = GUILayout.Toggle(_showDir, "Dir", EditorStyles.toolbarButton);
            _showEdges = GUILayout.Toggle(_showEdges, new GUIContent("Edges",
                "Vẽ thanh quanh grid cho biết mặt nào gun bắn được (Shootable Edges). Xanh = bắn được, " +
                "đỏ = mặt chặn. Grid mặc định (None) chỉ hiện xanh ở mặt Front (hàng 0)."), EditorStyles.toolbarButton);
            _showMove = GUILayout.Toggle(_showMove, new GUIContent("Move",
                "Mũi tên xanh cyan = các HƯỚNG cell có thể dồn/move khi collapse: DỌC (về hàng 0). " +
                "Bật Collapse2D thì thêm NGANG (về index 0) và CHÉO."), EditorStyles.toolbarButton);
            _showQueue = GUILayout.Toggle(_showQueue, new GUIContent("Queue",
                "Hiện các cell hàng đợi nằm SAU cell Spawner (ô mờ + ô \"+\"). Tắt cho đỡ rối khi không sửa spawner — " +
                "tắt rồi thì click cũng không còn trúng chúng nữa."), EditorStyles.toolbarButton);
            _showObstacles = GUILayout.Toggle(_showObstacles, new GUIContent("Obstacles",
                "Vẽ footprint + handle của obstacle trên map (di chuyển tâm hồng · xoay xanh lá · scale xanh dương)."),
                EditorStyles.toolbarButton);
            _showFrame = GUILayout.Toggle(_showFrame, "Cam", EditorStyles.toolbarButton);
            _showRange = GUILayout.Toggle(_showRange, "Range", EditorStyles.toolbarButton);
            _liveSync = GUILayout.Toggle(_liveSync, new GUIContent("Live",
                "Đang Play mà sửa grid/path/cell thì dựng lại level ngay trong runtime (thả chuột mới dựng). " +
                "Chỉ chạy khi LevelController đang gán ĐÚNG level đang mở."), EditorStyles.toolbarButton);
            if (GUILayout.Button("Fit", EditorStyles.toolbarButton)) { _zoom = 1f; _pan = Vector2.zero; Repaint(); }
            if (GUILayout.Button("Add Preview", EditorStyles.toolbarButton) && _target != null) AddPreviewToScene();
            if (GUILayout.Button("Save", EditorStyles.toolbarButton))
            {
                // Áp thay đổi đang chờ rồi tính lại Left/Right của MỌI grid trước khi ghi xuống đĩa.
                if (_target != null && _so != null) _so.ApplyModifiedProperties();
                RecomputeGridSides();
                AssetDatabase.SaveAssets();
            }
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Left panel

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical("box", GUILayout.Width(_leftW));
            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

            EditorGUILayout.LabelField("Files", EditorStyles.boldLabel);
            var folderObj = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_levelsFolder);
            var newFolder = (DefaultAsset)EditorGUILayout.ObjectField("Levels Folder", folderObj, typeof(DefaultAsset), false);
            if (newFolder != folderObj)
            {
                string p = AssetDatabase.GetAssetPath(newFolder);
                if (!string.IsNullOrEmpty(p) && AssetDatabase.IsValidFolder(p)) { _levelsFolder = p; EditorPrefs.SetString(FolderPrefKey, p); RefreshLevels(); }
            }
            _defaultGunPrefab = (Gun)EditorGUILayout.ObjectField("Def. Gun", _defaultGunPrefab, typeof(Gun), false);
            _defaultBlockPrefab = (Block)EditorGUILayout.ObjectField("Def. Block", _defaultBlockPrefab, typeof(Block), false);
            _previewCamera = (Camera)EditorGUILayout.ObjectField("Scene Camera", _previewCamera, typeof(Camera), true);

            EditorGUILayout.Space(4);
            DrawGameSettings();

            EditorGUILayout.Space(4);
            DrawLevelList();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reload")) RefreshLevels();
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Create Scene Slots (0-4)")) CreateSceneSlots();
            if (GUILayout.Button("Create MapController")) CreateMapController();
            if (GUILayout.Button("Create IceController")) CreateIceController();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // Thứ tự chơi: LevelController lấy level theo index trong asset này (UserProgress.currentLevelIndex).
        // LIST DUY NHẤT quản lý level: kéo thả để sắp xếp; mỗi dòng có Play / Duplicate / Delete.
        private void DrawLevelList()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("LEVEL LIST — kéo thả để sắp xếp", EditorStyles.boldLabel);

            var list = LevelList.Instance;
            EditorGUILayout.ObjectField("Asset", list, typeof(LevelList), false);
            if (list == null)
            {
                EditorGUILayout.HelpBox("Chưa có LevelList. Tạo asset để LevelController load level theo index.",
                    MessageType.Info);
                if (GUILayout.Button("Create LevelList")) CreateLevelList();
                EditorGUILayout.EndVertical();
                return;
            }

            if (_listSO == null || _listSO.targetObject != list) { _listSO = new SerializedObject(list); _levelReorder = null; }
            _listSO.Update();
            var levels = _listSO.FindProperty("Levels");
            if (_levelReorder == null) BuildLevelReorder(_listSO, levels);

            EditorGUILayout.HelpBox($"{list.Count} level. Index 0 = level đầu. Chơi hết list thì LẶP LẠI, "
                + "bỏ qua level có bật Skip Level Loop (level tutorial chỉ chơi 1 lần).", MessageType.None);

            // Reset cờ thao tác mỗi frame; nút trong dòng chỉ GHI cờ, xử lý SAU khi vẽ xong list để không
            // sửa mảng giữa lúc layout (tránh lỗi GUI khi số phần tử đổi).
            _pendingPlay = null; _pendingDelete = null; _pendingDup = -1;
            _levelReorder.DoLayoutList();
            _listSO.ApplyModifiedProperties();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("+ New Level", "Tạo asset LevelData mới và thêm vào cuối list.")))
                { CreateNewLevel(); GUIUtility.ExitGUI(); }
            using (new EditorGUI.DisabledScope(_target == null || list.IndexOf(_target) >= 0))
                if (GUILayout.Button(new GUIContent("+ Level đang mở", "Thêm level đang mở vào cuối list.")))
                { AddLevelToList(list, _target); GUIUtility.ExitGUI(); }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Fill from folder",
                "Nạp mọi LevelData trong Levels Folder theo thứ tự tên, ghi đè list hiện tại.")))
                { FillLevelListFromFolder(levels); _listSO.ApplyModifiedProperties(); }
            if (GUILayout.Button("Clear")) { levels.arraySize = 0; _listSO.ApplyModifiedProperties(); }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            // Áp thao tác đã ghi cờ (ngoài vòng vẽ list).
            if (_pendingDup >= 0) { DuplicateLevelInList(list, _pendingDup); GUIUtility.ExitGUI(); }
            else if (_pendingDelete != null) { DeleteLevelAndUnlist(list, _pendingDelete); GUIUtility.ExitGUI(); }
            else if (_pendingPlay != null) { Select(_pendingPlay); PlayLevel(_pendingPlay); GUIUtility.ExitGUI(); }
        }

        // Dựng ReorderableList cho LevelList.Levels: kéo thả sắp xếp (draggable), mỗi dòng tự vẽ tên +
        // 3 nút Play/Duplicate/Delete. Không hiện nút +/− mặc định của ReorderableList (dùng nút riêng bên
        // dưới để kèm thao tác tạo/nhân bản asset thật).
        private void BuildLevelReorder(SerializedObject so, SerializedProperty levels)
        {
            _levelReorder = new ReorderableList(so, levels, true, false, false, false)
            {
                elementHeight = EditorGUIUtility.singleLineHeight + 6f,
            };
            _levelReorder.drawElementCallback = (rect, index, active, focused) =>
            {
                if (index < 0 || index >= levels.arraySize) return;
                var el = levels.GetArrayElementAtIndex(index);
                var lv = el.objectReferenceValue as LevelData;
                rect.y += 3f; rect.height = EditorGUIUtility.singleLineHeight;

                const float bw = 26f, gap = 2f, idxW = 22f;
                float right = rect.xMax;
                var rDel = new Rect(right - bw, rect.y, bw, rect.height); right -= bw + gap;
                var rDup = new Rect(right - bw, rect.y, bw, rect.height); right -= bw + gap;
                var rPlay = new Rect(right - bw, rect.y, bw, rect.height); right -= bw + gap;
                var rIdx = new Rect(rect.x, rect.y, idxW, rect.height);
                var rName = new Rect(rect.x + idxW, rect.y, right - rect.x - idxW - gap, rect.height);

                EditorGUI.LabelField(rIdx, index.ToString());
                if (lv != null)
                {
                    bool sel = lv == _target;
                    string tag = lv.SkipLevelLoop ? " (skip loop)" : "";
                    if (GUI.Toggle(rName, sel, new GUIContent(lv.name + tag, "Bấm để mở level này trong tool."), "Button") && !sel)
                        Select(lv);
                }
                else el.objectReferenceValue = EditorGUI.ObjectField(rName, null, typeof(LevelData), false);

                using (new EditorGUI.DisabledScope(lv == null))
                {
                    if (GUI.Button(rPlay, new GUIContent("▶", "Chơi level này ngay (tự vào Play mode nếu đang tắt).")))
                        _pendingPlay = lv;
                    if (GUI.Button(rDup, new GUIContent("⧉", "Nhân bản asset và chèn ngay sau dòng này.")))
                        _pendingDup = index;
                    if (GUI.Button(rDel, new GUIContent("✕", "Xoá asset và gỡ khỏi list.")))
                        _pendingDelete = lv;
                }
            };
        }

        // Thêm 1 level vào cuối LevelList (không trùng lặp), ghi undo + dirty.
        private void AddLevelToList(LevelList list, LevelData lv)
        {
            if (list == null || lv == null || list.Levels.Contains(lv)) return;
            Undo.RecordObject(list, "Add Level");
            list.Levels.Add(lv);
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            _listSO = null; // buộc dựng lại SerializedObject + ReorderableList ở frame sau
        }

        // Nhân bản asset của level ở index rồi chèn ngay sau nó trong list.
        private void DuplicateLevelInList(LevelList list, int index)
        {
            if (list == null || index < 0 || index >= list.Levels.Count) return;
            var src = list.Levels[index];
            if (src == null) return;
            string srcPath = AssetDatabase.GetAssetPath(src);
            string newPath = AssetDatabase.GenerateUniqueAssetPath(srcPath);
            if (!AssetDatabase.CopyAsset(srcPath, newPath)) return;
            var dup = AssetDatabase.LoadAssetAtPath<LevelData>(newPath);
            Undo.RecordObject(list, "Duplicate Level");
            list.Levels.Insert(index + 1, dup);
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            _listSO = null;
            RefreshLevels();
            Select(dup);
        }

        // Xoá asset level và gỡ MỌI tham chiếu tới nó khỏi list (có hỏi xác nhận).
        private void DeleteLevelAndUnlist(LevelList list, LevelData lv)
        {
            if (lv == null) return;
            if (!EditorUtility.DisplayDialog("Delete Level", $"Xoá asset '{lv.name}' và gỡ khỏi list?", "Delete", "Cancel"))
                return;
            if (list != null)
            {
                Undo.RecordObject(list, "Delete Level");
                list.Levels.RemoveAll(x => x == lv);
                EditorUtility.SetDirty(list);
            }
            string path = AssetDatabase.GetAssetPath(lv);
            if (_target == lv) { _target = null; _so = null; }
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
            _listSO = null;
            RefreshLevels();
        }

        private void CreateLevelList()
        {
            const string dir = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Resources");
            var asset = CreateInstance<LevelList>();
            foreach (var lv in _levels) if (lv != null) asset.Levels.Add(lv);
            AssetDatabase.CreateAsset(asset, dir + "/LevelList.asset");
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(asset);
        }

        private void FillLevelListFromFolder(SerializedProperty levels)
        {
            RefreshLevels();
            levels.arraySize = _levels.Count;
            for (int i = 0; i < _levels.Count; i++) levels.GetArrayElementAtIndex(i).objectReferenceValue = _levels[i];
        }

        /// <summary>
        /// Bấm ▶ = chơi ĐÚNG level này, bất kể tiến trình đang ở đâu.
        /// Đang Play thì nạp thẳng vào LevelController. Chưa Play thì gửi level qua SessionState rồi vào
        /// Play mode — LevelController.Start() sẽ nhặt lên (xem LevelController.PlayLevelKey).
        /// </summary>
        private void PlayLevel(LevelData lv)
        {
            if (lv == null) return;

            if (Application.isPlaying)
            {
                var lc = Object.FindObjectOfType<LevelController>();
                if (lc == null) { Debug.LogWarning("[Level Tool] Scene không có LevelController."); return; }
                // PlayLevelNow chứ không phải LoadLevel(index): LoadLevel đi qua Resolve() nên Level
                // Override (nếu scene có gán) sẽ thắng, bấm level nào cũng ra đúng cái override đó.
                lc.PlayLevelNow(lv);
                return;
            }

            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(lv));
            if (string.IsNullOrEmpty(guid)) { Debug.LogWarning("[Level Tool] Level chưa lưu thành asset."); return; }
            AssetDatabase.SaveAssets(); // chơi đúng cái vừa sửa, không phải bản cũ còn trên đĩa
            SessionState.SetString(LevelController.PlayLevelKey, guid);
            EditorApplication.EnterPlaymode();
        }

        private void DrawGameSettings()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("GAME SETTINGS (config chung)", EditorStyles.boldLabel);
            var gs = GameSettings.Instance;
            EditorGUILayout.ObjectField("Asset", gs, typeof(GameSettings), false);
            if (gs == null)
            {
                if (GUILayout.Button("Create GameSettings")) CreateGameSettings();
                EditorGUILayout.EndVertical();
                return;
            }
            if (_gsSO == null || _gsSO.targetObject != gs) _gsSO = new SerializedObject(gs);
            _gsSO.Update();
            foreach (var name in new[] { "CoreType", "SlotGunSpacing", "PathWidth", "MaxGunOnPath", "GunSpeed", "GunSpacing",
                "FireInterval", "FireMode", "BurstSpawnStacked", "BurstRowLead", "GunFireRange",
                "GunFireAngle", "FrontStationDistance", "BulletSpeed", "BlockStackSpacing",
                "BlockCollapseDuration" })
                EditorGUILayout.PropertyField(_gsSO.FindProperty(name));
            _gsSO.ApplyModifiedProperties();
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Center panel

        private void DrawCenterPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var rect = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.14f));
            if (_target != null) DrawSchematicView(rect);
            else GUI.Label(rect, "Chọn 1 level ở panel trái", EditorStyles.centeredGreyMiniLabel);
            GUI.Label(new Rect(rect.x + 6, rect.yMax - 18, 320, 16),
                $"zoom {(_zoom * 100f):0}%  (scroll: zoom, chuột giữa/Alt+kéo: pan)", EditorStyles.miniLabel);
            if (_activeGrid >= 0)
            {
                var lbl = new GUIStyle(EditorStyles.miniBoldLabel);
                lbl.normal.textColor = new Color(0.4f, 1f, 0.5f);
                GUI.Label(new Rect(rect.x + 6, rect.y + 4, 360, 16),
                    $"Đang sửa CHỈ Grid {_activeGrid} (grid khác bị khóa/mờ — bỏ nút ✎ để sửa tất cả)", lbl);
            }
            EditorGUILayout.EndVertical();
        }

        // Vị trí vẽ thứ oi → index grid: đẩy grid ACTIVE xuống CUỐI (vẽ trên nóc). Không có active → giữ nguyên.
        private int DrawGridOrder(int oi)
        {
            int n = _target.Grids.Count;
            if (_activeGrid < 0 || _activeGrid >= n) return oi;
            if (oi == n - 1) return _activeGrid;            // slot cuối = grid active
            return oi < _activeGrid ? oi : oi + 1;          // các slot trước: bỏ qua index active
        }

        private void DrawSchematicView(Rect area)
        {
            Camera cam = _previewCamera;
            _camMode = cam != null;

            float aspW = _target.ScreenAspect.x, aspH = _target.ScreenAspect.y;
            Rect content = area;
            if (aspW > 0f && aspH > 0f)
            {
                float ta = aspW / aspH;
                if (area.width / area.height > ta) { float w = area.height * ta; content = new Rect(area.center.x - w / 2f, area.y, w, area.height); }
                else { float h = area.width / ta; content = new Rect(area.x, area.center.y - h / 2f, area.width, h); }
            }
            _viewContent = content;

            // Flat bounds (khi chưa gán camera).
            Vector2 min = new Vector2(-5, -5), max = new Vector2(5, 5);
            if (!_camMode)
            {
                if (_target.CameraOrthoSize > 0f)
                {
                    float hH = _target.CameraOrthoSize, hW = aspH > 0f ? hH * aspW / aspH : hH;
                    Vector2 cc = new Vector2(_target.CameraCenter.x, _target.CameraCenter.z);
                    min = cc + new Vector2(-hW, -hH); max = cc + new Vector2(hW, hH);
                }
                else
                {
                    var pts = new List<Vector3>();
                    if (_target.PathWaypoints != null) pts.AddRange(_target.PathWaypoints);
                    CollectGridPoints(pts); CollectSlotPoints(pts);
                    if (pts.Count > 0) { min = max = new Vector2(pts[0].x, pts[0].z); foreach (var p in pts) { var q = new Vector2(p.x, p.z); min = Vector2.Min(min, q); max = Vector2.Max(max, q); } }
                }
            }
            float spanX = Mathf.Max(0.001f, max.x - min.x), spanY = Mathf.Max(0.001f, max.y - min.y);
            float flatScale = Mathf.Min((content.width - 2 * Pad) / spanX, (content.height - 2 * Pad) / spanY);
            if (flatScale <= 0 || float.IsInfinity(flatScale)) flatScale = 1f;
            _viewMin = min; _viewScale = flatScale;

            HandleZoomPan(area, content.center);
            // Ép aspect = ScreenAspect (resolution gate) — physical cam + gate-fit tự dựng đúng khung
            // như game render trên thiết bị. KHÔNG tắt usePhysicalProperties để giữ WYSIWYG.
            if (_camMode && aspW > 0f && aspH > 0f) cam.aspect = aspW / aspH;

            Vector2 C = content.center;
            Vector2 ViewT(Vector2 p) => C + (p - C) * _zoom + _pan;
            Vector2 ProjBase(Vector3 w)
            {
                if (_camMode) { Vector3 vp = cam.WorldToViewportPoint(w); return new Vector2(content.x + vp.x * content.width, content.yMax - vp.y * content.height); }
                return new Vector2(content.x + Pad + (w.x - min.x) * flatScale, content.yMax - Pad - (w.z - min.y) * flatScale);
            }
            Vector2 Proj(Vector3 w) => ViewT(ProjBase(w));
            bool Front(Vector3 w) => !_camMode || cam.WorldToViewportPoint(w).z > 0f;
            float PixSize(Vector3 w, float world) => Mathf.Clamp((Proj(w + Vector3.right * world) - Proj(w)).magnitude, 3f, 160f);
            void Line(Vector2 a, Vector2 b) { if (ClipSegment(ref a, ref b, area)) Handles.DrawLine(a, b); }
            void FillRect(Rect rc, Color col) { var ir = RectIntersect(rc, area); if (ir.width > 0 && ir.height > 0) EditorGUI.DrawRect(ir, col); }
            void ArrowHead(Vector2 from, Vector2 to)
            {
                Vector2 d = to - from; float m = d.magnitude; if (m < 1e-3f) return; d /= m;
                Vector2 n = new Vector2(-d.y, d.x); float h = Mathf.Min(7f, m * 0.5f);
                Line(to, to - d * h + n * h * 0.55f);
                Line(to, to - d * h - n * h * 0.55f);
            }
            // Mũi tên NGẮN từ tâm cell about 42% về phía ô đích — cho biết hướng cell có thể dồn tới.
            void MoveArrow(Vector3 from, Vector3 target)
            {
                Vector3 dd = target - from; dd.y = 0f;
                if (dd.sqrMagnitude < 1e-6f) return;
                Vector3 tipW = from + dd * 0.42f;
                if (!Front(from) || !Front(tipW)) return;
                Vector2 a = Proj(from), b = Proj(tipW);
                Line(a, b); ArrowHead(a, b);
            }

            // Khung camera = 4 góc frustum cắt y=0 (đúng vùng camera nhìn trên sàn).
            if (_showFrame) DrawCameraFrame(cam, aspW, aspH, Proj, Front, Line);

            // Đường bo góc dùng CHUNG với runtime; dựng 1 lần rồi cả path lẫn vòng range đều xài.
            _pathSamples = _target.PathWaypoints != null && _target.PathWaypoints.Count >= 2
                ? RoundedPolylinePath.BuildSamples(_target.PathWaypoints, _target.IsClosed,
                                                   _target.CornerRadius, 8, _target.PathStyle)
                : null;

            // Path: vẽ ĐÚNG đường bo góc, kèm 2 mép theo PathWidth để thấy mặt đường rộng bao nhiêu.
            if (_showPath)
            {
                var samples = _pathSamples;
                if (samples != null && samples.Length >= 2)
                {
                    float half = Mathf.Max(0f, PathWidthShared) * 0.5f;
                    for (int i = 1; i < samples.Length; i++)
                    {
                        Vector3 p0 = samples[i - 1], p1 = samples[i];
                        if (!Front(p0) || !Front(p1)) continue;

                        Handles.color = new Color(0.3f, 0.8f, 1f, 0.55f);
                        Line(Proj(p0), Proj(p1)); // tim đường

                        if (half <= 0f) continue;
                        Vector3 dir = p1 - p0; dir.y = 0f;
                        if (dir.sqrMagnitude < 1e-8f) continue;
                        Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * half;
                        Handles.color = new Color(0.3f, 0.8f, 1f, 0.95f);
                        Line(Proj(p0 + side), Proj(p1 + side)); // mép trái
                        Line(Proj(p0 - side), Proj(p1 - side)); // mép phải
                    }
                }
            }

            // HAI quạt CHỌN TARGET của gun tại ĐIỂM VÀO path — mọi gun xuất phát từ đây rồi chạy dọc path.
            // Quạt chỉ lọc lúc CHỌN: chốt được cell rồi thì nòng bắn dứt điểm hết stack kể cả khi cell đã
            // trôi ra ngoài quạt, nên tầm bắn THỰC TẾ rộng hơn hình vẽ này.
            // Gun KHÔNG quay mặt về target: thân luôn hướng theo đường ray. Mỗi nòng quét TỪ hướng trước
            // mặt rồi toả sang sườn của nó đúng GunFireAngle độ — 2 quạt chung mép ở trục trước mặt, nòng
            // phải (R) toả sang phải, nòng trái (L) sang trái. Mỗi nòng có target riêng và không bắn trùng
            // cell với nòng kia (xem Gun.TickBarrel). GunFireAngle = 180 thì 2 quạt phủ kín vòng tròn.
            if (_showRange && _pathSamples != null && _pathSamples.Length >= 2)
            {
                var gs = GameSettings.Instance;
                float rng = gs != null ? gs.GunFireRange : 3f;
                float spread = Mathf.Clamp(gs != null ? gs.GunFireAngle : 360f, 0f, 180f);
                float front = gs != null ? gs.FrontStationDistance : 0f;
                Vector3 ctr = PathPointAt(front);
                Vector2 c = Proj(ctr);

                // Hướng path tại điểm vào: look-ahead 0.05 y hệt RoundedPolylineFollower.
                Vector3 fwd = PathPointAt(front + 0.05f) - ctr; fwd.y = 0f;
                if (fwd.sqrMagnitude > 1e-6f) fwd.Normalize(); else fwd = Vector3.forward;

                var sideLbl = new GUIStyle(EditorStyles.miniBoldLabel);
                sideLbl.normal.textColor = new Color(1f, 0.9f, 0.4f);
                const int SEG = 18;
                for (int s = 0; s < 2; s++)
                {
                    float sign = s == 0 ? 1f : -1f; // +1 = nòng phải, −1 = nòng trái
                    Vector3 D(int k) => Quaternion.AngleAxis(spread * sign * k / SEG, Vector3.up) * fwd;

                    Handles.color = new Color(1f, 0.85f, 0.2f, 0.5f);
                    Vector2 prev = Proj(ctr + D(0) * rng);
                    for (int k = 1; k <= SEG; k++) { Vector2 p = Proj(ctr + D(k) * rng); Line(prev, p); prev = p; }
                    Line(c, Proj(ctr + D(SEG) * rng)); // mép NGOÀI; mép trong trùng trục trước mặt, vẽ dưới

                    Handles.color = new Color(1f, 0.9f, 0.4f, 0.6f);
                    Vector2 outTip = Proj(ctr + D(SEG) * rng);
                    if (area.Contains(outTip))
                        GUI.Label(new Rect(outTip.x + 4f, outTip.y - 8f, 14f, 14f), s == 0 ? "R" : "L", sideLbl);
                }

                // Trục TRƯỚC MẶT = mép chung của 2 quạt, cũng là hướng gun chạy.
                Handles.color = new Color(1f, 0.85f, 0.2f, 1f);
                Vector2 fTip = Proj(ctr + fwd * rng);
                Line(c, fTip); ArrowHead(c, fTip);

                FillRect(new Rect(c.x - 3, c.y - 3, 6, 6), Color.yellow);
            }

            if (_showPath) HandleWaypointDrag(Proj, area);

            // Grids (fan vòng cung).
            if (_showBlocks && _target.Grids != null)
            {
                var blockLbl = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
                blockLbl.normal.textColor = Color.white;

                // Thu vùng click MỌI frame: brush tô màu chạy trên MouseDrag, không chỉ MouseDown/Up.
                _hitCells.Clear();
                _hitQueue.Clear();

                // Grid active trỏ ra ngoài danh sách (vừa xoá grid) → về -1, không thì khóa sạch mọi grid.
                if (_activeGrid >= _target.Grids.Count) _activeGrid = -1;

                for (int oi = 0; oi < _target.Grids.Count; oi++)
                {
                    // Vẽ grid ACTIVE SAU CÙNG (trên nóc) để không bị grid mờ index lớn hơn vẽ đè che mất.
                    int gi = DrawGridOrder(oi);
                    var grid = _target.Grids[gi];
                    if (grid == null) continue;
                    int last = Mathf.Max(0, grid.Rows - 1);
                    // Grid active (hoặc chưa chọn grid nào) mới cho SỬA; grid khác chỉ hiện mờ, không nhận click.
                    bool editable = _activeGrid < 0 || gi == _activeGrid;

                    // Viền: Rect = 4 cạnh; Arc = 2 cạnh bên. Spline bỏ qua — dải uốn lượn không có "cạnh"
                    // nối thẳng được, vẽ vào chỉ thành đường chém ngang qua grid; đã có đường tím thay thế.
                    Handles.color = new Color(0.7f, 0.7f, 0.7f);
                    if (grid.Shape == BlockGridShape.Rect)
                    {
                        Vector3 p0 = grid.CellPos(0, 0), p1 = grid.CellPos(0, grid.ElementsInRow(0) - 1);
                        Vector3 p2 = grid.CellPos(last, grid.ElementsInRow(last) - 1), p3 = grid.CellPos(last, 0);
                        Line(Proj(p0), Proj(p1)); Line(Proj(p1), Proj(p2)); Line(Proj(p2), Proj(p3)); Line(Proj(p3), Proj(p0));
                    }
                    else if (grid.Shape == BlockGridShape.Arc)
                    {
                        Line(Proj(grid.CellPos(0, 0)), Proj(grid.CellPos(last, grid.ElementsInRow(last) - 1)));
                        Line(Proj(grid.CellPos(0, grid.ElementsInRow(0) - 1)), Proj(grid.CellPos(last, 0)));
                    }

                    // Thanh cạnh: cho biết các HƯỚNG bắn được của grid (ShootableEdges).
                    if (_showEdges) DrawGridEdges(grid, last, area, Proj, Front);

                    for (int r = 0; r < grid.Rows; r++)
                    {
                        int count = grid.ElementsInRow(r);
                        for (int e = 0; e < count; e++)
                        {
                            var cell = grid.GetCell(r, e);
                            if (cell == null) continue;
                            bool empty = cell.BlockStackCt <= 0;
                            // Ô đã XOÁ (stack 0): chỉ hiện khi đang tô màu, để click phục hồi + đổi màu.
                            if (empty && _paintColor == TypeColor.None) continue;
                            Vector3 wp = grid.CellPos(r, e);
                            if (!Front(wp)) continue;
                            // Kích thước vẽ = kích thước THẬT của block (prefab 1x1 × CellScale của grid).
                            Vector2 bp = Proj(wp);
                            float sz = PixSize(wp, Mathf.Max(0.05f, grid.CellScale.x));
                            var cellRect = new Rect(bp.x - sz / 2, bp.y - sz / 2, sz, sz);
                            int flatIdx = grid.CellIndex(r, e);
                            if (editable) _hitCells.Add((cellRect, gi, flatIdx)); // grid khóa → không bắt click
                            if (empty)
                            {
                                // Ghost ô trống: fill rất mờ + viền, click (tô màu) sẽ phục hồi stack.
                                FillRect(cellRect, new Color(1f, 1f, 1f, 0.06f));
                                DrawOutline(cellRect,
                                    IsCellSelected(gi, flatIdx) ? Color.yellow : new Color(1f, 1f, 1f, 0.4f), area);
                                continue;
                            }
                            bool isSpawner = cell.Type.IsSpawner();
                            var cellCol = GlobalConfigManager.ColorOf(cell.Color);
                            if (!editable) cellCol.a *= 0.22f; // grid bị khóa → làm MỜ HẲN để nổi grid active
                            FillRect(cellRect, cellCol);
                            // Cell KHÔNG bắn được: gạch chéo X + viền đen để phân biệt.
                            if (!cell.Shootable)
                            {
                                Handles.color = new Color(0f, 0f, 0f, 0.75f);
                                Line(new Vector2(cellRect.x, cellRect.y), new Vector2(cellRect.xMax, cellRect.yMax));
                                Line(new Vector2(cellRect.xMax, cellRect.y), new Vector2(cellRect.x, cellRect.yMax));
                                DrawOutline(cellRect, new Color(0f, 0f, 0f, 0.9f), area);
                            }
                            // Băng phủ: lớp xanh băng mờ + viền xanh (cell chưa bắn được cho tới khi tan).
                            if (cell.Iced)
                            {
                                FillRect(cellRect, new Color(0.5f, 0.85f, 1f, 0.35f));
                                DrawOutline(cellRect, new Color(0.4f, 0.8f, 1f, 0.95f), area);
                            }
                            // Viền vàng = cell đang chọn; viền cam = cell Spawner (còn hàng đợi phía sau).
                            if (IsCellSelected(gi, flatIdx)) DrawOutline(cellRect, Color.yellow, area);
                            else if (isSpawner) DrawOutline(cellRect, SpawnerCol, area);
                            // Số block trong stack (giống nhãn số đạn của gun).
                            if (sz >= 10f && area.Contains(bp))
                                GUI.Label(new Rect(bp.x - sz / 2, bp.y - sz / 2, sz, sz), cell.BlockStackCt.ToString(), blockLbl);

                            // Các HƯỚNG cell có thể dồn/move khi collapse: DỌC (về hàng 0) luôn có; NGANG (về
                            // index 0) chỉ khi Collapse2D. KHÔNG có chéo — chéo là của riêng Spawner8 (xem Dir).
                            if (_showMove)
                            {
                                Handles.color = new Color(0.3f, 0.9f, 1f, 0.95f);
                                if (r > 0 && e < grid.ElementsInRow(r - 1)) MoveArrow(wp, grid.CellPos(r - 1, e));
                                if (grid.Collapse2D && e > 0) MoveArrow(wp, grid.CellPos(r, e - 1));
                            }

                        }
                    }

                    // Hàng đợi Spawner vẽ SAU toàn bộ cell thật: ghost xếp lùi về phía hàng sâu hơn, vẽ
                    // xen trong vòng trên thì bị cell hàng sau đè lên — mà click lại ưu tiên ghost, thành
                    // ra nhìn một đằng bấm một nẻo. Tắt Queue = không vẽ → _hitQueue rỗng → ghost cũng
                    // hết ăn click, không còn cướp chuột của cell thật nằm dưới.
                    if (_showQueue && editable) // grid khóa → không vẽ/không bắt click ghost hàng đợi
                        for (int r = 0; r < grid.Rows; r++)
                        {
                            int count = grid.ElementsInRow(r);
                            for (int e = 0; e < count; e++)
                            {
                                var cell = grid.GetCell(r, e);
                                if (cell == null || cell.BlockStackCt <= 0 || !cell.Type.IsSpawner()) continue;
                                Vector3 wp = grid.CellPos(r, e);
                                if (!Front(wp)) continue;
                                DrawQueueGhosts(gi, grid, r, e, count, wp, grid.CellIndex(r, e), cell, area,
                                                Proj, Front, PixSize);
                            }
                        }

                    // Mũi tên HƯỚNG + handle xoay vẽ SAU CÙNG (sau cả cell lẫn ghost hàng đợi): vẽ xen trong
                    // vòng cell thì cell/ghost hàng sau ĐÈ lên, handle xoay của SpawnerLine bị che, khó bấm.
                    if (_showDir)
                        for (int r = 0; r < grid.Rows; r++)
                        {
                            int count = grid.ElementsInRow(r);
                            for (int e = 0; e < count; e++)
                            {
                                var cell = grid.GetCell(r, e);
                                if (cell == null || cell.BlockStackCt <= 0) continue;
                                Vector3 wp = grid.CellPos(r, e);
                                if (!Front(wp)) continue;
                                Vector2 bp = Proj(wp);
                                bool isSpawner = cell.Type.IsSpawner();
                                // SpawnerLine: hướng nhả do NGƯỜI DÙNG đặt (kéo xoay), kể cả trên Rect/Spline
                                // vốn auto-hướng. Còn lại: Rect/Spline auto theo grid, Arc đọc data.
                                bool autoDir = grid.CellAngleFromShape && cell.Type != BlockCellType.SpawnerLine;
                                float baseAng = autoDir ? grid.DefaultCellAngle(r, e) : cell.SpawnerDirectionAngleZ;
                                if (cell.Type == BlockCellType.Spawner8)
                                {
                                    // Spawner8: 8 mũi tên ngắn toả đều — nhả ra mọi ô trống, không có 1 hướng để kéo.
                                    Handles.color = SpawnerCol;
                                    for (int k = 0; k < 8; k++)
                                    {
                                        Vector3 v8 = Quaternion.Euler(0f, baseAng + k * 45f, 0f) * Vector3.forward;
                                        Vector3 tip8 = wp + v8 * 0.6f;
                                        if (!Front(tip8)) continue;
                                        Vector2 t8 = Proj(tip8);
                                        Line(bp, t8); ArrowHead(bp, t8);
                                    }
                                }
                                else
                                {
                                    Vector3 dirV = Quaternion.Euler(0f, baseAng, 0f) * Vector3.forward;
                                    Vector3 tipW = wp + dirV * (isSpawner ? 0.95f : 0.55f);
                                    if (Front(tipW))
                                    {
                                        Vector2 tip = Proj(tipW);
                                        Handles.color = isSpawner ? SpawnerCol : new Color(1f, 1f, 1f, 0.9f);
                                        Line(bp, tip); ArrowHead(bp, tip);
                                        if (!autoDir && editable) CellRotateHandle(gi, grid.CellIndex(r, e), wp, tip, area);
                                    }
                                }
                            }
                        }

                    // Spline: hình dạng do waypoint quyết định → vẽ đường + handle waypoint, và BỎ 2 handle
                    // đầu cạnh (chúng chỉnh BaseRadius/ArcAngle, vô nghĩa ở đây).
                    if (grid.Shape == BlockGridShape.Spline) DrawSplineHandles(gi, grid, area, Proj, Line);

                    // Handle: tâm + 2 đầu cạnh + xoay (kéo được).
                    GridHandle(gi, 0, Proj(grid.Center), area);
                    if (grid.Shape != BlockGridShape.Spline)
                    {
                        GridHandle(gi, 1, Proj(grid.CellPos(0, 0)), area);                             // trái
                        GridHandle(gi, 2, Proj(grid.CellPos(0, grid.ElementsInRow(0) - 1)), area);     // phải
                    }
                    // Rect: thêm 2 handle GÓC SAU (hàng cuối) — kéo để chỉnh SỐ HÀNG (theo chiều sâu) +
                    // SỐ CỘT (theo bề ngang), khỏi nhập tay.
                    if (grid.Shape == BlockGridShape.Rect)
                    {
                        int lastR = Mathf.Max(0, grid.Rows - 1);
                        GridHandle(gi, 4, Proj(grid.CellPos(lastR, 0)), area);
                        GridHandle(gi, 5, Proj(grid.CellPos(lastR, grid.ElementsInRow(lastR) - 1)), area);
                    }

                    // Handle xoay: nằm trên hướng "sâu dần" của grid, ra ngoài hàng cuối 1 bậc.
                    Vector3 rotW = grid.Center + grid.Forward *
                                   (grid.BaseRadius + Mathf.Max(1, grid.Rows) * grid.RowSpacing);
                    if (Front(rotW))
                    {
                        Handles.color = new Color(0.3f, 1f, 0.4f, 0.9f);
                        Line(Proj(grid.Center), Proj(rotW));
                        GridHandle(gi, 3, Proj(rotW), area);
                    }

                }
                ApplyGridHandleDrag();
                ApplySplineDrag();
                if (_showDir) ApplyCellRotateDrag();
                // Tô màu chạy SAU toàn bộ handle (handle đã e.Use() thì tới đây không còn MouseDown nữa)
                // và sau cả vòng vẽ, vì brush cần _hitCells/_hitQueue của MỌI grid đã gom đủ.
                HandleQueuePaint(area);
                HandleCellPaint(area);
            }

            // Slots.
            if (_showSlots && _target.Slots != null)
            {
                var slotPos = GetSlotBasePositions();
                if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp) _hitGuns.Clear();
                _hitGunAdd.Clear();
                float spacing = GameSettings.Instance != null ? GameSettings.Instance.SlotGunSpacing : 1f;
                var lbl = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
                lbl.normal.textColor = Color.white;
                // Gom vị trí gun theo nhóm CONNECT để vẽ đường nối (màu 2 đầu theo gun).
                var connectPts = new Dictionary<int, List<(Vector3 wp, TypeColor color)>>();
                for (int si = 0; si < slotPos.Count && si < _target.Slots.Count; si++)
                {
                    var guns = _target.Slots[si]?.Guns;
                    if (guns == null) continue;
                    Vector3 basePos = slotPos[si];
                    for (int i = 0; i < guns.Count; i++)
                    {
                        var g = guns[i]; if (g == null) continue;
                        Vector3 wp = basePos - Vector3.forward * spacing * i; // index 0 phía trước (+Z)
                        if (!Front(wp)) continue;
                        Vector2 gp = Proj(wp); float sz = PixSize(wp, 0.6f);
                        var gunRect = new Rect(gp.x - sz / 2, gp.y - sz / 2, sz, sz);
                        // Gun ẨN & CHƯA ở vị trí đầu (index 0) → vẽ xám (che màu) như lúc chơi, NHƯNG vẫn
                        // hiện SỐ LƯỢNG đạn (thay vì "?") để tool đọc được cấu hình.
                        bool hiddenNow = g.Hidden && i != 0;
                        FillRect(gunRect, hiddenNow ? new Color(0.45f, 0.45f, 0.48f) : GlobalConfigManager.ColorOf(g.Color));
                        if (area.Contains(gp)) GUI.Label(gunRect, g.CountBullet.ToString(), lbl);
                        if (_selSlot == si && _selGun == i) DrawOutline(gunRect, Color.yellow, area);
                        // Vùng click gun — chọn xử lý ở HandleMarquee (click ngắn = chọn 1 gun).
                        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)
                            _hitGuns.Add((gunRect, si, i));
                        // Gun connect → gom để vẽ đường nối.
                        if (g.ConnectGroup != 0)
                        {
                            if (!connectPts.TryGetValue(g.ConnectGroup, out var lst))
                            { lst = new List<(Vector3, TypeColor)>(); connectPts[g.ConnectGroup] = lst; }
                            lst.Add((wp, g.Color));
                        }
                    }

                    // Ô "+" ghost sau ĐUÔI slot (khi đang chọn Paint Color): click để thêm gun màu đó vào slot,
                    // đúng kiểu thêm cell vào queue của spawner.
                    if (_paintColor != TypeColor.None)
                    {
                        Vector3 addWp = basePos - Vector3.forward * spacing * guns.Count;
                        if (Front(addWp))
                        {
                            Vector2 ap = Proj(addWp); float asz = PixSize(addWp, 0.6f);
                            var addRect = new Rect(ap.x - asz / 2, ap.y - asz / 2, asz, asz);
                            var col = GlobalConfigManager.ColorOf(_paintColor); col.a = 0.35f;
                            FillRect(addRect, col);
                            DrawOutline(addRect, new Color(1f, 0.6f, 0.1f, 0.8f), area);
                            if (area.Contains(ap)) GUI.Label(addRect, "+", lbl);
                            _hitGunAdd.Add((addRect, si));
                        }
                    }
                }
                // Đường nối các gun CONNECT (nhóm ≥2): mỗi đoạn tô nửa màu gun đầu, nửa màu gun cuối.
                foreach (var kv in connectPts)
                {
                    var pts = kv.Value;
                    if (pts.Count < 2) continue;
                    for (int i = 0; i + 1 < pts.Count; i++)
                    {
                        if (!Front(pts[i].wp) || !Front(pts[i + 1].wp)) continue;
                        Vector2 pa = Proj(pts[i].wp), pb = Proj(pts[i + 1].wp), pm = (pa + pb) * 0.5f;
                        Handles.color = GlobalConfigManager.ColorOf(pts[i].color); Line(pa, pm);
                        Handles.color = GlobalConfigManager.ColorOf(pts[i + 1].color); Line(pm, pb);
                    }
                }
                HandleGunPaint(area); // thêm/đổi màu/xoá gun bằng chuột — trước marquee để nuốt click
            }

            // Obstacle: vẽ footprint + handle TRƯỚC marquee để handle nuốt click trước khi quét chọn.
            if (_showObstacles) DrawObstacles(area, Proj, Front, Line);

            // Cuối cùng: quét chọn / click chọn. Đặt sau mọi handle nên nếu handle đã e.Use() thì bỏ qua.
            HandleMarquee(area);
            HandleDeleteKey(area);

            if (_camMode) cam.ResetAspect();
        }

        // Paint = None: kéo chuột = QUÉT chọn nhiều cell; click ngắn = chọn 1 cell/gun.
        // Giữ Ctrl (hoặc Cmd) = chọn THÊM (click lại vào cell đã chọn thì bỏ chọn).
        private void HandleMarquee(Rect area)
        {
            if (_paintColor != TypeColor.None || _eraseMode || _shootMode) return; // tô màu / xoá / shoot → không quét
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || e.alt || !area.Contains(e.mousePosition)) break;
                    _marqueeStart = _marqueeEnd = e.mousePosition;
                    _marqueeOn = true;
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (!_marqueeOn) break;
                    _marqueeEnd = e.mousePosition;
                    e.Use(); Repaint();
                    break;

                case EventType.MouseUp:
                    if (!_marqueeOn) break;
                    _marqueeOn = false;
                    _marqueeEnd = e.mousePosition;
                    bool additive = e.control || e.command;
                    var r = MarqueeRect();
                    if (r.width < 3f && r.height < 3f) ClickSelect(e.mousePosition, additive);
                    else RectSelect(r, additive);
                    e.Use(); Repaint();
                    break;

                case EventType.Repaint:
                    if (!_marqueeOn) break;
                    var mr = MarqueeRect();
                    EditorGUI.DrawRect(RectIntersect(mr, area), new Color(0.3f, 0.7f, 1f, 0.15f));
                    DrawOutline(mr, new Color(0.4f, 0.8f, 1f, 0.9f), area);
                    break;
            }
        }

        private Rect MarqueeRect()
        {
            float x = Mathf.Min(_marqueeStart.x, _marqueeEnd.x), y = Mathf.Min(_marqueeStart.y, _marqueeEnd.y);
            return new Rect(x, y, Mathf.Abs(_marqueeEnd.x - _marqueeStart.x), Mathf.Abs(_marqueeEnd.y - _marqueeStart.y));
        }

        private void ClickSelect(Vector2 pos, bool additive)
        {
            // Ghost hàng đợi xét TRƯỚC cell thật: nó vẽ đè lên vùng của các hàng sâu hơn, không ưu tiên
            // thì click trúng ghost lại chọn nhầm cell nằm dưới. Ô "+" (qi >= Queue.Count) không chọn được.
            foreach (var h in _hitQueue)
                if (h.rect.Contains(pos) && h.qi < QueueSize(h.grid, h.flat))
                { SelectQueue(h.grid, h.flat, h.qi); return; }
            foreach (var h in _hitCells)
                if (h.rect.Contains(pos)) { ToggleCell(h.grid, h.flat, additive); return; }
            foreach (var h in _hitGuns)
                if (h.rect.Contains(pos)) { SelectGun(h.slot, h.gun); return; }
            // click ra chỗ trống = bỏ chọn
            if (!additive) { _selCells.Clear(); _selSlot = -1; _selGun = -1; ClearQueueSel(); _selWaypoints.Clear(); }
        }

        private int QueueSize(int gi, int flat)
        {
            if (_target?.Grids == null || gi < 0 || gi >= _target.Grids.Count) return 0;
            var cells = _target.Grids[gi]?.Cells;
            if (cells == null || flat < 0 || flat >= cells.Count) return 0;
            return cells[flat]?.Queue?.Count ?? 0;
        }

        /// <summary>
        /// Phím Delete/Backspace: xoá thứ đang chọn. Cell trong hàng đợi Spawner → rút khỏi Queue. Cell
        /// trên grid → đặt stack = 0, tức thành LỖ: runtime không dựng cell VÀ không cho cell nào dồn vào
        /// (xem GridBlockManager.CanEnter) — grid giữ nguyên hình dạng, chỉ thủng đúng ô đó.
        /// </summary>
        // Thêm/đổi màu/xoá gun bằng chuột (giống HandleQueuePaint của cell). TRÁI + có Paint Color: click ô
        // "+" = thêm gun màu đó vào slot; click gun cũ = đổi màu nó. PHẢI (hoặc TRÁI khi Erase) trên 1 gun = xoá.
        private void HandleGunPaint(Rect area)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.alt || !area.Contains(e.mousePosition)) return;
            bool paint = e.button == 0 && _paintColor != TypeColor.None && !_eraseMode && !_shootMode;
            bool del = e.button == 1 || (e.button == 0 && _eraseMode);
            if (!paint && !del) return;

            var slots = _so.FindProperty("Slots");

            if (paint)
                foreach (var h in _hitGunAdd)
                    if (h.rect.Contains(e.mousePosition))
                    {
                        if (h.slot < 0 || h.slot >= slots.arraySize) return;
                        var guns = slots.GetArrayElementAtIndex(h.slot).FindPropertyRelative("Guns");
                        var g = guns.GetArrayElementAtIndex(AddArray(guns));
                        g.FindPropertyRelative("Color").enumValueIndex = (int)_paintColor;
                        g.FindPropertyRelative("CountBullet").intValue = 5;
                        e.Use(); Repaint(); return;
                    }

            foreach (var h in _hitGuns)
                if (h.rect.Contains(e.mousePosition))
                {
                    if (h.slot < 0 || h.slot >= slots.arraySize) return;
                    var guns = slots.GetArrayElementAtIndex(h.slot).FindPropertyRelative("Guns");
                    if (h.gun < 0 || h.gun >= guns.arraySize) return;
                    if (del)
                    {
                        guns.DeleteArrayElementAtIndex(h.gun);
                        if (_selSlot == h.slot && _selGun == h.gun) { _selGun = -1; _selSlot = -1; }
                    }
                    else guns.GetArrayElementAtIndex(h.gun).FindPropertyRelative("Color").enumValueIndex = (int)_paintColor;
                    e.Use(); Repaint(); return;
                }
        }

        private void HandleDeleteKey(Rect area)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode != KeyCode.Delete && e.keyCode != KeyCode.Backspace) return;
            if (!area.Contains(e.mousePosition)) return; // chỉ ăn khi chuột đang ở khung giữa

            var grids = _so.FindProperty("Grids");
            if (_selQueue.qi >= 0)
            {
                var q = QueueProp(grids, _selQueue.grid, _selQueue.cell);
                if (q != null && _selQueue.qi < q.arraySize) q.DeleteArrayElementAtIndex(_selQueue.qi);
                ClearQueueSel();
                e.Use(); Repaint();
                return;
            }

            // Đang chọn 1 GUN → Delete xoá gun đó khỏi slot.
            if (_selGun >= 0 && _selSlot >= 0)
            {
                var s = _so.FindProperty("Slots");
                if (_selSlot < s.arraySize)
                {
                    var guns = s.GetArrayElementAtIndex(_selSlot).FindPropertyRelative("Guns");
                    if (_selGun < guns.arraySize) guns.DeleteArrayElementAtIndex(_selGun);
                }
                _selGun = -1; _selSlot = -1;
                e.Use(); Repaint();
                return;
            }

            if (_selCells.Count == 0) return;
            foreach (var (gi, ci) in _selCells)
            {
                if (gi < 0 || gi >= grids.arraySize) continue;
                var cells = grids.GetArrayElementAtIndex(gi).FindPropertyRelative("Cells");
                if (ci < 0 || ci >= cells.arraySize) continue;
                var cd = cells.GetArrayElementAtIndex(ci);
                cd.FindPropertyRelative("BlockStackCt").intValue = 0;
                var qp = cd.FindPropertyRelative("Queue"); // xoá luôn queue (ô thành lỗ)
                if (qp != null) qp.arraySize = 0;
            }
            _selCells.Clear();
            e.Use(); Repaint();
        }

        private static SerializedProperty QueueProp(SerializedProperty grids, int gi, int flat)
        {
            if (gi < 0 || gi >= grids.arraySize) return null;
            var cells = grids.GetArrayElementAtIndex(gi).FindPropertyRelative("Cells");
            if (flat < 0 || flat >= cells.arraySize) return null;
            return cells.GetArrayElementAtIndex(flat).FindPropertyRelative("Queue");
        }

        // Quét: mọi cell có ô GIAO với vùng quét đều được chọn.
        private void RectSelect(Rect r, bool additive)
        {
            if (!additive) { _selCells.Clear(); _selWaypoints.Clear(); }
            _selSlot = -1; _selGun = -1;
            foreach (var h in _hitCells)
            {
                var ir = RectIntersect(h.rect, r);
                if (ir.width <= 0f || ir.height <= 0f) continue;
                if (!_selCells.Contains((h.grid, h.flat))) _selCells.Add((h.grid, h.flat));
            }
            // Quét cả waypoint path (để căn thẳng dọc/ngang).
            if (_showPath)
                foreach (var h in _hitWaypoints)
                {
                    var ir = RectIntersect(h.rect, r);
                    if (ir.width <= 0f || ir.height <= 0f) continue;
                    if (!_selWaypoints.Contains(h.index)) _selWaypoints.Add(h.index);
                }
        }

        // Vẽ khung camera bằng 4 góc viewport chiếu ray xuống y=0.
        private void DrawCameraFrame(Camera cam, float aspW, float aspH, System.Func<Vector3, Vector2> Proj,
            System.Func<Vector3, bool> Front, System.Action<Vector2, Vector2> Line)
        {
            Handles.color = new Color(1f, 0.55f, 0.1f);
            if (cam != null)
            {
                var plane = new Plane(Vector3.up, Vector3.zero);
                Vector2[] vps = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                Vector3[] g = new Vector3[4];
                bool[] ok = new bool[4];
                for (int i = 0; i < 4; i++)
                {
                    Ray ray = cam.ViewportPointToRay(vps[i]);
                    if (plane.Raycast(ray, out float e)) { g[i] = ray.GetPoint(e); ok[i] = true; }
                }
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    if (ok[i] && ok[j] && Front(g[i]) && Front(g[j])) Line(Proj(g[i]), Proj(g[j]));
                }
            }
            else if (_target.CameraOrthoSize > 0f)
            {
                float hH = _target.CameraOrthoSize, hW = aspH > 0f ? hH * aspW / aspH : hH;
                Vector3 c = _target.CameraCenter;
                Vector3 a = c + new Vector3(-hW, 0, -hH), b = c + new Vector3(hW, 0, -hH), d = c + new Vector3(hW, 0, hH), e2 = c + new Vector3(-hW, 0, hH);
                Line(Proj(a), Proj(b)); Line(Proj(b), Proj(d)); Line(Proj(d), Proj(e2)); Line(Proj(e2), Proj(a));
            }
        }

        /// <summary>
        /// Vẽ thanh dày quanh grid cho biết mặt nào bắn được (khớp <see cref="GridBlockManager"/> IsShootable):
        /// XANH = cạnh bắn được, ĐỎ = mặt chặn. Grid mặc định (ShootableEdges = None) chỉ hiện xanh ở mặt
        /// Front (hàng 0) như hành vi cũ. Cạnh vẽ theo TÂM cell nên bám cả Arc/Spline cong.
        /// </summary>
        private void DrawGridEdges(BlockGridData grid, int last, Rect area,
            System.Func<Vector3, Vector2> Proj, System.Func<Vector3, bool> Front)
        {
            var edges = grid.ShootableEdges;
            bool legacy = edges == GridEdges.None;
            bool front = legacy || (edges & GridEdges.Front) != 0;
            bool back  = !legacy && (edges & GridEdges.Back)  != 0;
            bool left  = !legacy && (edges & GridEdges.Left)  != 0;
            bool right = !legacy && (edges & GridEdges.Right) != 0;

            Vector3 fwd = grid.Forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 1e-6f ? Vector3.forward : fwd.normalized;
            Vector3 rgt = Vector3.Cross(Vector3.up, fwd); // local +X trên sàn
            float off = Mathf.Max(0.2f, (grid.BlockWidth + grid.Spacing) * 0.5f);

            var rowFront = EdgeRow(grid, 0);
            var rowBack  = EdgeRow(grid, last);
            var colLeft  = EdgeCol(grid, false);
            var colRight = EdgeCol(grid, true);

            // Đỏ (mặt chặn) trước — chỉ multi-side; grid mặc định không vẽ đỏ cho đỡ rối. Tiếp tuyến để
            // dựng đoạn ngắn khi cạnh chỉ 1 cell: hàng (Front/Back) trải theo rgt, cột (Left/Right) theo fwd.
            if (!legacy)
            {
                if (!front) DrawEdgeBar(rowFront, -fwd, rgt, off, true, area, Proj, Front);
                if (!back)  DrawEdgeBar(rowBack,   fwd, rgt, off, true, area, Proj, Front);
                if (!left)  DrawEdgeBar(colLeft,  -rgt, fwd, off, true, area, Proj, Front);
                if (!right) DrawEdgeBar(colRight,  rgt, fwd, off, true, area, Proj, Front);
            }
            // Xanh (bắn được) sau — nổi trên đỏ ở chỗ chồng (vd strip rộng 1 cell: Left/Right trùng nhau).
            if (front) DrawEdgeBar(rowFront, -fwd, rgt, off, false, area, Proj, Front);
            if (back)  DrawEdgeBar(rowBack,   fwd, rgt, off, false, area, Proj, Front);
            if (left)  DrawEdgeBar(colLeft,  -rgt, fwd, off, false, area, Proj, Front);
            if (right) DrawEdgeBar(colRight,  rgt, fwd, off, false, area, Proj, Front);
        }

        // Điểm tâm cell dọc HÀNG row (mặt Front/Back).
        private static Vector3[] EdgeRow(BlockGridData g, int row)
        {
            int n = Mathf.Max(1, g.ElementsInRow(row));
            var pts = new Vector3[n];
            for (int e = 0; e < n; e++) pts[e] = g.CellPos(row, e);
            return pts;
        }

        // Điểm tâm cell dọc CỘT mép (mặt Left = index 0, Right = index cuối) qua mọi hàng.
        private static Vector3[] EdgeCol(BlockGridData g, bool lastIndex)
        {
            int rows = Mathf.Max(1, g.Rows);
            var pts = new Vector3[rows];
            for (int r = 0; r < rows; r++)
            {
                int n = g.ElementsInRow(r);
                pts[r] = g.CellPos(r, lastIndex ? n - 1 : 0);
            }
            return pts;
        }

        private void DrawEdgeBar(Vector3[] world, Vector3 outward, Vector3 tangent, float off, bool blocked,
            Rect area, System.Func<Vector3, Vector2> Proj, System.Func<Vector3, bool> Front)
        {
            if (world == null || world.Length == 0) return;
            Handles.color = blocked ? new Color(1f, 0.32f, 0.30f, 0.6f) : new Color(0.35f, 1f, 0.45f, 0.95f);
            float w = blocked ? 3f : 6f;

            // Cạnh chỉ 1 cell → dựng đoạn ngắn dọc tiếp tuyến cho nhìn thấy được.
            if (world.Length == 1)
            {
                Vector3 p = world[0] + outward * off;
                if (Front(p)) DrawThick(Proj(p - tangent * off), Proj(p + tangent * off), w, area);
                return;
            }
            for (int i = 0; i + 1 < world.Length; i++)
            {
                Vector3 a = world[i] + outward * off, b = world[i + 1] + outward * off;
                if (Front(a) && Front(b)) DrawThick(Proj(a), Proj(b), w, area);
            }
        }

        private void DrawThick(Vector2 a, Vector2 b, float w, Rect area)
        {
            if (ClipSegment(ref a, ref b, area))
                Handles.DrawAAPolyLine(w, new Vector3(a.x, a.y, 0f), new Vector3(b.x, b.y, 0f));
        }

        private void GridHandle(int gi, int hid, Vector2 p, Rect area)
        {
            if (!area.Contains(p)) return;
            float hr = hid == 0 ? 7f : 6f;
            var hrect = new Rect(p.x - hr, p.y - hr, hr * 2, hr * 2);
            bool active = _dragGrid == gi && _dragHandle == hid;
            Color col = hid == 0 ? new Color(1f, 0.5f, 0.9f, 0.9f)          // tâm: hồng
                      : hid == 3 ? new Color(0.3f, 1f, 0.4f, 0.9f)          // xoay: xanh lá
                      : hid >= 4 ? new Color(0.3f, 1f, 0.9f, 0.95f)         // góc sau: teal (số hàng/cột)
                                 : new Color(0.3f, 0.9f, 1f, 0.9f);         // đầu cạnh trước: xanh dương
            EditorGUI.DrawRect(hrect, active ? Color.yellow : col);
            EditorGUIUtility.AddCursorRect(hrect, hid == 3 ? MouseCursor.RotateArrow : MouseCursor.MoveArrow);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && hrect.Contains(e.mousePosition))
            {
                _dragGrid = gi; _dragHandle = hid;
                if (_target != null && _target.Grids != null && gi < _target.Grids.Count && _target.Grids[gi] != null)
                    _dragGridStart = _target.Grids[gi].Center; // mốc để Shift khóa trục
                e.Use();
            }
        }

        // Kéo tâm (0) hoặc 2 đầu cạnh (1/2): đầu cạnh set BaseRadius (khoảng cách) + ArcAngle (góc).
        private void ApplyGridHandleDrag()
        {
            if (_dragGrid < 0 || _so == null) return;
            var grids = _so.FindProperty("Grids");
            if (_dragGrid >= grids.arraySize) { _dragGrid = -1; return; }
            var e = Event.current;
            if (e.type == EventType.MouseDrag)
            {
                Vector3 nw = InverseV(e.mousePosition);
                var g = grids.GetArrayElementAtIndex(_dragGrid);
                if (_dragHandle == 0)
                {
                    var cp = g.FindPropertyRelative("Center");
                    Vector3 old = cp.vector3Value;
                    Vector3 t = new Vector3(nw.x, old.y, nw.z);
                    // Giữ SHIFT → khóa vào 1 trục (trục nào dịch nhiều hơn tính từ điểm bắt đầu kéo).
                    if (e.shift)
                    {
                        if (Mathf.Abs(nw.x - _dragGridStart.x) >= Mathf.Abs(nw.z - _dragGridStart.z))
                            t.z = _dragGridStart.z; // chỉ đi ngang (X)
                        else
                            t.x = _dragGridStart.x; // chỉ đi dọc (Z)
                    }
                    cp.vector3Value = t;
                }
                else if (_dragHandle == 3)
                {
                    // Handle xoay: hướng từ tâm tới chuột = hướng "sâu dần" (local +Z) của grid.
                    Vector3 center = g.FindPropertyRelative("Center").vector3Value;
                    Vector3 v = nw - center; v.y = 0f;
                    if (v.sqrMagnitude > 1e-6f)
                        g.FindPropertyRelative("Rotation").floatValue =
                            Mathf.Repeat(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg, 360f);
                }
                else if (_dragHandle >= 4)
                {
                    // Rect — góc SAU (hàng cuối): CHỈ đổi SỐ HÀNG theo chiều SÂU (trục Z) — GIỮ nguyên số cột.
                    Vector3 center = g.FindPropertyRelative("Center").vector3Value;
                    Vector3 v = nw - center; v.y = 0f;
                    v = Quaternion.Euler(0f, -g.FindPropertyRelative("Rotation").floatValue, 0f) * v; // hệ local
                    float rowSpacing = Mathf.Max(0.01f, g.FindPropertyRelative("RowSpacing").floatValue);
                    float baseRadius = g.FindPropertyRelative("BaseRadius").floatValue;
                    // Hàng cuối ở Z = BaseRadius + (Rows-1)*RowSpacing → Rows = round((|v.z|−BaseRadius)/RowSpacing)+1.
                    g.FindPropertyRelative("Rows").intValue =
                        Mathf.Clamp(Mathf.RoundToInt((Mathf.Abs(v.z) - baseRadius) / rowSpacing) + 1, 1, 64);
                }
                else
                {
                    Vector3 center = g.FindPropertyRelative("Center").vector3Value;
                    Vector3 v = nw - center; v.y = 0f;
                    // Quy về hệ LOCAL của grid để công thức dưới đúng cả khi grid đã xoay.
                    v = Quaternion.Euler(0f, -g.FindPropertyRelative("Rotation").floatValue, 0f) * v;
                    if (g.FindPropertyRelative("Shape").enumValueIndex == (int)BlockGridShape.Rect)
                    {
                        // Rect: đầu cạnh TRƯỚC CHỈ đổi SỐ CỘT (trục X) — GIỮ nguyên khoảng cách row 0 (BaseRadius).
                        float step = Mathf.Max(0.01f, g.FindPropertyRelative("BlockWidth").floatValue
                                                    + g.FindPropertyRelative("Spacing").floatValue);
                        g.FindPropertyRelative("Columns").intValue =
                            Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(v.x) * 2f / step) + 1, 1, 64);
                    }
                    else
                    {
                        g.FindPropertyRelative("BaseRadius").floatValue = Mathf.Max(0.1f, v.magnitude);
                        float angle = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                        // Trần 360 (không phải 350): 360 = VÒNG KÍN, kéo tới đó mới ra được level kiểu ring.
                        g.FindPropertyRelative("ArcAngle").floatValue = Mathf.Clamp(Mathf.Abs(angle) * 2f, 1f, 360f);
                    }
                }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp)
            {
                _dragGrid = -1; _dragHandle = -1;
                _recomputeGridSides = true; // grid vừa dời/đổi kích thước → tính lại Left/Right theo path
                e.Use();
            }
        }

        /// <summary>
        /// Tính lại <see cref="BlockGridData.Side"/> (Left/Right) cho MỌI grid theo path: grid nằm bên
        /// TRÁI hay PHẢI của hướng đi path (pos0 → posN). Gọi khi tạo/di chuyển grid hoặc lúc Save.
        /// Ghi thẳng lên object (đã áp serialized) rồi SetDirty + _so.Update() để view khớp.
        /// </summary>
        private void RecomputeGridSides()
        {
            if (_target == null || _target.Grids == null) return;
            var wps = _target.PathWaypoints;
            bool changed = false;
            for (int gi = 0; gi < _target.Grids.Count; gi++)
            {
                var g = _target.Grids[gi];
                if (g == null) continue;
                var side = ComputeGridSide(g, wps, _target.IsClosed);
                if (side != g.Side) { g.Side = side; changed = true; }
            }
            if (changed)
            {
                EditorUtility.SetDirty(_target);
                _so?.Update();
            }
        }

        /// <summary>
        /// Grid nằm bên nào của path: lấy CELL của grid GẦN path nhất (điểm ngoài cùng gần path), tìm điểm
        /// gần nhất trên polyline path + tiếp tuyến theo hướng pos0→posN, rồi xét dấu tích chéo để ra
        /// TRÁI/PHẢI. Không đủ path (&lt;2 waypoint) hoặc cell nằm ngay trên path → giữ Side cũ.
        /// </summary>
        private static GridSide ComputeGridSide(BlockGridData grid, List<Vector3> wps, bool closed)
        {
            if (grid == null || wps == null || wps.Count < 2) return grid != null ? grid.Side : GridSide.Any;
            // Grid bị path bao NHIỀU MẶT (ShootableEdges) cần cả 2 nòng bắn từng mặt → không ép 1 bên,
            // giữ nguyên Side (thường là Any) do người thiết kế đặt.
            if (grid.ShootableEdges != GridEdges.None) return grid.Side;

            float best = float.MaxValue;
            Vector3 bestP = Vector3.zero, bestQ = Vector3.zero, bestT = Vector3.forward;
            int rows = Mathf.Max(1, grid.Rows);
            for (int r = 0; r < rows; r++)
            {
                int cnt = grid.ElementsInRow(r);
                for (int e = 0; e < cnt; e++)
                {
                    Vector3 p = grid.CellPos(r, e);
                    float d = NearestOnPolyline(p, wps, closed, out var q, out var t);
                    if (d < best) { best = d; bestP = p; bestQ = q; bestT = t; }
                }
            }
            if (best == float.MaxValue) return grid.Side;

            Vector3 v = bestP - bestQ; v.y = 0f;
            Vector3 tg = bestT; tg.y = 0f;
            // Tích chéo (tiếp tuyến × vector tới grid) trên XZ: >0 = bên PHẢI hướng đi, <0 = TRÁI.
            // Khớp gameplay: nòng phải (Sign +1) = transform.right = Cross(up, forward).
            float s = tg.z * v.x - tg.x * v.z;
            if (Mathf.Abs(s) < 1e-4f) return grid.Side; // cell nằm ngay trên path → không đổi
            return s > 0f ? GridSide.Right : GridSide.Left;
        }

        /// <summary>Khoảng cách bình phương (XZ) từ p tới polyline; trả điểm gần nhất q và tiếp tuyến đoạn
        /// (chuẩn hoá, theo chiều index tăng = pos0→posN).</summary>
        private static float NearestOnPolyline(Vector3 p, List<Vector3> wps, bool closed,
                                               out Vector3 q, out Vector3 tangent)
        {
            q = p; tangent = Vector3.forward;
            int n = wps.Count;
            int segs = closed ? n : n - 1;
            Vector3 pp = p; pp.y = 0f;
            float best = float.MaxValue;
            for (int i = 0; i < segs; i++)
            {
                Vector3 a = wps[i]; a.y = 0f;
                Vector3 b = wps[(i + 1) % n]; b.y = 0f;
                Vector3 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(pp - a, ab) / len2) : 0f;
                Vector3 proj = a + ab * t;
                float d = (pp - proj).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    q = proj;
                    tangent = len2 > 1e-8f ? ab / Mathf.Sqrt(len2) : Vector3.forward;
                }
            }
            return best;
        }

        private static readonly Color ObstacleCol = new Color(0.9f, 0.55f, 0.2f, 1f);

        /// <summary>
        /// Vẽ obstacle trên map: footprint hình chữ nhật = kích thước MODEL (bounds) × Scale, xoay quanh Y
        /// theo RotationY. Kèm 3 handle kéo được: TÂM (hồng, di chuyển) · XOAY (xanh lá) · SCALE (xanh
        /// dương, kéo góc). Footprint 1×1 khi chưa gán model.
        /// </summary>
        private void DrawObstacles(Rect area, System.Func<Vector3, Vector2> Proj, System.Func<Vector3, bool> Front,
                                   System.Action<Vector2, Vector2> Line)
        {
            var obs = _target.Obstacles;
            if (obs == null) return;
            var lbl = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            for (int i = 0; i < obs.Count; i++)
            {
                var o = obs[i];
                if (o == null) continue;
                Vector3 c = o.Pos; c.y = 0f;
                if (!Front(c)) continue;

                Vector3 fp = ModelFootprint(o.Prefab);
                Vector3 sc = o.Scale == Vector3.zero ? Vector3.one : o.Scale;
                float halfX = Mathf.Max(0.05f, fp.x * Mathf.Abs(sc.x)) * 0.5f;
                float halfZ = Mathf.Max(0.05f, fp.z * Mathf.Abs(sc.z)) * 0.5f;
                Quaternion rot = Quaternion.Euler(0f, o.RotationY, 0f);
                Vector3 ax = rot * Vector3.right, az = rot * Vector3.forward;
                Vector3 c0 = c - ax * halfX - az * halfZ, c1 = c + ax * halfX - az * halfZ;
                Vector3 c2 = c + ax * halfX + az * halfZ, c3 = c - ax * halfX + az * halfZ;

                bool sel = _selObstacle == i;
                Handles.color = sel ? Color.yellow : ObstacleCol;
                Line(Proj(c0), Proj(c1)); Line(Proj(c1), Proj(c2));
                Line(Proj(c2), Proj(c3)); Line(Proj(c3), Proj(c0));

                Vector2 cp = Proj(c);
                if (area.Contains(cp))
                {
                    lbl.normal.textColor = sel ? Color.yellow : ObstacleCol;
                    GUI.Label(new Rect(cp.x - 50, cp.y - 20, 100, 16),
                              o.Prefab != null ? o.Prefab.name : $"Obstacle {i}", lbl);
                }

                // Handle xoay: ra ngoài theo +Z local, 1 bậc ngoài mép.
                Vector3 rotW = c + az * (halfZ + 0.6f);
                if (Front(rotW))
                {
                    Handles.color = new Color(0.3f, 1f, 0.4f, 0.9f);
                    Line(cp, Proj(rotW));
                    ObstacleHandle(i, 1, Proj(rotW), area, MouseCursor.RotateArrow, new Color(0.3f, 1f, 0.4f, 0.9f));
                }
                // 4 handle GÓC để co/giãn (scale đối xứng qua tâm) — hid 2..5.
                var corners = new[] { c0, c1, c2, c3 };
                for (int k = 0; k < 4; k++)
                    if (Front(corners[k]))
                        ObstacleHandle(i, 2 + k, Proj(corners[k]), area, MouseCursor.ScaleArrow, new Color(0.3f, 0.9f, 1f, 0.9f));
                ObstacleHandle(i, 0, cp, area, MouseCursor.MoveArrow, new Color(1f, 0.5f, 0.9f, 0.9f)); // tâm vẽ sau, nằm trên
            }
            ApplyObstacleDrag();
        }

        private void ObstacleHandle(int i, int hid, Vector2 p, Rect area, MouseCursor cursor, Color col)
        {
            if (!area.Contains(p)) return;
            float hr = hid == 0 ? 7f : 6f;
            var hrect = new Rect(p.x - hr, p.y - hr, hr * 2, hr * 2);
            bool active = _dragObstacle == i && _dragObHandle == hid;
            EditorGUI.DrawRect(hrect, active ? Color.yellow : col);
            EditorGUIUtility.AddCursorRect(hrect, cursor);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && hrect.Contains(e.mousePosition))
            {
                _dragObstacle = i; _dragObHandle = hid; _selObstacle = i;
                if (_target.Obstacles != null && i < _target.Obstacles.Count && _target.Obstacles[i] != null)
                    _dragObStart = _target.Obstacles[i].Pos;
                e.Use();
            }
        }

        // Kéo obstacle: 0 = di chuyển tâm (Shift khóa trục) · 1 = xoay quanh Y · 2..5 = kéo 1 trong 4 GÓC
        // để co/giãn — GÓC ĐỐI DIỆN giữ cố định làm mốc, tâm dịch theo (không giãn đều quanh tâm).
        private void ApplyObstacleDrag()
        {
            if (_dragObstacle < 0 || _so == null) return;
            var obs = _so.FindProperty("Obstacles");
            if (obs == null || _dragObstacle >= obs.arraySize) { _dragObstacle = -1; return; }
            var e = Event.current;
            if (e.type == EventType.MouseDrag)
            {
                Vector3 nw = InverseV(e.mousePosition);
                var el = obs.GetArrayElementAtIndex(_dragObstacle);
                if (_dragObHandle == 0)
                {
                    var pp = el.FindPropertyRelative("Pos");
                    Vector3 old = pp.vector3Value;
                    Vector3 t = new Vector3(nw.x, old.y, nw.z);
                    if (e.shift)
                    {
                        if (Mathf.Abs(nw.x - _dragObStart.x) >= Mathf.Abs(nw.z - _dragObStart.z)) t.z = _dragObStart.z;
                        else t.x = _dragObStart.x;
                    }
                    pp.vector3Value = t;
                }
                else if (_dragObHandle == 1)
                {
                    Vector3 center = el.FindPropertyRelative("Pos").vector3Value;
                    Vector3 v = nw - center; v.y = 0f;
                    if (v.sqrMagnitude > 1e-6f)
                        el.FindPropertyRelative("RotationY").floatValue =
                            Mathf.Repeat(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg, 360f);
                }
                else
                {
                    // Kéo 1 GÓC: GÓC ĐỐI DIỆN giữ cố định làm mốc, góc đang kéo bám chuột → tâm DỊCH theo
                    // (không giãn đều quanh tâm). Dấu góc trong hệ local suy từ hid: 2=c0(−,−) 3=c1(+,−)
                    // 4=c2(+,+) 5=c3(−,+).
                    int hk = _dragObHandle - 2;
                    float sx = (hk == 1 || hk == 2) ? 1f : -1f;
                    float sz = hk >= 2 ? 1f : -1f;
                    var o = _dragObstacle < _target.Obstacles.Count ? _target.Obstacles[_dragObstacle] : null;
                    Vector3 fp = ModelFootprint(o?.Prefab);
                    float fx = Mathf.Max(0.01f, fp.x), fz = Mathf.Max(0.01f, fp.z);

                    var posP = el.FindPropertyRelative("Pos");
                    var sp = el.FindPropertyRelative("Scale");
                    Vector3 center = posP.vector3Value;
                    Vector3 sc = sp.vector3Value; if (sc == Vector3.zero) sc = Vector3.one;
                    float halfX = fx * Mathf.Abs(sc.x) * 0.5f, halfZ = fz * Mathf.Abs(sc.z) * 0.5f;
                    Quaternion rot = Quaternion.Euler(0f, el.FindPropertyRelative("RotationY").floatValue, 0f);
                    Vector3 ax = rot * Vector3.right, az = rot * Vector3.forward;

                    // Mốc = góc đối diện (−sx, −sz) tính theo half HIỆN TẠI → đứng yên khi kéo.
                    Vector3 anchor = center + ax * (-sx * halfX) + az * (-sz * halfZ);
                    Vector3 lv = Quaternion.Inverse(rot) * (nw - anchor); lv.y = 0f; // vector mốc→chuột (local)
                    float newScaleX = Mathf.Clamp(Mathf.Abs(lv.x) / fx, 0.05f, 100f);
                    float newScaleZ = Mathf.Clamp(Mathf.Abs(lv.z) / fz, 0.05f, 100f);
                    float newHalfX = fx * newScaleX * 0.5f, newHalfZ = fz * newScaleZ * 0.5f;
                    // Tâm = mốc + nửa kích thước mới về phía góc đang kéo → góc đối diện vẫn ở anchor.
                    Vector3 newCenter = anchor + ax * (sx * newHalfX) + az * (sz * newHalfZ);

                    Vector3 outScale = sc; outScale.x = newScaleX; outScale.z = newScaleZ;
                    if (outScale.y <= 0f) outScale.y = 1f;
                    sp.vector3Value = outScale;
                    posP.vector3Value = new Vector3(newCenter.x, center.y, newCenter.z);
                }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp) { _dragObstacle = -1; _dragObHandle = -1; e.Use(); }
        }

        // Kích thước model (bounds gộp mọi mesh, đã tính transform con) — footprint mặc định trên map khi
        // Scale = 1, khớp kích thước lúc Instantiate model ở runtime. 1×1×1 nếu chưa gán / không có mesh.
        private static Vector3 ModelFootprint(GameObject prefab)
        {
            if (prefab == null) return Vector3.one;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            bool any = false;
            void Acc(Mesh m, Transform t)
            {
                if (m == null) return;
                var b = m.bounds; var mtx = t.localToWorldMatrix;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = b.center + new Vector3(
                        ((k & 1) == 0 ? -1 : 1) * b.extents.x,
                        ((k & 2) == 0 ? -1 : 1) * b.extents.y,
                        ((k & 4) == 0 ? -1 : 1) * b.extents.z);
                    Vector3 w = mtx.MultiplyPoint3x4(corner);
                    min = Vector3.Min(min, w); max = Vector3.Max(max, w); any = true;
                }
            }
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true)) Acc(mf.sharedMesh, mf.transform);
            foreach (var sm in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Acc(sm.sharedMesh, sm.transform);
            if (!any) return Vector3.one;
            Vector3 size = max - min;
            return new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y), Mathf.Max(0.01f, size.z));
        }

        private static readonly Color SpawnerCol = new Color(1f, 0.6f, 0.1f, 1f);

        /// <summary>
        /// Vẽ hàng đợi cell PHÍA SAU của 1 cell Spawner. Hướng "phía sau" lấy từ chính grid
        /// (CellPosAt(row+1) − CellPosAt(row)) nên đúng cả Arc lẫn Rect, và khớp chỗ runtime nhả cell ra
        /// (xem GridBlockManager.FeedSources). Ghost nhỏ + mờ hơn cell thật, bước ngắn hơn 1 hàng để không
        /// đè lên cell của hàng sâu hơn. Ô cuối là "+" (chỉ hiện khi đang chọn màu) để thêm nhanh.
        /// </summary>
        private void DrawQueueGhosts(int gi, BlockGridData grid, int r, int e, int count, Vector3 wp, int flatIdx,
                                     BlockCellData cell, Rect area,
                                     System.Func<Vector3, Vector2> Proj, System.Func<Vector3, bool> Front,
                                     System.Func<Vector3, float, float> PixSize)
        {
            // SpawnerLine: xếp ghost queue DỌC THEO hướng nhả (đường mà cell sẽ được đẩy ra) — xem trước cả
            // đường. Spawner thường/Spawner8: xếp về phía SAU (hàng sâu hơn) như cũ.
            Vector3 back;
            if (cell.Type == BlockCellType.SpawnerLine)
                back = Quaternion.Euler(0f, cell.SpawnerDirectionAngleZ, 0f) * Vector3.forward;
            else
                back = grid.CellPosAt(r + 1, e, count) - wp;
            back.y = 0f;
            if (back.sqrMagnitude < 1e-6f) return;
            back.Normalize();

            int qn = cell.Queue != null ? cell.Queue.Count : 0;
            int slots = _paintColor != TypeColor.None ? qn + 1 : qn; // slot dư = ô "+"
            if (slots == 0) return;

            float step = Mathf.Max(0.3f, (grid.BlockWidth + grid.Spacing) * 0.75f);
            for (int qi = 0; qi < slots; qi++)
            {
                Vector3 qw = wp + back * step * (qi + 1);
                if (!Front(qw)) continue;
                Vector2 qp = Proj(qw);
                float qsz = Mathf.Max(4f, PixSize(qw, Mathf.Max(0.05f, grid.CellScale.x)) * 0.6f);
                var qr = new Rect(qp.x - qsz * 0.5f, qp.y - qsz * 0.5f, qsz, qsz);

                if (qi < qn)
                {
                    var q = cell.Queue[qi];
                    var col = GlobalConfigManager.ColorOf(q != null ? q.Color : TypeColor.None);
                    col.a = 0.5f; // mờ = block chưa nhả ra, phân biệt với cell thật
                    var ir = RectIntersect(qr, area);
                    if (ir.width > 0f && ir.height > 0f) EditorGUI.DrawRect(ir, col);
                    DrawOutline(qr, IsQueueSelected(gi, flatIdx, qi) ? Color.yellow : SpawnerCol, area);
                }
                else DrawOutline(qr, new Color(1f, 0.6f, 0.1f, 0.5f), area); // ô "+" rỗng

                _hitQueue.Add((qr, gi, flatIdx, qi));
            }
        }

        // Viền 1px quanh ô (đánh dấu cell đang chọn / cell Spawner).
        private static void DrawOutline(Rect r, Color col, Rect clip)
        {
            void Bar(Rect b) { var ir = RectIntersect(b, clip); if (ir.width > 0 && ir.height > 0) EditorGUI.DrawRect(ir, col); }
            Bar(new Rect(r.x, r.y, r.width, 1f));
            Bar(new Rect(r.x, r.yMax - 1f, r.width, 1f));
            Bar(new Rect(r.x, r.y, 1f, r.height));
            Bar(new Rect(r.xMax - 1f, r.y, 1f, r.height));
        }

        // Đầu mũi tên hướng của 1 cell — kéo để xoay SpawnerDirectionAngleZ.
        private void CellRotateHandle(int gi, int flatIndex, Vector3 centerWorld, Vector2 tip, Rect area)
        {
            if (!area.Contains(tip)) return;
            const float hr = 5f;
            var hrect = new Rect(tip.x - hr, tip.y - hr, hr * 2, hr * 2);
            bool active = _dragCellGrid == gi && _dragCellIndex == flatIndex;
            EditorGUI.DrawRect(hrect, active ? Color.yellow : new Color(1f, 1f, 1f, 0.9f));
            EditorGUIUtility.AddCursorRect(hrect, MouseCursor.RotateArrow);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && hrect.Contains(e.mousePosition))
            {
                _dragCellGrid = gi; _dragCellIndex = flatIndex; _dragCellCenter = centerWorld; e.Use();
            }
        }

        /// <summary>
        /// Tô màu cell khi Paint Color != None: click 1 ô, hoặc GIỮ chuột rê qua nhiều ô (brush) — khỏi
        /// phải chấm từng cell. Test cả ĐOẠN chuột đi được trong frame (ClipSegment) chứ không chỉ điểm
        /// cuối, nên rê nhanh cũng không nhảy cóc bỏ sót ô. Paint = None thì nhường HandleMarquee chọn ô.
        /// </summary>
        private void HandleCellPaint(Rect area)
        {
            var e = Event.current;
            if (e.type == EventType.MouseUp) _painting = false;
            if (_paintColor == TypeColor.None && !_eraseMode && !_shootMode) return;

            bool start = e.type == EventType.MouseDown && e.button == 0 && !e.alt && area.Contains(e.mousePosition);
            bool cont = e.type == EventType.MouseDrag && _painting;
            if (!start && !cont) return;

            Vector2 from = start ? e.mousePosition : _lastPaintPos;
            Vector2 to = e.mousePosition;
            _lastPaintPos = to;
            if (start) _painting = true;

            var grids = _so.FindProperty("Grids");
            bool hit = false;
            foreach (var h in _hitCells)
            {
                Vector2 a = from, b = to;
                if (!ClipSegment(ref a, ref b, h.rect)) continue;
                if (h.grid < 0 || h.grid >= grids.arraySize) continue;
                var cells = grids.GetArrayElementAtIndex(h.grid).FindPropertyRelative("Cells");
                if (h.flat < 0 || h.flat >= cells.arraySize) continue;
                var cp = cells.GetArrayElementAtIndex(h.flat);
                var stackP = cp.FindPropertyRelative("BlockStackCt");
                if (_shootMode)
                {
                    // Đặt cờ bắn được/không cho cell (không đụng màu/stack).
                    cp.FindPropertyRelative("Shootable").boolValue = _shootValue;
                }
                else if (_eraseMode)
                {
                    // Xoá = đặt stack 0 → thành LỖ vĩnh viễn (runtime không dựng, không cho dồn/refill vào).
                    stackP.intValue = 0;
                    // Xoá luôn hàng đợi: ô đã thành lỗ thì queue của nó vô nghĩa, để lại chỉ làm lệch cân bằng.
                    var qp = cp.FindPropertyRelative("Queue");
                    if (qp != null) qp.arraySize = 0;
                }
                else
                {
                    cp.FindPropertyRelative("Color").enumValueIndex = (int)_paintColor;
                    // Tô vào ô đã xoá (stack 0) = phục hồi cell với stack = Hole Capacity.
                    if (stackP.intValue <= 0) stackP.intValue = Mathf.Max(1, _target.HoleCapacity);
                }
                hit = true;
            }
            // Đang ERASE mà click (không rê) vào khoảng TRỐNG — không trúng cell nào (grid/handle đã e.Use()
            // trước nên start=false, không lọt xuống đây) → thoát Erase, về None cho tiện (và nuốt click).
            if (start && _eraseMode && !hit) { _eraseMode = false; e.Use(); Repaint(); return; }
            // CHỈ nuốt click khi thực sự trúng cell — để click Paint TRƯỢT cell (vd ô "+" của slot) rơi xuống
            // HandleGunPaint. Marquee vẫn không chạy vì nó tự return khi Paint != None.
            if (hit) { e.Use(); Repaint(); }
        }

        /// <summary>
        /// Ghost hàng đợi của cell Spawner: click trái = tô màu đang chọn, click vào ô "+" ở cuối đuôi =
        /// thêm 1 cell vào hàng đợi, click PHẢI = xoá. Chạy TRƯỚC HandleCellPaint vì ghost nằm đè lên
        /// vùng của hàng sâu hơn — không ưu tiên thì tô trúng cell thật bên dưới.
        /// </summary>
        private void HandleQueuePaint(Rect area)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.alt || !area.Contains(e.mousePosition)) return;
            bool paint = e.button == 0 && _paintColor != TypeColor.None && !_eraseMode;
            // Xoá ô queue: click PHẢI, hoặc click TRÁI khi đang ở chế độ Erase.
            bool del = e.button == 1 || (e.button == 0 && _eraseMode);
            if (!paint && !del) return;

            foreach (var h in _hitQueue)
            {
                if (!h.rect.Contains(e.mousePosition)) continue;
                var grids = _so.FindProperty("Grids");
                if (h.grid < 0 || h.grid >= grids.arraySize) return;
                var cells = grids.GetArrayElementAtIndex(h.grid).FindPropertyRelative("Cells");
                if (h.flat < 0 || h.flat >= cells.arraySize) return;
                var q = cells.GetArrayElementAtIndex(h.flat).FindPropertyRelative("Queue");

                if (del)
                {
                    if (h.qi < 0 || h.qi >= q.arraySize) return;
                    q.DeleteArrayElementAtIndex(h.qi);
                }
                else if (h.qi >= q.arraySize) // ô "+" cuối đuôi → thêm cell mới
                {
                    var it = q.GetArrayElementAtIndex(AddArray(q));
                    it.FindPropertyRelative("Color").enumValueIndex = (int)_paintColor;
                    it.FindPropertyRelative("BlockStackCt").intValue = Mathf.Max(1, _target.HoleCapacity);
                }
                else q.GetArrayElementAtIndex(h.qi).FindPropertyRelative("Color").enumValueIndex = (int)_paintColor;

                e.Use(); Repaint();
                return;
            }
        }

        private void ApplyCellRotateDrag()
        {
            if (_dragCellGrid < 0 || _so == null) return;
            var grids = _so.FindProperty("Grids");
            if (_dragCellGrid >= grids.arraySize) { _dragCellGrid = -1; return; }
            var e = Event.current;
            if (e.type == EventType.MouseDrag)
            {
                var cells = grids.GetArrayElementAtIndex(_dragCellGrid).FindPropertyRelative("Cells");
                if (_dragCellIndex < 0 || _dragCellIndex >= cells.arraySize) { _dragCellGrid = -1; return; }
                Vector3 v = InverseV(e.mousePosition) - _dragCellCenter; v.y = 0f;
                if (v.sqrMagnitude > 1e-6f)
                {
                    float ang = Mathf.Repeat(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg, 360f);
                    cells.GetArrayElementAtIndex(_dragCellIndex).FindPropertyRelative("SpawnerDirectionAngleZ").floatValue = ang;
                }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp) { _dragCellGrid = -1; _dragCellIndex = -1; e.Use(); }
        }

        /// <summary>
        /// Vẽ đường spline + handle waypoint của 1 Spline grid. Waypoint lưu ở LOCAL nên phải qua
        /// Center+Rotation mới ra world; lúc kéo thì làm ngược lại.
        /// </summary>
        private void DrawSplineHandles(int gi, BlockGridData g, Rect area,
                                       System.Func<Vector3, Vector2> Proj, System.Action<Vector2, Vector2> Line)
        {
            if (g.SplineWaypoints == null || g.SplineWaypoints.Count == 0) return;

            var rot = Quaternion.Euler(0f, g.Rotation, 0f);
            Vector3 ToW(Vector3 local) => g.Center + rot * local;

            // Đường tim spline (grid bám theo) — mặc định ẨN, bật ở mục SPLINE.
            if (_showSplineLine)
            {
                var s = RoundedPolylinePath.BuildSamples(g.SplineWaypoints, g.SplineClosed,
                                                         g.SplineCornerRadius, 8, g.SplineStyle);
                if (s != null && s.Length >= 2)
                {
                    Handles.color = new Color(0.8f, 0.4f, 1f, 0.7f);
                    for (int i = 1; i < s.Length; i++) Line(Proj(ToW(s[i - 1])), Proj(ToW(s[i])));
                }
            }

            // Waypoint spline (ô vuông tím) LUÔN hiện trong khung giữa để kéo thả chỉnh đường. (Mũi tên
            // foldout ở panel phải chỉ ẩn/hiện DANH SÁCH waypoint, không ẩn ô kéo ở đây.)
            var e = Event.current;
            for (int i = 0; i < g.SplineWaypoints.Count; i++)
            {
                Vector2 p = Proj(ToW(g.SplineWaypoints[i]));
                if (!area.Contains(p)) continue;
                var hr = new Rect(p.x - 6f, p.y - 6f, 12f, 12f);
                bool active = _dragSplineGrid == gi && _dragSplineWp == i;
                EditorGUI.DrawRect(hr, active ? Color.yellow : new Color(0.8f, 0.4f, 1f, 0.95f));
                EditorGUIUtility.AddCursorRect(hr, MouseCursor.MoveArrow);
                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && hr.Contains(e.mousePosition))
                { _dragSplineGrid = gi; _dragSplineWp = i; e.Use(); }
            }
        }

        private void ApplySplineDrag()
        {
            if (_dragSplineGrid < 0 || _so == null) return;
            var grids = _so.FindProperty("Grids");
            if (_dragSplineGrid >= grids.arraySize) { _dragSplineGrid = -1; return; }

            var e = Event.current;
            if (e.type == EventType.MouseDrag)
            {
                var g = grids.GetArrayElementAtIndex(_dragSplineGrid);
                var wp = g.FindPropertyRelative("SplineWaypoints");
                if (_dragSplineWp < 0 || _dragSplineWp >= wp.arraySize) { _dragSplineGrid = -1; return; }

                // Chuột cho ra toạ độ WORLD → quy ngược về LOCAL của grid để lưu.
                Vector3 world = InverseV(e.mousePosition);
                Vector3 center = g.FindPropertyRelative("Center").vector3Value;
                float rotY = g.FindPropertyRelative("Rotation").floatValue;
                Vector3 local = Quaternion.Euler(0f, -rotY, 0f) * (world - center);

                var prop = wp.GetArrayElementAtIndex(_dragSplineWp);
                prop.vector3Value = new Vector3(local.x, prop.vector3Value.y, local.z);
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp) { _dragSplineGrid = -1; _dragSplineWp = -1; _recomputeGridSides = true; e.Use(); }
        }

        private void HandleWaypointDrag(System.Func<Vector3, Vector2> V, Rect area)
        {
            var wp = _so?.FindProperty("PathWaypoints");
            if (wp == null) return;
            var e = Event.current;
            const float hr = 6f;
            _hitWaypoints.Clear();
            for (int i = 0; i < wp.arraySize; i++)
            {
                Vector2 p = V(wp.GetArrayElementAtIndex(i).vector3Value);
                if (!area.Contains(p)) continue;
                var hrect = new Rect(p.x - hr, p.y - hr, hr * 2, hr * 2);
                bool sel = _selWaypoints.Contains(i);
                EditorGUI.DrawRect(hrect, _dragWaypoint == i ? Color.yellow
                    : sel ? new Color(1f, 0.6f, 0.1f, 0.95f) : new Color(1, 1, 1, 0.85f));
                EditorGUIUtility.AddCursorRect(hrect, MouseCursor.MoveArrow);
                _hitWaypoints.Add((hrect, i)); // để marquee quét chọn
                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && hrect.Contains(e.mousePosition)) { _dragWaypoint = i; e.Use(); }
            }
            if (_dragWaypoint >= 0 && _dragWaypoint < wp.arraySize)
            {
                if (e.type == EventType.MouseDrag)
                {
                    Vector3 nw = InverseV(e.mousePosition);
                    var prop = wp.GetArrayElementAtIndex(_dragWaypoint);
                    Vector3 old = prop.vector3Value;
                    prop.vector3Value = new Vector3(nw.x, old.y, nw.z);
                    e.Use(); Repaint();
                }
                else if (e.type == EventType.MouseUp) { _dragWaypoint = -1; e.Use(); }
            }
        }

        // Căn các waypoint ĐANG CHỌN về 1 đường thẳng: vertical = cùng X (đường DỌC), ngang = cùng Z. Dùng
        // TRUNG BÌNH trục cần căn của các điểm đó.
        private void AlignWaypoints(bool vertical)
        {
            var wp = _so.FindProperty("PathWaypoints");
            if (wp == null) return;
            var idx = new List<int>();
            foreach (var i in _selWaypoints) if (i >= 0 && i < wp.arraySize) idx.Add(i);
            if (idx.Count < 2) return;

            float sum = 0f;
            foreach (var i in idx) { var v = wp.GetArrayElementAtIndex(i).vector3Value; sum += vertical ? v.x : v.z; }
            float avg = sum / idx.Count;
            foreach (var i in idx)
            {
                var p = wp.GetArrayElementAtIndex(i);
                var v = p.vector3Value;
                if (vertical) v.x = avg; else v.z = avg;
                p.vector3Value = v;
            }
        }

        private void HandleZoomPan(Rect area, Vector2 center)
        {
            var e = Event.current;
            if (e.type == EventType.ScrollWheel && area.Contains(e.mousePosition))
            {
                float old = _zoom;
                _zoom = Mathf.Clamp(_zoom * (1f - e.delta.y * 0.05f), 0.1f, 20f);
                Vector2 m = e.mousePosition, baseM = center + (m - _pan - center) / old;
                _pan = m - center - (baseM - center) * _zoom;
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDown && (e.button == 2 || (e.button == 0 && e.alt)) && area.Contains(e.mousePosition)) { _panning = true; e.Use(); }
            else if (_panning && e.type == EventType.MouseDrag) { _pan += e.delta; e.Use(); Repaint(); }
            else if (_panning && e.type == EventType.MouseUp) { _panning = false; e.Use(); }
        }

        private Vector3 InverseV(Vector2 s)
        {
            Vector2 C = _viewContent.center;
            Vector2 baseS = C + (s - _pan - C) / _zoom;
            if (_camMode && _previewCamera != null)
            {
                float vx = (baseS.x - _viewContent.x) / _viewContent.width;
                float vy = (_viewContent.yMax - baseS.y) / _viewContent.height;
                Ray ray = _previewCamera.ViewportPointToRay(new Vector3(vx, vy, 0f));
                if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float e)) return ray.GetPoint(e);
                return Vector3.zero;
            }
            float wx = _viewMin.x + (baseS.x - _viewContent.x - Pad) / _viewScale;
            float wz = _viewMin.y + (_viewContent.yMax - Pad - baseS.y) / _viewScale;
            return new Vector3(wx, 0f, wz);
        }

        private void CollectGridPoints(List<Vector3> pts)
        {
            if (_target.Grids == null) return;
            foreach (var g in _target.Grids)
            {
                if (g == null) continue;
                int last = Mathf.Max(0, g.Rows - 1);
                pts.Add(g.Center);
                pts.Add(g.CellPos(0, 0));
                pts.Add(g.CellPos(0, g.ElementsInRow(0) - 1));
                pts.Add(g.CellPos(last, 0));
                pts.Add(g.CellPos(last, g.ElementsInRow(last) - 1));
            }
        }

        private void CollectSlotPoints(List<Vector3> pts)
        {
            if (_target.Slots == null) return;
            var slotPos = GetSlotBasePositions();
            float spacing = GameSettings.Instance != null ? GameSettings.Instance.SlotGunSpacing : 1f;
            for (int si = 0; si < slotPos.Count && si < _target.Slots.Count; si++)
            {
                var guns = _target.Slots[si]?.Guns;
                if (guns == null) continue;
                Vector3 basePos = slotPos[si];
                for (int i = 0; i < guns.Count; i++) pts.Add(basePos + Vector3.forward * spacing * i);
            }
        }

        #endregion

        #region Right panel

        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical("box", GUILayout.Width(_rightW));
            if (_target == null) { EditorGUILayout.HelpBox("Chưa chọn level.", MessageType.Info); EditorGUILayout.EndVertical(); return; }
            if (_so == null || _so.targetObject != _target) { _so = new SerializedObject(_target); _so.Update(); }

            EditorGUILayout.LabelField("Level: " + _target.name, EditorStyles.boldLabel);
            bool ok0 = _target.ValidateColorBalance(out string rep0);
            EditorGUILayout.HelpBox(rep0, ok0 ? MessageType.Info : MessageType.Warning);

            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            DrawSelectionSection();
            EditorGUILayout.Space(4); DrawMetaSection();
            EditorGUILayout.Space(4); DrawPathSection();
            EditorGUILayout.Space(4); DrawObstaclesSection();
            EditorGUILayout.Space(4); DrawSlotsSection();
            EditorGUILayout.Space(4); DrawGridsSection();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // Đã bỏ khỏi UI (vẫn còn trong LevelData, code chiếu vẫn dùng): CAMERA FRAME (Ortho Size / Aspect /
        // Center) — không cần chỉnh tay nữa vì đang WYSIWYG theo Scene Camera.
        // Tạm ẩn: NumberOfColors, MechanicNames, BoardProps. (Obstacles có section riêng ở panel phải.)
        // Prefabs Gun/Block/Bullet đã bỏ khỏi UI (PoolManager runtime vẫn đọc field trong LevelData —
        // null thì fallback primitive), thay bằng section OBSTACLES.
        private void DrawMetaSection()
        {
            EditorGUILayout.BeginVertical("box");
            _foldMeta = EditorGUILayout.Foldout(_foldMeta, "META", true, EditorStyles.foldoutHeader);
            if (_foldMeta)
            {
                EditorGUILayout.PropertyField(_so.FindProperty("CurGameDifficulty"));
                EditorGUILayout.PropertyField(_so.FindProperty("HoleCapacity"),
                    new GUIContent("Hole Capacity", "Stack mỗi cell khi Generate Cells."));
            }
            EditorGUILayout.EndVertical();
        }

        // Thông số của gun/cell đang click chọn trong khung giữa (chỉ hiện khi Paint Color = None).
        private void DrawSelectionSection()
        {
            if (_selQueue.qi >= 0) { DrawSelectedQueueCell(); return; }
            if (_selGun >= 0) { DrawSelectedGun(); return; }
            if (_selCells.Count == 1) { DrawSelectedCell(_selCells[0].grid, _selCells[0].cell); return; }
            if (_selCells.Count > 1) { DrawMultiCellEdit(); return; }
            if (_paintColor == TypeColor.None)
                EditorGUILayout.HelpBox("Paint = None → click chọn 1 GUN/CELL (kể cả ô mờ hàng đợi Spawner) · "
                    + "KÉO để quét chọn nhiều cell · giữ CTRL để chọn thêm · DELETE để xoá.", MessageType.None);
        }

        // 1 cell trong hàng đợi Spawner (ô mờ phía sau). Chọn bằng click khi Paint = None.
        private void DrawSelectedQueueCell()
        {
            var grids = _so.FindProperty("Grids");
            var q = QueueProp(grids, _selQueue.grid, _selQueue.cell);
            if (q == null || _selQueue.qi >= q.arraySize) { ClearQueueSel(); return; }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"CELL SAU SPAWNER — Grid {_selQueue.grid} · #{_selQueue.cell} · thứ {_selQueue.qi + 1}",
                EditorStyles.boldLabel);
            if (GUILayout.Button("✕", BtnW)) { ClearQueueSel(); EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); return; }
            EditorGUILayout.EndHorizontal();

            var it = q.GetArrayElementAtIndex(_selQueue.qi);
            EditorGUILayout.PropertyField(it.FindPropertyRelative("Color"), new GUIContent("Màu"));
            EditorGUILayout.PropertyField(it.FindPropertyRelative("BlockStackCt"),
                new GUIContent("Số đạn (stack)", "Số block của cell này khi spawner đẩy nó ra — cũng là số " +
                               "đạn cần để phá nó."));

            EditorGUILayout.HelpBox("Ấn DELETE (chuột đang ở khung giữa) để xoá cell này khỏi hàng đợi.",
                MessageType.None);
            if (GUILayout.Button("Xoá cell này"))
            {
                q.DeleteArrayElementAtIndex(_selQueue.qi);
                ClearQueueSel();
            }
            EditorGUILayout.EndVertical();
        }

        // Đặt giá trị CHUNG cho tất cả cell đang quét chọn.
        private void DrawMultiCellEdit()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"CELLS — đang chọn {_selCells.Count} ô", EditorStyles.boldLabel);
            if (GUILayout.Button("✕", BtnW)) _selCells.Clear();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Bấm Áp dụng để set giá trị chung cho toàn bộ ô đang chọn.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            _multiColor = (TypeColor)EditorGUILayout.EnumPopup("Màu", _multiColor);
            if (GUILayout.Button("Áp dụng", GUILayout.Width(70))) ApplyToSelected("Color", (int)_multiColor);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _multiType = (BlockCellType)EditorGUILayout.EnumPopup("Type", _multiType);
            if (GUILayout.Button("Áp dụng", GUILayout.Width(70))) ApplyToSelected("Type", (int)_multiType);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _multiStack = Mathf.Max(1, EditorGUILayout.IntField("Stack", _multiStack));
            if (GUILayout.Button("Áp dụng", GUILayout.Width(70))) ApplyToSelected("BlockStackCt", _multiStack);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _multiShootable = EditorGUILayout.Toggle("Bắn được", _multiShootable);
            if (GUILayout.Button("Áp dụng", GUILayout.Width(70))) ApplyToSelected("Shootable", _multiShootable ? 1 : 0);
            EditorGUILayout.EndHorizontal();

            // Băng phủ cả VÙNG đang chọn (cùng ngưỡng → tan cùng lúc).
            EditorGUILayout.BeginHorizontal();
            _multiIced = EditorGUILayout.Toggle("Meta Ice", _multiIced);
            if (GUILayout.Button("Áp dụng", GUILayout.Width(70))) ApplyToSelected("Iced", _multiIced ? 1 : 0);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            _multiIceThreshold = Mathf.Max(0, EditorGUILayout.IntField("Block Meta Ice ≥", _multiIceThreshold));
            if (GUILayout.Button("Áp dụng", GUILayout.Width(70))) ApplyToSelected("IceThreshold", _multiIceThreshold);
            EditorGUILayout.EndHorizontal();

            // HƯỚNG CHUNG cho MỌI SpawnerLine đang chọn (góc quanh Y: 0=+Z, 90=+X, 180=−Z, 270=−X). Khỏi
            // phải kéo xoay từng ô.
            EditorGUILayout.BeginHorizontal();
            _multiLineDir = EditorGUILayout.FloatField("Hướng Line (°)", _multiLineDir);
            if (GUILayout.Button("Áp dụng", GUILayout.Width(70))) ApplyLineDirection(_multiLineDir);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Nhanh", GUILayout.Width(72));
            if (GUILayout.Button("↑ 0°", EditorStyles.miniButton)) { _multiLineDir = 0f; ApplyLineDirection(0f); }
            if (GUILayout.Button("→ 90°", EditorStyles.miniButton)) { _multiLineDir = 90f; ApplyLineDirection(90f); }
            if (GUILayout.Button("↓ 180°", EditorStyles.miniButton)) { _multiLineDir = 180f; ApplyLineDirection(180f); }
            if (GUILayout.Button("← 270°", EditorStyles.miniButton)) { _multiLineDir = 270f; ApplyLineDirection(270f); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Hướng Line chỉ áp cho ô SpawnerLine đang chọn (đặt chung 1 hướng nhả).", MessageType.None);

            // Thêm N mục queue vào MỌI ô đang chọn LÀ SPAWNER (màu = màu ô đó, stack = Hole Capacity).
            EditorGUILayout.BeginHorizontal();
            _multiQueueCount = Mathf.Clamp(EditorGUILayout.IntField("Queue +", _multiQueueCount), 1, 999);
            if (GUILayout.Button("Thêm queue", GUILayout.Width(90))) AddQueueToSelected(_multiQueueCount);
            if (GUILayout.Button("Clear queue", GUILayout.Width(90))) ClearQueueOfSelected();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Thêm/Clear queue chỉ tác dụng lên ô đang chọn là Spawner/Spawner8/SpawnerLine.", MessageType.None);

            EditorGUILayout.EndVertical();
        }

        // Thêm N mục queue vào mọi ô đang chọn là spawner; màu = màu ô, stack = Hole Capacity.
        private void AddQueueToSelected(int count)
        {
            int defStack = Mathf.Max(1, _target.HoleCapacity);
            var grids = _so.FindProperty("Grids");
            foreach (var (gi, ci) in _selCells)
            {
                if (gi < 0 || gi >= grids.arraySize) continue;
                var cells = grids.GetArrayElementAtIndex(gi).FindPropertyRelative("Cells");
                if (ci < 0 || ci >= cells.arraySize) continue;
                var cell = cells.GetArrayElementAtIndex(ci);
                if (!((BlockCellType)cell.FindPropertyRelative("Type").enumValueIndex).IsSpawner()) continue;
                int color = cell.FindPropertyRelative("Color").enumValueIndex;
                var q = cell.FindPropertyRelative("Queue");
                for (int k = 0; k < count; k++)
                {
                    var e = q.GetArrayElementAtIndex(AddArray(q));
                    e.FindPropertyRelative("Color").enumValueIndex = color;
                    e.FindPropertyRelative("BlockStackCt").intValue = defStack;
                }
            }
        }

        private void ClearQueueOfSelected()
        {
            var grids = _so.FindProperty("Grids");
            foreach (var (gi, ci) in _selCells)
            {
                if (gi < 0 || gi >= grids.arraySize) continue;
                var cells = grids.GetArrayElementAtIndex(gi).FindPropertyRelative("Cells");
                if (ci < 0 || ci >= cells.arraySize) continue;
                cells.GetArrayElementAtIndex(ci).FindPropertyRelative("Queue").arraySize = 0;
            }
        }

        // Đặt HƯỚNG nhả (SpawnerDirectionAngleZ) chung cho MỌI ô đang chọn LÀ SpawnerLine.
        private void ApplyLineDirection(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);
            var grids = _so.FindProperty("Grids");
            foreach (var (gi, ci) in _selCells)
            {
                if (gi < 0 || gi >= grids.arraySize) continue;
                var cells = grids.GetArrayElementAtIndex(gi).FindPropertyRelative("Cells");
                if (ci < 0 || ci >= cells.arraySize) continue;
                var cell = cells.GetArrayElementAtIndex(ci);
                if ((BlockCellType)cell.FindPropertyRelative("Type").enumValueIndex != BlockCellType.SpawnerLine) continue;
                cell.FindPropertyRelative("SpawnerDirectionAngleZ").floatValue = angle;
            }
        }

        private void ApplyToSelected(string propName, int value)
        {
            // Color/BlockStackCt có trên CẢ PendingBlockData của hàng đợi → áp luôn cho các cell queue phía
            // sau spawner (trước đây chỉ đổi cell mặt ngoài, hàng đợi giữ màu/stack cũ). Type chỉ có ở cell
            // thật, không lan xuống queue.
            bool alsoQueue = propName == "Color" || propName == "BlockStackCt";
            var grids = _so.FindProperty("Grids");
            foreach (var (gi, ci) in _selCells)
            {
                if (gi < 0 || gi >= grids.arraySize) continue;
                var cells = grids.GetArrayElementAtIndex(gi).FindPropertyRelative("Cells");
                if (ci < 0 || ci >= cells.arraySize) continue;
                var cell = cells.GetArrayElementAtIndex(ci);
                SetIntOrEnum(cell.FindPropertyRelative(propName), value);
                if (!alsoQueue) continue;

                var q = cell.FindPropertyRelative("Queue");
                if (q == null) continue;
                for (int i = 0; i < q.arraySize; i++)
                    SetIntOrEnum(q.GetArrayElementAtIndex(i).FindPropertyRelative(propName), value);
            }
        }

        private static void SetIntOrEnum(SerializedProperty p, int value)
        {
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = value;
            else if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value != 0;
            else p.intValue = value;
        }

        private void DrawSelectedGun()
        {
            var slots = _so.FindProperty("Slots");
            if (_selSlot < 0 || _selSlot >= slots.arraySize) { _selGun = -1; return; }
            var guns = slots.GetArrayElementAtIndex(_selSlot).FindPropertyRelative("Guns");
            if (_selGun >= guns.arraySize) { _selGun = -1; return; }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"GUN — Slot {_selSlot} · #{_selGun}", EditorStyles.boldLabel);
            if (GUILayout.Button("✕", BtnW)) { _selGun = -1; _selSlot = -1; }
            EditorGUILayout.EndHorizontal();

            var g = guns.GetArrayElementAtIndex(_selGun);
            EditorGUILayout.PropertyField(g.FindPropertyRelative("Color"), new GUIContent("Màu"));
            EditorGUILayout.PropertyField(g.FindPropertyRelative("CountBullet"), new GUIContent("Số lượng đạn"));
            EditorGUILayout.PropertyField(g.FindPropertyRelative("Hidden"),
                new GUIContent("Ẩn màu (hidden)", "Che màu (material hidden) cho tới khi gun ra VỊ TRÍ ĐẦU của slot."));
            EditorGUILayout.PropertyField(g.FindPropertyRelative("ConnectGroup"),
                new GUIContent("Connect nhóm", "0 = không connect. Gun CÙNG số này (ở slot khác) nối với nhau: chỉ "
                    + "deploy khi cả nhóm ở index 0, và cùng vào path 1 lượt (cần đủ chỗ)."));
            EditorGUILayout.HelpBox("Ấn DELETE (chuột ở khung giữa) hoặc CLICK PHẢI vào gun để xoá.", MessageType.None);
            if (GUILayout.Button("Xoá gun này"))
            {
                guns.DeleteArrayElementAtIndex(_selGun);
                _selGun = -1; _selSlot = -1;
            }
            EditorGUILayout.EndVertical();
        }

        // Băng phủ cell: bật Iced + ngưỡng tan (tổng block phá). Đặt CÙNG ngưỡng cho cả 1 vùng để tan cùng lúc,
        // và khớp Melt-At của Obstacle băng đặt đè lên vùng đó.
        private static void DrawIceFields(SerializedProperty c)
        {
            var iced = c.FindPropertyRelative("Iced");
            EditorGUILayout.PropertyField(iced,
                new GUIContent("Băng phủ", "Cell KHÔNG bắn được cho tới khi băng tan. IceController tự sinh khối Ice "
                    + "phủ vùng cell băng liền kề (cùng ngưỡng) khi Play."));
            if (iced.boolValue)
                EditorGUILayout.PropertyField(c.FindPropertyRelative("IceThreshold"),
                    new GUIContent("Tan khi phá ≥", "Tổng block phá trong màn để băng tan (cũng là số countdown "
                        + "ban đầu). Cell liền kề CÙNG ngưỡng gộp thành 1 khối băng."));
        }

        private void DrawSelectedCell(int gridIdx, int cellIdx)
        {
            var grids = _so.FindProperty("Grids");
            if (gridIdx < 0 || gridIdx >= grids.arraySize) { _selCells.Clear(); return; }
            var cells = grids.GetArrayElementAtIndex(gridIdx).FindPropertyRelative("Cells");
            if (cellIdx < 0 || cellIdx >= cells.arraySize) { _selCells.Clear(); return; }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"CELL — Grid {gridIdx} · #{cellIdx}", EditorStyles.boldLabel);
            if (GUILayout.Button("✕", BtnW)) _selCells.Clear();
            EditorGUILayout.EndHorizontal();

            var c = cells.GetArrayElementAtIndex(cellIdx);
            EditorGUILayout.PropertyField(c.FindPropertyRelative("Color"), new GUIContent("Màu"));
            EditorGUILayout.PropertyField(c.FindPropertyRelative("BlockStackCt"), new GUIContent("Stack"));
            EditorGUILayout.PropertyField(c.FindPropertyRelative("Shootable"),
                new GUIContent("Bắn được", "Tắt = gun không bao giờ ngắm cell này (khung giữa vẽ gạch X)."));
            DrawIceFields(c);

            var typeProp = c.FindPropertyRelative("Type");
            EditorGUILayout.PropertyField(typeProp, new GUIContent("Type"));
            var cellType = (BlockCellType)typeProp.enumValueIndex;
            if (cellType == BlockCellType.SpawnerLine || cellType == BlockCellType.Spawner8)
                EditorGUILayout.PropertyField(c.FindPropertyRelative("SpawnerReach"),
                    new GUIContent("Reach (số ô)", cellType == BlockCellType.Spawner8
                        ? "Số ô TỐI ĐA lan ra mỗi hướng (Chebyshev) từ ô gốc (0 = tới mép grid)."
                        : "Số ô tối đa nhả ra dọc theo hướng mũi tên (0 = tới mép grid)."));
            // SpawnerLine: nhập chính xác hướng nhả (hoặc kéo mũi tên trong khung giữa).
            if (cellType == BlockCellType.SpawnerLine)
                EditorGUILayout.PropertyField(c.FindPropertyRelative("SpawnerDirectionAngleZ"),
                    new GUIContent("Hướng nhả (°)", "Góc quanh Y: 0=+Z, 90=+X, 180=−Z, 270=−X. Hoặc kéo đầu mũi tên."));
            if (cellType.IsSpawner()) DrawCellQueue(c);
            else EditorGUILayout.HelpBox("Normal: phá hết stack là cell biến mất.", MessageType.None);
            EditorGUILayout.EndVertical();
        }

        // Hàng đợi cell PHÍA SAU của 1 cell Spawner: phá hết stack hiện tại → đẩy mục kế ra tại chỗ.
        private void DrawCellQueue(SerializedProperty cell)
        {
            var q = cell.FindPropertyRelative("Queue");
            if (q == null) return;

            int total = 0;
            for (int i = 0; i < q.arraySize; i++)
                total += Mathf.Max(0, q.GetArrayElementAtIndex(i).FindPropertyRelative("BlockStackCt").intValue);
            EditorGUILayout.LabelField($"Cell phía sau — {q.arraySize} mục · ∑{total} block", EditorStyles.miniBoldLabel);
            var qType = (BlockCellType)cell.FindPropertyRelative("Type").enumValueIndex;
            EditorGUILayout.HelpBox(
                qType == BlockCellType.Spawner8
                ? "Spawner8: nhả vào ô TRỐNG ở 8 ô xung quanh (dọc/ngang trước, rồi chéo). Nhả MÀU HIỆN TẠI trước rồi tới queue. Block ở đây CÓ tính vào cân bằng bullet↔block."
                : qType == BlockCellType.SpawnerLine
                ? "SpawnerLine: nhả theo 1 ĐƯỜNG dọc/ngang (theo mũi tên) tới tối đa Reach ô. Nhả MÀU HIỆN TẠI trước rồi tới queue (xem trước ghost dọc theo đường). Block ở đây CÓ tính vào cân bằng bullet↔block."
                : "Spawner: phá hết stack hiện tại → đẩy mục kế ra ĐÚNG vị trí này (đổi màu/stack). Hết hàng đợi mới biến mất. Block ở đây CÓ tính vào cân bằng bullet↔block.",
                MessageType.None);

            int pend = -1; ListOp op = ListOp.None;
            for (int i = 0; i < q.arraySize; i++)
            {
                var it = q.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField((i + 1) + ".", GUILayout.Width(20));
                EditorGUILayout.PropertyField(it.FindPropertyRelative("Color"), GUIContent.none, GUILayout.MinWidth(70));
                EditorGUILayout.PropertyField(it.FindPropertyRelative("BlockStackCt"), GUIContent.none, GUILayout.Width(50));
                var o = MiniButtons(i, q.arraySize);
                if (o != ListOp.None) { pend = i; op = o; }
                EditorGUILayout.EndHorizontal();
            }

            // Thêm N mục cùng lúc; MÀU MẶC ĐỊNH = màu ô spawner. Clear = xoá cả queue.
            int spawnerColor = cell.FindPropertyRelative("Color").enumValueIndex;
            EditorGUILayout.BeginHorizontal();
            _queueAddCount = Mathf.Clamp(EditorGUILayout.IntField(_queueAddCount, GUILayout.Width(40)), 1, 999);
            if (GUILayout.Button($"+ {_queueAddCount} cell (màu ô spawner)"))
            {
                int defStack = Mathf.Max(1, _target.HoleCapacity);
                for (int k = 0; k < _queueAddCount; k++)
                {
                    var e = q.GetArrayElementAtIndex(AddArray(q));
                    e.FindPropertyRelative("Color").enumValueIndex = spawnerColor;
                    e.FindPropertyRelative("BlockStackCt").intValue = defStack;
                }
            }
            if (GUILayout.Button("Clear", GUILayout.Width(50))) q.arraySize = 0;
            EditorGUILayout.EndHorizontal();

            // Sửa HÀNG LOẠT cả queue: đặt màu / stack cho MỌI mục đang có.
            EditorGUILayout.BeginHorizontal();
            _queueBulkColor = (TypeColor)EditorGUILayout.EnumPopup(_queueBulkColor, GUILayout.Width(80));
            if (GUILayout.Button("Màu cả queue", GUILayout.Width(100)))
                for (int i = 0; i < q.arraySize; i++)
                    q.GetArrayElementAtIndex(i).FindPropertyRelative("Color").enumValueIndex = (int)_queueBulkColor;
            _queueBulkStack = Mathf.Max(1, EditorGUILayout.IntField(_queueBulkStack, GUILayout.Width(40)));
            if (GUILayout.Button("Stack cả queue", GUILayout.Width(100)))
                for (int i = 0; i < q.arraySize; i++)
                    q.GetArrayElementAtIndex(i).FindPropertyRelative("BlockStackCt").intValue = _queueBulkStack;
            EditorGUILayout.EndHorizontal();

            if (pend >= 0) ApplyOp(q, pend, op);
        }

        // OBSTACLES: list model đặt trên board. "+ Obstacle" thả 1 cái ở tâm view; kéo trên map để
        // di chuyển/xoay/scale (xem DrawObstacles). Kích thước footprint = bounds model × Scale.
        private void DrawObstaclesSection()
        {
            var obs = _so.FindProperty("Obstacles");
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            _foldObstacles = EditorGUILayout.Foldout(_foldObstacles, $"OBSTACLES — {obs.arraySize}", true, EditorStyles.foldoutHeader);
            if (GUILayout.Button("+ Obstacle", GUILayout.Width(84))) AddObstacle(obs);
            EditorGUILayout.EndHorizontal();
            if (!_foldObstacles) { EditorGUILayout.EndVertical(); return; }

            EditorGUILayout.HelpBox("Bấm + Obstacle để thả 1 model lên map (ở giữa khung nhìn). Trên map: kéo "
                + "TÂM (hồng) để di chuyển · handle XANH LÁ để xoay · kéo 1 trong 4 GÓC (xanh dương) để co/giãn. "
                + "Chưa gán Model thì footprint = 1×1; gán vào là bằng kích thước model.", MessageType.None);

            int pend = -1; ListOp op = ListOp.None;
            for (int i = 0; i < obs.arraySize; i++)
            {
                var el = obs.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                var prefab = el.FindPropertyRelative("Prefab").objectReferenceValue;
                bool selNow = _selObstacle == i;
                var bg = GUI.backgroundColor;
                GUI.backgroundColor = selNow ? new Color(1f, 0.85f, 0.4f) : bg;
                if (GUILayout.Toggle(selNow, new GUIContent("◉", "Chọn obstacle này (viền vàng trên map)."),
                        EditorStyles.miniButton, GUILayout.Width(24)) != selNow)
                    _selObstacle = selNow ? -1 : i;
                GUI.backgroundColor = bg;
                EditorGUILayout.LabelField($"#{i} · {(prefab != null ? prefab.name : "(chưa có model)")}", EditorStyles.boldLabel);
                var o = MiniButtons(i, obs.arraySize);
                if (o != ListOp.None) { pend = i; op = o; }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(el.FindPropertyRelative("Prefab"), new GUIContent("Model"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("Pos"), new GUIContent("Vị trí"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("RotationY"), new GUIContent("Xoay Y (°)"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("Scale"), new GUIContent("Scale"));
                if (GUILayout.Button("Reset Scale = 1 (kích thước model)"))
                    el.FindPropertyRelative("Scale").vector3Value = Vector3.one;
                EditorGUILayout.PropertyField(el.FindPropertyRelative("Type"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("Strength"));
                EditorGUILayout.EndVertical();
            }
            if (pend >= 0)
            {
                ApplyOp(obs, pend, op);
                if (_selObstacle == pend && op == ListOp.Delete) _selObstacle = -1;
            }
            EditorGUILayout.EndVertical();
        }

        // Thả 1 obstacle mới ở TÂM khung nhìn hiện tại (InverseV của center) — rơi đúng chỗ đang xem.
        private void AddObstacle(SerializedProperty obs)
        {
            int idx = obs.arraySize; obs.arraySize++;
            var el = obs.GetArrayElementAtIndex(idx);
            Vector3 at = _viewContent.width > 0f ? InverseV(_viewContent.center) : Vector3.zero;
            at.y = 0f;
            el.FindPropertyRelative("Prefab").objectReferenceValue = null;
            el.FindPropertyRelative("Pos").vector3Value = at;
            el.FindPropertyRelative("RotationY").floatValue = 0f;
            el.FindPropertyRelative("Scale").vector3Value = Vector3.one;
            el.FindPropertyRelative("Type").enumValueIndex = 0;
            el.FindPropertyRelative("TargetCellIndex").intValue = -1;
            el.FindPropertyRelative("Strength").intValue = 1;
            _selObstacle = idx;
            _foldObstacles = true;
        }

        private void DrawPathSection()
        {
            EditorGUILayout.BeginVertical("box");
            _foldPath = EditorGUILayout.Foldout(_foldPath, "PATH SHAPE (riêng level)", true, EditorStyles.foldoutHeader);
            if (!_foldPath) { EditorGUILayout.EndVertical(); return; }

            EditorGUILayout.PropertyField(_so.FindProperty("IsClosed"));
            EditorGUILayout.PropertyField(_so.FindProperty("PathStyle"), new GUIContent("Path Style"));
            // CornerRadius chỉ có tác dụng với RoundedCorner — Bezier bỏ qua nó hoàn toàn.
            using (new EditorGUI.DisabledScope(_target.PathStyle != PathStyle.RoundedCorner))
                EditorGUILayout.PropertyField(_so.FindProperty("CornerRadius"));
            if (!_target.IsClosed)
                EditorGUILayout.HelpBox("Path HỞ → PathManager sinh TunnelIn ở đầu và TunnelOut ở cuối "
                    + "(gán prefab trên PathManager trong scene).", MessageType.None);
            EditorGUILayout.HelpBox($"Path Width = {PathWidthShared:0.##} (DÙNG CHUNG — chỉnh ở GAME SETTINGS panel trái).", MessageType.None);

            if (GUILayout.Button(new GUIContent("Xóa cell grid trùng path",
                "Xoá (stack = 0 → lỗ) mọi cell của các grid nằm ĐÈ lên mặt đường (trong Path Width). Dùng để " +
                "dọn chỗ path chạy qua grid.")))
                DeleteCellsOverlappingPath();

            // Quét chọn waypoint trong khung giữa (ô CAM) rồi CĂN thẳng dọc/ngang cho các điểm đó.
            if (_selWaypoints.Count >= 2)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Đã chọn {_selWaypoints.Count} waypoint — căn:", GUILayout.Width(180));
                if (GUILayout.Button(new GUIContent("Dọc |", "Cùng X → xếp thành đường DỌC (thẳng đứng).")))
                    AlignWaypoints(true);
                if (GUILayout.Button(new GUIContent("Ngang —", "Cùng Z → xếp thành đường NGANG.")))
                    AlignWaypoints(false);
                if (GUILayout.Button("✕", BtnW)) _selWaypoints.Clear();
                EditorGUILayout.EndHorizontal();
            }

            var wp = _so.FindProperty("PathWaypoints");
            _foldWaypoints = EditorGUILayout.Foldout(_foldWaypoints, $"Waypoints — {wp.arraySize}", true);
            if (_foldWaypoints)
            {
                int pend = -1; ListOp op = ListOp.None;
                for (int i = 0; i < wp.arraySize; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(wp.GetArrayElementAtIndex(i), new GUIContent("WP " + i));
                    var o = MiniButtons(i, wp.arraySize);
                    if (o != ListOp.None) { pend = i; op = o; }
                    EditorGUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Waypoint"))
                {
                    int idx = wp.arraySize; wp.arraySize++;
                    wp.GetArrayElementAtIndex(idx).vector3Value = idx > 0 ? wp.GetArrayElementAtIndex(idx - 1).vector3Value + Vector3.right : Vector3.zero;
                }
                if (pend >= 0) ApplyOp(wp, pend, op);
            }
            EditorGUILayout.EndVertical();
        }

        // Path Width DÙNG CHUNG mọi level — đọc từ GameSettings (fallback 1.5 nếu chưa có asset).
        private static float PathWidthShared => GameSettings.Instance != null ? GameSettings.Instance.PathWidth : 1.5f;

        // Xoá mọi cell của các grid nằm ĐÈ lên mặt đường path (tâm cell cách đường ≤ nửa Path Width + nửa
        // kích thước cell). Đặt stack = 0 → thành lỗ, giống thao tác Delete/Erase.
        private void DeleteCellsOverlappingPath()
        {
            var samples = _target.PathWaypoints != null && _target.PathWaypoints.Count >= 2
                ? RoundedPolylinePath.BuildSamples(_target.PathWaypoints, _target.IsClosed,
                                                   _target.CornerRadius, 8, _target.PathStyle)
                : null;
            if (samples == null || samples.Length < 2)
            {
                EditorUtility.DisplayDialog("Xóa cell trùng path", "Path cần ít nhất 2 waypoint.", "OK");
                return;
            }

            float halfW = Mathf.Max(0f, PathWidthShared) * 0.5f;
            var grids = _so.FindProperty("Grids");
            int removed = 0;
            for (int gi = 0; gi < _target.Grids.Count && gi < grids.arraySize; gi++)
            {
                var grid = _target.Grids[gi];
                if (grid == null) continue;
                var cells = grids.GetArrayElementAtIndex(gi).FindPropertyRelative("Cells");
                // Dùng nửa ĐƯỜNG CHÉO của cell (≈ 0.707 × cạnh) làm tầm với: cell chỉ cần lẹm 1 GÓC vào
                // dải path (không cần tâm nằm trong) là đã bị xóa. Lấy cạnh = max(BlockWidth, CellScale) cho
                // chắc (BlockWidth = bước xếp, CellScale = scale hiển thị block).
                float cellSize = Mathf.Max(0.05f, Mathf.Max(grid.BlockWidth, grid.CellScale.x));
                float thr = halfW + cellSize * 0.7071f;
                for (int r = 0; r < grid.Rows; r++)
                {
                    int count = grid.ElementsInRow(r);
                    for (int e = 0; e < count; e++)
                    {
                        var cd = grid.GetCell(r, e);
                        if (cd == null || cd.BlockStackCt <= 0) continue;
                        if (DistToPath(grid.CellPos(r, e), samples) > thr) continue;
                        int idx = grid.CellIndex(r, e);
                        if (idx < 0 || idx >= cells.arraySize) continue;
                        cells.GetArrayElementAtIndex(idx).FindPropertyRelative("BlockStackCt").intValue = 0;
                        removed++;
                    }
                }
            }
            Debug.Log($"[LevelTool] Đã xóa {removed} cell trùng path.");
        }

        // Khoảng cách (trên sàn XZ) từ điểm tới polyline path (min qua mọi đoạn mẫu).
        private static float DistToPath(Vector3 p, Vector3[] samples)
        {
            p.y = 0f;
            float best = float.MaxValue;
            for (int i = 0; i + 1 < samples.Length; i++)
            {
                Vector3 a = samples[i]; a.y = 0f;
                Vector3 b = samples[i + 1]; b.y = 0f;
                Vector3 ab = b - a;
                float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector3.Distance(p, a + ab * t));
            }
            return best;
        }

        private void DrawSlotsSection()
        {
            var slots = _so.FindProperty("Slots");
            EditorGUILayout.BeginHorizontal();
            _foldSlots = EditorGUILayout.Foldout(_foldSlots, $"SLOTS — {slots.arraySize}", true, EditorStyles.foldoutHeader);
            if (GUILayout.Button("+ Slot", GUILayout.Width(58))) slots.GetArrayElementAtIndex(AddArray(slots)).FindPropertyRelative("Guns").arraySize = 0;
            EditorGUILayout.EndHorizontal();
            if (!_foldSlots) return;

            // LOẠI MAP theo số slot (2..5). Đổi loại → tự co/giãn Slots đúng số đó (thêm slot rỗng / cắt bớt).
            var scProp = _so.FindProperty("SlotCount");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Loại map (số slot)",
                "MapController sẽ spawn map prefab tương ứng số slot này; vị trí slot do prefab quyết định."),
                GUILayout.Width(130));
            for (int n = MapController.MinSlots; n <= MapController.MaxSlots; n++)
            {
                bool sel = scProp.intValue == n;
                if (GUILayout.Toggle(sel, n.ToString(), EditorStyles.miniButton, GUILayout.Width(30)) && !sel)
                {
                    scProp.intValue = n;
                    int old = slots.arraySize;
                    slots.arraySize = n;                                   // co/giãn về đúng n slot
                    for (int k = 0; k < n; k++)
                    {
                        var guns = slots.GetArrayElementAtIndex(k).FindPropertyRelative("Guns");
                        // Slot MỚI (hoặc slot cũ đang rỗng) → mặc định 1 gun ĐẦU màu ĐỎ (5 đạn). Slot cũ đã có
                        // gun thì giữ nguyên. (Slot mới do resize copy phần tử cuối nên phải set lại rõ ràng.)
                        if (k >= old || guns.arraySize == 0)
                        {
                            guns.arraySize = 1;
                            var g0 = guns.GetArrayElementAtIndex(0);
                            g0.FindPropertyRelative("Color").enumValueIndex = (int)_slotGenColor;
                            g0.FindPropertyRelative("CountBullet").intValue = 5;
                        }
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // Màu gun để SINH vào slot: dùng cho gun mặc định khi chọn số slot, và nút "+ Gun mọi slot".
            EditorGUILayout.BeginHorizontal();
            _slotGenColor = (TypeColor)EditorGUILayout.EnumPopup("Màu gun sinh", _slotGenColor);
            if (GUILayout.Button(new GUIContent("+ Gun mọi slot", "Thêm 1 gun màu này (5 đạn) vào cuối MỌI slot."),
                                 GUILayout.Width(120)))
                for (int k = 0; k < slots.arraySize; k++)
                {
                    var guns = slots.GetArrayElementAtIndex(k).FindPropertyRelative("Guns");
                    var g = guns.GetArrayElementAtIndex(AddArray(guns));
                    g.FindPropertyRelative("Color").enumValueIndex = (int)_slotGenColor;
                    g.FindPropertyRelative("CountBullet").intValue = 5;
                }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("Vị trí slot lấy từ script Map trên map prefab (list mốc — mỗi mốc = nơi sinh "
                + "GUN 0 của 1 slot, theo loại số slot ở trên). Gán 4 map prefab trên MapController trong scene, "
                + "mỗi prefab có component Map. Chưa có thì fallback GunSlot đặt sẵn scene. Khoảng cách gun theo GameSettings.\n"
                + "Khung giữa: chọn Paint Color rồi CLICK ô \"+\" sau đuôi slot để THÊM gun màu đó (như thêm cell vào "
                + "queue) · click gun cũ để ĐỔI MÀU · CLICK PHẢI vào gun để XOÁ · click gun (Paint=None) để chọn/sửa · DELETE để xoá.",
                MessageType.None);

            int pend = -1; ListOp op = ListOp.None;
            for (int i = 0; i < slots.arraySize; i++)
            {
                var guns = slots.GetArrayElementAtIndex(i).FindPropertyRelative("Guns");
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                bool open = FoldAt(_foldSlot, i, $"Slot {i} — {guns.arraySize} guns");
                var o = MiniButtons(i, slots.arraySize);
                if (o != ListOp.None) { pend = i; op = o; }
                EditorGUILayout.EndHorizontal();
                if (open) DrawGunList(guns);
                EditorGUILayout.EndVertical();
            }
            if (pend >= 0) ApplyOp(slots, pend, op);
        }

        private void DrawGunList(SerializedProperty guns)
        {
            EditorGUILayout.LabelField($"Guns — thứ tự ({guns.arraySize})", EditorStyles.miniBoldLabel);
            int pend = -1; ListOp op = ListOp.None;
            for (int i = 0; i < guns.arraySize; i++)
            {
                var g = guns.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField((i + 1) + ".", GUILayout.Width(20));
                EditorGUILayout.PropertyField(g.FindPropertyRelative("Color"), GUIContent.none, GUILayout.MinWidth(70));
                EditorGUILayout.PropertyField(g.FindPropertyRelative("CountBullet"), GUIContent.none, GUILayout.Width(50));
                var o = MiniButtons(i, guns.arraySize);
                if (o != ListOp.None) { pend = i; op = o; }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ Gun")) guns.GetArrayElementAtIndex(AddArray(guns)).FindPropertyRelative("CountBullet").intValue = 5;
            if (pend >= 0) ApplyOp(guns, pend, op);
        }

        private void DrawGridsSection()
        {
            var grids = _so.FindProperty("Grids");
            EditorGUILayout.BeginHorizontal();
            _foldGrids = EditorGUILayout.Foldout(_foldGrids, $"GRIDS — {grids.arraySize}", true, EditorStyles.foldoutHeader);
            // Arc đã bỏ khỏi tool — chỉ tạo mới Rect / Spline.
            int gsi = EditorGUILayout.Popup(_genShape == BlockGridShape.Spline ? 1 : 0,
                                            ToolShapeNames, GUILayout.Width(70));
            _genShape = gsi == 1 ? BlockGridShape.Spline : BlockGridShape.Rect;
            if (GUILayout.Button("+ Grid", GUILayout.Width(58))) AddGrid(grids);
            EditorGUILayout.EndHorizontal();
            if (!_foldGrids) return;
            EditorGUILayout.HelpBox("Chọn loại grid (Rect = lưới chữ nhật, Spline = dải uốn lượn) rồi bấm + Grid.\nRect — mỗi handle chỉ đổi 1 trục: TÂM (hồng) di chuyển · 2 ĐẦU CẠNH TRƯỚC (xanh dương) kéo NGANG = SỐ CỘT · 2 GÓC SAU (teal) kéo SÂU = SỐ HÀNG · XOAY (xanh lá). Row 0 = hàng gần path.\nClick ô cell = tô màu đang chọn ở Paint Color · kéo đầu mũi tên = xoay hướng cell.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            _genColor = (TypeColor)EditorGUILayout.EnumPopup("Gen Color", _genColor);
            _genStack = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Refill Stack", "Stack cho hàng đợi Refill (Generate Cells dùng Hole Capacity)."), _genStack, GUILayout.Width(120)));
            EditorGUILayout.EndHorizontal();

            DrawPaintPalette();

            int pend = -1; ListOp op = ListOp.None;
            for (int i = 0; i < grids.arraySize; i++)
            {
                var grid = grids.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                int rows = grid.FindPropertyRelative("Rows").intValue;
                // Đọc THẲNG từ object thay vì duyệt SerializedProperty: nhãn foldout tính lại mỗi frame cho
                // MỌI grid, mà GetArrayElementAtIndex/FindPropertyRelative đắt gấp bội đọc list thường →
                // grid nhiều cell là giật khi repaint (kéo handle). Lệch 1 frame với data không sao (chỉ là nhãn).
                var gdata = _target != null && _target.Grids != null && i < _target.Grids.Count ? _target.Grids[i] : null;
                int gridBlocks = SumStackData(gdata?.Cells);
                int pendBlocks = SumStackPending(gdata?.PendingRefill);
                string blkTxt = pendBlocks > 0 ? $"{gridBlocks}(+{pendBlocks})" : gridBlocks.ToString();
                bool openGrid = FoldAt(_foldGrid, i, $"Grid {i} — {rows} rows · {blkTxt} block");
                // Nút chọn grid để SỬA: bật thì chỉ grid này nhận click/tô/xóa/quét trong khung giữa.
                bool wasActive = _activeGrid == i;
                var bg = GUI.backgroundColor;
                GUI.backgroundColor = wasActive ? new Color(0.4f, 1f, 0.5f) : bg;
                bool nowActive = GUILayout.Toggle(wasActive, new GUIContent("✎",
                    "Chọn grid này để SỬA — chỉ grid đang chọn mới nhận click/tô/xóa/quét (grid chồng khác bị khóa). " +
                    "Bấm lại để bỏ (cho sửa mọi grid)."), EditorStyles.miniButton, GUILayout.Width(24));
                GUI.backgroundColor = bg;
                if (nowActive != wasActive) _activeGrid = nowActive ? i : -1;
                var o = MiniButtons(i, grids.arraySize);
                if (o != ListOp.None) { pend = i; op = o; }
                EditorGUILayout.EndHorizontal();
                if (!openGrid) { EditorGUILayout.EndVertical(); continue; }

                var shapeProp = grid.FindPropertyRelative("Shape");
                bool isRect = shapeProp.enumValueIndex == (int)BlockGridShape.Rect;
                bool isSpline = shapeProp.enumValueIndex == (int)BlockGridShape.Spline;
                bool isArc = shapeProp.enumValueIndex == (int)BlockGridShape.Arc;
                EditorGUILayout.BeginHorizontal();
                // Arc đã bỏ: grid mới chỉ Rect/Spline. Grid CŨ kiểu Arc hiện "Arc (đã bỏ)" + cho chuyển sang
                // Rect/Spline, KHÔNG tự convert nếu user không chọn (BeginChangeCheck).
                if (isArc)
                {
                    EditorGUI.BeginChangeCheck();
                    int ci = EditorGUILayout.Popup("Shape", 0, new[] { "Arc (đã bỏ)", "Rect", "Spline" });
                    if (EditorGUI.EndChangeCheck() && ci > 0)
                        shapeProp.enumValueIndex = (int)(ci == 2 ? BlockGridShape.Spline : BlockGridShape.Rect);
                }
                else
                {
                    int nsi = EditorGUILayout.Popup("Shape", isSpline ? 1 : 0, ToolShapeNames);
                    shapeProp.enumValueIndex = (int)(nsi == 1 ? BlockGridShape.Spline : BlockGridShape.Rect);
                }
                EditorGUILayout.PropertyField(grid.FindPropertyRelative("Side"), new GUIContent("Side",
                    "Left/Right = chỉ nòng cùng bên của gun bắn được grid này. Any = theo quạt gun."));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.PropertyField(grid.FindPropertyRelative("ShootableEdges"), new GUIContent(
                    "Shootable Edges",
                    "Các CẠNH gun bắn được khi path bao quanh grid. None = mặc định cũ (chỉ mặt Front/hàng 0). " +
                    "Bật Front/Back/Left/Right để phá từ nhiều phía; đặt Spawner8 ở giữa làm nguồn bất tử."));
                EditorGUILayout.PropertyField(grid.FindPropertyRelative("Collapse2D"), new GUIContent(
                    "Collapse 2D",
                    "Dồn cả DỌC (về hàng 0) lẫn NGANG (về index 0) → lấp lỗ 2 chiều về góc. Chỉ khi KHÔNG bật Shootable Edges."));
                EditorGUILayout.PropertyField(grid.FindPropertyRelative("Center"));
                EditorGUILayout.PropertyField(grid.FindPropertyRelative("Rotation"),
                    new GUIContent("Rotation (Y°)", "Xoay cả grid quanh trục Y. Kéo handle XANH LÁ trong khung giữa."));
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(grid.FindPropertyRelative("BaseRadius"),
                    new GUIContent(isRect ? "Dist Row 0" : isSpline ? "Lệch Row 0" : "Base Radius",
                        isSpline ? "Khoảng cách từ đường spline tới hàng 0, theo pháp tuyến." : null));
                EditorGUILayout.PropertyField(grid.FindPropertyRelative("RowSpacing"), new GUIContent("Row Spacing"));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(grid.FindPropertyRelative("Rows"), new GUIContent("Rows"));
                // ArcAngle / Columns / SpiralGrowth vô nghĩa với Spline — hình dạng do waypoint quyết định.
                if (isRect) EditorGUILayout.PropertyField(grid.FindPropertyRelative("Columns"), new GUIContent("Columns"));
                else if (!isSpline) EditorGUILayout.PropertyField(grid.FindPropertyRelative("ArcAngle"), new GUIContent("Arc Angle"));
                EditorGUILayout.EndHorizontal();
                if (!isRect && !isSpline)
                    EditorGUILayout.PropertyField(grid.FindPropertyRelative("SpiralGrowth"), new GUIContent("Spiral Growth (xoắn)"));
                if (isSpline) DrawSplineSection(grid, i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(grid.FindPropertyRelative("BlockWidth"), new GUIContent("Block W"));
                EditorGUILayout.PropertyField(grid.FindPropertyRelative("Spacing"), new GUIContent("Spacing"));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.PropertyField(grid.FindPropertyRelative("CellScale"),
                    new GUIContent("Cell Scale", "Scale của mọi BLOCK trong grid này."));
                if (!isRect) EditorGUILayout.PropertyField(grid.FindPropertyRelative("Layout"), new GUIContent("Layout"));
                EditorGUILayout.HelpBox(DescribeLayout(i), MessageType.None);

                if (GUILayout.Button($"Generate Cells (Gen Color · stack = Hole Capacity {Mathf.Max(1, _target.HoleCapacity)})")) GenerateGridCells(i);
                EditorGUILayout.PropertyField(grid.FindPropertyRelative("Cells"), new GUIContent($"Cells ({grid.FindPropertyRelative("Cells").arraySize})"), true);

                EditorGUILayout.Space(2);
                DrawRefillQueue(grid, i);
                EditorGUILayout.EndVertical();
            }
            if (pend >= 0) ApplyOp(grids, pend, op);
        }

        #endregion

        #region Grid ops

        // Đường uốn lượn mà Spline grid bám theo. Waypoint là toạ độ LOCAL so với Center + Rotation.
        private void DrawSplineSection(SerializedProperty grid, int gridIndex)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("SPLINE — đường grid bám theo", EditorStyles.miniBoldLabel);

            var styleProp = grid.FindPropertyRelative("SplineStyle");
            EditorGUILayout.PropertyField(grid.FindPropertyRelative("SplineClosed"), new GUIContent("Khép kín"));
            EditorGUILayout.PropertyField(styleProp, new GUIContent("Style"));
            using (new EditorGUI.DisabledScope(styleProp.enumValueIndex != (int)PathStyle.RoundedCorner))
                EditorGUILayout.PropertyField(grid.FindPropertyRelative("SplineCornerRadius"),
                    new GUIContent("Corner Radius"));

            // Mũi tên (foldout) ẩn/hiện Ở PANEL NÀY. "Đường spline" gate vẽ đường tim trong khung giữa; "Waypoints"
            // chỉ ẩn/hiện DANH SÁCH toạ độ ở đây — ô vuông tím kéo thả trong khung giữa LUÔN hiện.
            _showSplineLine = EditorGUILayout.Foldout(_showSplineLine, "Đường spline (đường grid bám theo)", true);
            if (_showSplineLine)
                EditorGUILayout.HelpBox("Đang HIỆN đường tim spline (tím) trong khung giữa. Bấm mũi tên để ẩn.",
                    MessageType.None);

            var wp = grid.FindPropertyRelative("SplineWaypoints");
            _showSplineWaypoints = EditorGUILayout.Foldout(_showSplineWaypoints, $"Waypoints — {wp.arraySize}", true);
            if (_showSplineWaypoints)
            {
                EditorGUILayout.HelpBox("Kéo ô VUÔNG TÍM trong khung giữa để chỉnh đường. Cần ít nhất 2 waypoint "
                    + "thì grid mới hiện.", MessageType.None);
                int pend = -1; ListOp op = ListOp.None;
                for (int i = 0; i < wp.arraySize; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(wp.GetArrayElementAtIndex(i), new GUIContent("WP " + i));
                    var o = MiniButtons(i, wp.arraySize);
                    if (o != ListOp.None) { pend = i; op = o; }
                    EditorGUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Waypoint"))
                {
                    int idx = AddArray(wp);
                    // Nối tiếp waypoint trước cho khỏi đè lên nhau; rỗng thì bắt đầu tại gốc local.
                    wp.GetArrayElementAtIndex(idx).vector3Value = idx > 0
                        ? wp.GetArrayElementAtIndex(idx - 1).vector3Value + Vector3.right * 2f
                        : Vector3.zero;
                }
                if (pend >= 0) ApplyOp(wp, pend, op);
            }
            EditorGUILayout.EndVertical();
        }

        private void AddGrid(SerializedProperty grids)
        {
            int idx = grids.arraySize; grids.arraySize++;
            var g = grids.GetArrayElementAtIndex(idx);
            bool spline = _genShape == BlockGridShape.Spline;
            bool rect = _genShape == BlockGridShape.Rect;

            g.FindPropertyRelative("Shape").enumValueIndex = (int)_genShape;
            g.FindPropertyRelative("Center").vector3Value = Vector3.zero;
            g.FindPropertyRelative("Rotation").floatValue = 0f;
            g.FindPropertyRelative("Rows").intValue = 3;
            g.FindPropertyRelative("Columns").intValue = 5;
            g.FindPropertyRelative("ArcAngle").floatValue = 90f;
            g.FindPropertyRelative("SpiralGrowth").floatValue = 0f;
            g.FindPropertyRelative("CellScale").vector3Value = Vector3.one;
            g.FindPropertyRelative("Layout").enumValueIndex = (int)BlockGridLayout.ArcLength;
            g.FindPropertyRelative("Cells").arraySize = 0;

            // Default riêng theo shape: Rect = BlockW 0 · Dist Row 0 = 0 · RowSpacing 1 · Spacing 1;
            // Spline (dải uốn lượn); còn lại Arc.
            g.FindPropertyRelative("BaseRadius").floatValue = spline || rect ? 0f : 3f;   // Dist/Lệch Row 0
            g.FindPropertyRelative("RowSpacing").floatValue = rect ? 1f : spline ? 1f : 1.2f;
            g.FindPropertyRelative("BlockWidth").floatValue = spline || rect ? 0f : 0.8f;
            g.FindPropertyRelative("Spacing").floatValue = rect ? 1f : spline ? 0.8f : 0.2f;

            var scr = g.FindPropertyRelative("SplineCornerRadius");
            if (scr != null) scr.floatValue = 5f;

            // Spline: sinh sẵn 2 waypoint để có 1 đoạn đường ngay.
            var wp = g.FindPropertyRelative("SplineWaypoints");
            if (wp != null)
            {
                wp.arraySize = spline ? 2 : 0;
                if (spline)
                {
                    wp.GetArrayElementAtIndex(0).vector3Value = Vector3.zero;
                    wp.GetArrayElementAtIndex(1).vector3Value = new Vector3(4f, 0f, 0f);
                }
            }

            _recomputeGridSides = true; // grid mới → tính Left/Right theo path ngay khi serialized được áp
        }

        // Hàng đợi SPAWNER refill: mỗi mục = 1 stack (màu + số block) sẽ được nhả bù ở ring ngoài cùng
        // khi front bị thu hết. "+ Refill Ring" nạp nguyên 1 vòng (= số phần tử hàng ngoài cùng).
        private void DrawRefillQueue(SerializedProperty grid, int gridIndex)
        {
            var pending = grid.FindPropertyRelative("PendingRefill");
            if (pending == null) return;

            int total = 0;
            for (int i = 0; i < pending.arraySize; i++)
                total += Mathf.Max(0, pending.GetArrayElementAtIndex(i).FindPropertyRelative("BlockStackCt").intValue);

            EditorGUILayout.LabelField($"Refill Queue (spawner) — {pending.arraySize} mục · ∑{total} block", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox("Ring front bị thu hết → collapse → spawner nhả 1 ring mới ở NGOÀI CÙNG, lấy lần lượt từ hàng đợi này. Block ở đây CÓ tính vào cân bằng bullet↔block.", MessageType.None);

            int pend = -1; ListOp op = ListOp.None;
            for (int i = 0; i < pending.arraySize; i++)
            {
                var it = pending.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField((i + 1) + ".", GUILayout.Width(20));
                EditorGUILayout.PropertyField(it.FindPropertyRelative("Color"), GUIContent.none, GUILayout.MinWidth(70));
                EditorGUILayout.PropertyField(it.FindPropertyRelative("BlockStackCt"), GUIContent.none, GUILayout.Width(50));
                var o = MiniButtons(i, pending.arraySize);
                if (o != ListOp.None) { pend = i; op = o; }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Refill"))
            {
                var e = pending.GetArrayElementAtIndex(AddArray(pending));
                e.FindPropertyRelative("Color").enumValueIndex = (int)_genColor;
                e.FindPropertyRelative("BlockStackCt").intValue = _genStack;
            }
            if (GUILayout.Button("+ Refill Ring")) AddRefillRing(gridIndex);
            if (GUILayout.Button("Clear")) pending.arraySize = 0;
            EditorGUILayout.EndHorizontal();

            if (pend >= 0) ApplyOp(pending, pend, op);
        }

        // Nạp nguyên 1 vòng vào hàng đợi: số mục = số phần tử hàng NGOÀI CÙNG (theo chiều dài cung).
        private void AddRefillRing(int gridIndex)
        {
            _so.ApplyModifiedProperties(); // sync field vừa sửa để ElementsInRow tính đúng
            if (gridIndex < 0 || gridIndex >= _target.Grids.Count) return;
            var g = _target.Grids[gridIndex];
            int outerRow = Mathf.Max(0, g.Rows - 1);
            int count = Mathf.Max(1, g.ElementsInRow(outerRow));

            _so.Update();
            var pending = _so.FindProperty("Grids").GetArrayElementAtIndex(gridIndex).FindPropertyRelative("PendingRefill");
            for (int k = 0; k < count; k++)
            {
                var e = pending.GetArrayElementAtIndex(AddArray(pending));
                e.FindPropertyRelative("Color").enumValueIndex = (int)_genColor;
                e.FindPropertyRelative("BlockStackCt").intValue = _genStack;
            }
        }

        // Bảng màu tô cell: None (mặc định) = click cell để chọn/xem thông số. Chọn 1 màu → click cell trong
        // khung giữa sẽ tô cell đó. Danh sách màu lấy từ GlobalConfigManager (chỉ hiện màu đã cấu hình).
        private void DrawPaintPalette()
        {
            var cfg = GlobalConfigManager.Instance;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Paint Color", GUILayout.Width(72));
            if (GUILayout.Toggle(_paintColor == TypeColor.None && !_eraseMode && !_shootMode, "None", EditorStyles.miniButton, GUILayout.Width(44)))
            { _paintColor = TypeColor.None; _eraseMode = false; _shootMode = false; }

            var bg = GUI.backgroundColor;
            if (cfg != null && cfg.listColor != null)
                foreach (var co in cfg.listColor)
                {
                    if (co == null || co.typeColor == TypeColor.None) continue;
                    bool sel = _paintColor == co.typeColor && !_eraseMode && !_shootMode;
                    GUI.backgroundColor = GlobalConfigManager.ColorOf(co.typeColor);
                    if (GUILayout.Toggle(sel, sel ? "●" : "", EditorStyles.miniButton, GUILayout.Width(24)) && !sel)
                    { _paintColor = co.typeColor; _eraseMode = false; _shootMode = false; }
                }
            GUI.backgroundColor = bg;
            // Erase: bật lên thì click/rê vào block là xoá (thành lỗ). Tô màu và Erase loại trừ nhau.
            GUI.backgroundColor = _eraseMode ? new Color(1f, 0.4f, 0.4f) : bg;
            if (GUILayout.Toggle(_eraseMode, "Erase", EditorStyles.miniButton, GUILayout.Width(48)) != _eraseMode)
            { _eraseMode = !_eraseMode; if (_eraseMode) { _paintColor = TypeColor.None; _shootMode = false; } }
            GUI.backgroundColor = bg;
            EditorGUILayout.EndHorizontal();

            // Shoot paint: tô cờ "bắn được / không bắn được" cho cell bằng chuột.
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Shoot", GUILayout.Width(72));
            GUI.backgroundColor = _shootMode ? new Color(0.4f, 0.9f, 1f) : bg;
            if (GUILayout.Toggle(_shootMode, new GUIContent("Shoot paint",
                "Bật rồi click/rê vào cell để đặt cờ Bắn được theo lựa chọn bên phải. Cell KHÔNG bắn được vẽ gạch X."),
                EditorStyles.miniButton, GUILayout.Width(80)) != _shootMode)
            { _shootMode = !_shootMode; if (_shootMode) { _paintColor = TypeColor.None; _eraseMode = false; } }
            GUI.backgroundColor = bg;
            using (new EditorGUI.DisabledScope(!_shootMode))
            {
                if (GUILayout.Toggle(_shootMode && !_shootValue, "Không bắn", EditorStyles.miniButton, GUILayout.Width(70))) _shootValue = false;
                if (GUILayout.Toggle(_shootMode && _shootValue, "Bắn được", EditorStyles.miniButton, GUILayout.Width(70))) _shootValue = true;
            }
            EditorGUILayout.EndHorizontal();

            if (cfg == null)
            {
                EditorGUILayout.HelpBox("Không tìm thấy GlobalConfigManager → không có bảng màu.", MessageType.Warning);
                return;
            }
            EditorGUILayout.HelpBox(_shootMode
                ? $"Đang SHOOT PAINT ({(_shootValue ? "Bắn được" : "KHÔNG bắn")}): click/rê vào cell để đặt cờ. Cell không bắn được vẽ gạch X (gun bỏ qua, không tính vào điều kiện thắng)."
                : _eraseMode
                ? "Đang ERASE: click vào block để XOÁ (stack = 0 → lỗ), hoặc GIỮ CHUỘT RÊ qua nhiều block để xoá cả vùng. Tô màu vào lại để phục hồi."
                : _paintColor == TypeColor.None
                ? "Paint = None: click cell/gun để XEM & SỬA thông số (không đổi màu) · kéo chuột = quét chọn nhiều cell."
                : $"Đang tô màu {_paintColor} — click, hoặc GIỮ CHUỘT RÊ qua nhiều cell để tô cả vùng. "
                  + "Ô đã XOÁ hiện dạng khung mờ; tô vào đó sẽ PHỤC HỒI cell (stack = Hole Capacity).\n"
                  + (_showQueue
                      ? "Cell Spawner: các ô mờ phía sau = hàng đợi · click ô mờ để tô · click ô \"+\" cuối đuôi "
                        + "để thêm cell · CLICK PHẢI vào ô mờ để xoá. (Tắt nút Queue trên toolbar để ẩn.)"
                      : "Hàng đợi Spawner đang ẩn (nút Queue trên toolbar đang tắt) — bật lại để sửa bằng chuột."),
                MessageType.None);
        }

        // Mô tả số cell từng hàng + kiểu chặn, để thấy ngay cột có thẳng hay không.
        private string DescribeLayout(int index)
        {
            if (index < 0 || index >= _target.Grids.Count) return "";
            var g = _target.Grids[index];
            var sb = new System.Text.StringBuilder();
            sb.Append("Cell/hàng: ");
            for (int r = 0; r < Mathf.Max(1, g.Rows); r++) sb.Append(g.ElementsInRow(r)).Append(r < g.Rows - 1 ? " / " : "");
            if (g.Shape == BlockGridShape.Rect)
                sb.Append("\nRect: lưới chữ nhật, mọi hàng = Columns → cột THẲNG, cell sau bị đúng 1 cell trước chặn.");
            else
                sb.Append(g.Layout == BlockGridLayout.Uniform
                    ? "\nArc/Uniform: mọi hàng bằng nhau → cột THẲNG, cell sau bị đúng 1 cell trước chặn."
                    : "\nArc/ArcLength: hàng ra xa nhiều cell hơn → cột LỆCH, cell giữa bị 2 cell trước chặn.");

            if (g.IsFullRing)
                sb.Append("\n★ VÒNG KÍN (ArcAngle 360): mỗi hàng là 1 vòng tròn quanh Center, Rows = số vòng "
                        + "lồng nhau. Nên để Layout = Uniform — ArcLength cho mỗi vòng số cell khác nhau, "
                        + "map dồn hàng không vòng qua được mối nối.");
            else if (g.Shape == BlockGridShape.Arc && g.SpiralGrowth > 0f)
                sb.Append("\n★ XOẮN ỐC (SpiralGrowth > 0): bán kính lớn dần dọc theo sweep. Để ArcAngle > 360 "
                        + "là cuộn nhiều vòng.");
            return sb.ToString();
        }

        private void GenerateGridCells(int index)
        {
            _so.ApplyModifiedProperties(); // sync các field vừa sửa
            if (index < 0 || index >= _target.Grids.Count) return;
            var grid = _target.Grids[index];
            int stack = Mathf.Max(1, _target.HoleCapacity); // stack mỗi cell = Hole Capacity của level

            var cells = _so.FindProperty("Grids").GetArrayElementAtIndex(index).FindPropertyRelative("Cells");
            cells.arraySize = grid.TotalCells(); // tính theo chiều dài cong
            int i = 0;
            for (int r = 0; r < grid.Rows; r++)
            {
                int count = grid.ElementsInRow(r);
                for (int el = 0; el < count; el++)
                {
                    var c = cells.GetArrayElementAtIndex(i++);
                    c.FindPropertyRelative("Color").enumValueIndex = (int)_genColor;
                    c.FindPropertyRelative("BlockStackCt").intValue = stack;
                    c.FindPropertyRelative("CellScale").vector3Value = Vector3.one;
                    c.FindPropertyRelative("BlockCol").intValue = el;   // index cột trong hàng
                    c.FindPropertyRelative("SpawnerDepth").intValue = r; // 0 = hàng sát path
                    // Mặc định MỌI cell sinh ra đều BẮN ĐƯỢC. Phải set rõ: mảng nới qua SerializedProperty
                    // khởi tạo phần tử mới bằng default(bool)=false, KHÔNG theo field initializer (= true) của
                    // BlockCellData → không set thì cả grid vừa Generate lại thành "không bắn được".
                    c.FindPropertyRelative("Shootable").boolValue = true;
                    // Hướng dồn: Rect = mọi cell chung 1 hướng của grid; Arc = từng cell về tâm.
                    c.FindPropertyRelative("SpawnerDirectionAngleZ").floatValue = grid.DefaultCellAngle(r, el);
                }
            }
        }

        #endregion

        #region Level list ops

        private void Select(LevelData lv) { _target = lv; _so = null; _activeGrid = -1; if (lv != null) EditorGUIUtility.PingObject(lv); Repaint(); }

        private void RefreshLevels()
        {
            _levels.Clear();
            string folder = AssetDatabase.IsValidFolder(_levelsFolder) ? _levelsFolder : "Assets";
            foreach (var guid in AssetDatabase.FindAssets("t:LevelData", new[] { folder }))
            {
                var lv = AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(guid));
                if (lv != null) _levels.Add(lv);
            }
        }

        private void CreateNewLevel()
        {
            string folder = AssetDatabase.IsValidFolder(_levelsFolder) ? _levelsFolder : "Assets";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Level{_levels.Count + 1}.asset");
            var asset = CreateInstance<LevelData>();
            asset.GunPrefab = _defaultGunPrefab; asset.BlockPrefab = _defaultBlockPrefab;
            AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssets();
            // Tự thêm vào LevelList để level mới hiện luôn trong list gộp (không phải Fill from folder lại).
            var list = LevelList.Instance;
            if (list != null) { Undo.RecordObject(list, "Add Level"); list.Levels.Add(asset); EditorUtility.SetDirty(list); AssetDatabase.SaveAssets(); }
            _listSO = null;
            RefreshLevels(); Select(asset);
        }

        private void AddPreviewToScene()
        {
            var go = new GameObject("LevelPreview_" + _target.name);
            go.AddComponent<LevelPreview>().level = _target;
            Undo.RegisterCreatedObjectUndo(go, "Add Level Preview");
            Selection.activeGameObject = go;
        }

        private void CreateGameSettings()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            var asset = CreateInstance<GameSettings>();
            AssetDatabase.CreateAsset(asset, "Assets/Resources/GameSettings.asset"); AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(asset);
        }

        // Tạo 1 MapController rỗng trên scene để gán 4 map prefab (2/3/4/5 slot). Không tạo trùng.
        private void CreateMapController()
        {
            var mc = FindObjectOfType<MapController>();
            if (mc == null)
            {
                var go = new GameObject("MapController");
                Undo.RegisterCreatedObjectUndo(go, "Create MapController");
                mc = go.AddComponent<MapController>();
            }
            Selection.activeGameObject = mc.gameObject;
            EditorGUIUtility.PingObject(mc.gameObject);
        }

        // Tạo 1 IceController rỗng trên scene để gán prefab Ice (băng tự sinh phủ vùng cell Iced).
        private void CreateIceController()
        {
            var ic = FindObjectOfType<IceController>();
            if (ic == null)
            {
                var go = new GameObject("IceController");
                Undo.RegisterCreatedObjectUndo(go, "Create IceController");
                ic = go.AddComponent<IceController>();
            }
            Selection.activeGameObject = ic.gameObject;
            EditorGUIUtility.PingObject(ic.gameObject);
        }

        private void CreateSceneSlots()
        {
            var mgr = FindObjectOfType<SlotManager>();
            if (mgr == null) { var go = new GameObject("SlotManager"); Undo.RegisterCreatedObjectUndo(go, "Create SlotManager"); mgr = go.AddComponent<SlotManager>(); }
            var so = new SerializedObject(mgr);
            var arr = so.FindProperty("sceneSlots");
            arr.arraySize = 5;
            for (int i = 0; i < 5; i++)
            {
                var go = new GameObject("Slot" + i);
                Undo.RegisterCreatedObjectUndo(go, "Create Slot");
                go.transform.SetParent(mgr.transform);
                go.transform.position = new Vector3((i - 2) * 1.2f, 0f, -3f);
                var slot = go.AddComponent<GunSlot>(); slot.SlotIndex = i;
                arr.GetArrayElementAtIndex(i).objectReferenceValue = slot;
            }
            so.ApplyModifiedProperties();
            Selection.activeGameObject = mgr.gameObject;
        }

        #endregion

        #region Helpers

        private static int AddArray(SerializedProperty arr) { int idx = arr.arraySize; arr.arraySize++; return idx; }

        // Tổng BlockStackCt của 1 mảng phần tử (Cells hoặc PendingRefill — đều có field BlockStackCt).
        // Đọc thẳng object (nhanh hơn nhiều SerializedProperty) cho nhãn foldout tính mỗi frame.
        private static int SumStackData(List<BlockCellData> cells)
        {
            if (cells == null) return 0;
            int s = 0;
            foreach (var c in cells) if (c != null) s += Mathf.Max(0, c.BlockStackCt);
            return s;
        }

        private static int SumStackPending(List<PendingBlockData> pend)
        {
            if (pend == null) return 0;
            int s = 0;
            foreach (var p in pend) if (p != null) s += Mathf.Max(0, p.BlockStackCt);
            return s;
        }

        private ListOp MiniButtons(int index, int count)
        {
            ListOp op = ListOp.None;
            using (new EditorGUI.DisabledScope(index <= 0)) if (GUILayout.Button("↑", BtnW)) op = ListOp.MoveUp;
            using (new EditorGUI.DisabledScope(index >= count - 1)) if (GUILayout.Button("↓", BtnW)) op = ListOp.MoveDown;
            if (GUILayout.Button("⧉", BtnW)) op = ListOp.Duplicate;
            if (GUILayout.Button("✕", BtnW)) op = ListOp.Delete;
            return op;
        }

        private void ApplyOp(SerializedProperty list, int i, ListOp op)
        {
            switch (op)
            {
                case ListOp.MoveUp: list.MoveArrayElement(i, i - 1); break;
                case ListOp.MoveDown: list.MoveArrayElement(i, i + 1); break;
                case ListOp.Duplicate: list.InsertArrayElementAtIndex(i); break;
                case ListOp.Delete:
                    // Mảng OBJECT REFERENCE (vd LevelList.Levels): lần gọi đầu chỉ set phần tử = null,
                    // phải gọi lần 2 mới rút nó ra. Mảng class [Serializable] thì 1 lần là đủ.
                    var el = list.GetArrayElementAtIndex(i);
                    if (el.propertyType == SerializedPropertyType.ObjectReference && el.objectReferenceValue != null)
                        list.DeleteArrayElementAtIndex(i);
                    list.DeleteArrayElementAtIndex(i);
                    break;
            }
        }

        private List<GunSlot> GetSceneSlots()
        {
            var list = new List<GunSlot>(Object.FindObjectsOfType<GunSlot>(true));
            list.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
            return list;
        }

        // Vị trí GỐC (nơi sinh gun 0) từng slot để PREVIEW: ưu tiên các mốc trong script Map của map prefab
        // theo SlotCount (đúng chỗ map sẽ sinh lúc Play, cộng transform MapController trên scene nếu có).
        // Fallback: GunSlot đặt sẵn scene.
        private List<Vector3> GetSlotBasePositions()
        {
            var result = new List<Vector3>();
            var mc = Object.FindObjectOfType<MapController>();
            if (mc != null && _target != null)
            {
                var prefab = mc.PrefabFor(_target.SlotCount);
                var map = prefab != null ? prefab.GetComponent<Map>() : null;
                if (map != null && map.SlotSpawns != null)
                {
                    foreach (var t in map.SlotSpawns)
                        if (t != null) result.Add(mc.transform.TransformPoint(t.position));
                    if (result.Count > 0) return result;
                }
            }
            foreach (var s in GetSceneSlots()) result.Add(s.transform.position);
            return result;
        }

        // Điểm trên path theo arc-length — chạy trên ĐƯỜNG BO GÓC (_pathSamples) đúng như runtime, không
        // phải đoạn thẳng nối waypoint. Wrap khoảng cách bất kể IsClosed, khớp GetPointAtDistance.
        private Vector3 PathPointAt(float dist)
        {
            var s = _pathSamples;
            if (s == null || s.Length == 0) return Vector3.zero;
            if (s.Length == 1) return s[0];

            float total = 0f;
            for (int i = 1; i < s.Length; i++) total += Vector3.Distance(s[i - 1], s[i]);
            if (total < 1e-4f) return s[0];

            dist = Mathf.Repeat(dist, total);
            float acc = 0f;
            for (int i = 1; i < s.Length; i++)
            {
                float len = Vector3.Distance(s[i - 1], s[i]);
                if (acc + len >= dist) return Vector3.Lerp(s[i - 1], s[i], len > 1e-4f ? (dist - acc) / len : 0f);
                acc += len;
            }
            return s[s.Length - 1];
        }

        private static bool ClipSegment(ref Vector2 a, ref Vector2 b, Rect r)
        {
            float xmin = r.xMin, xmax = r.xMax, ymin = r.yMin, ymax = r.yMax;
            int Code(Vector2 p) { int c = 0; if (p.x < xmin) c |= 1; else if (p.x > xmax) c |= 2; if (p.y < ymin) c |= 4; else if (p.y > ymax) c |= 8; return c; }
            int ca = Code(a), cb = Code(b);
            for (int guard = 0; guard < 16; guard++)
            {
                if ((ca | cb) == 0) return true;
                if ((ca & cb) != 0) return false;
                int co = ca != 0 ? ca : cb;
                Vector2 p = Vector2.zero;
                if ((co & 8) != 0) { p.x = a.x + (b.x - a.x) * (ymax - a.y) / (b.y - a.y); p.y = ymax; }
                else if ((co & 4) != 0) { p.x = a.x + (b.x - a.x) * (ymin - a.y) / (b.y - a.y); p.y = ymin; }
                else if ((co & 2) != 0) { p.y = a.y + (b.y - a.y) * (xmax - a.x) / (b.x - a.x); p.x = xmax; }
                else { p.y = a.y + (b.y - a.y) * (xmin - a.x) / (b.x - a.x); p.x = xmin; }
                if (co == ca) { a = p; ca = Code(a); } else { b = p; cb = Code(b); }
            }
            return true;
        }

        private static Rect RectIntersect(Rect a, Rect b)
        {
            float x = Mathf.Max(a.xMin, b.xMin), y = Mathf.Max(a.yMin, b.yMin);
            float xM = Mathf.Min(a.xMax, b.xMax), yM = Mathf.Min(a.yMax, b.yMax);
            return new Rect(x, y, Mathf.Max(0f, xM - x), Mathf.Max(0f, yM - y));
        }

        #endregion
    }
}
