using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardGame.ActionQueue.Editor
{
    public sealed class ActionQueueDebuggerWindow : EditorWindow
    {
        private const float DetailsWidth = 315f;
        private const float TreeIndent = 20f;
        private const float TreeRowHeight = 28f;
        private const string StyleSheetPath = "Assets/TurnBasedPack/ActionQueue/Unity/Editor/ActionQueueDebuggerWindow.uss";

        private readonly List<ActionQueueRunner> runners = new();
        private readonly Dictionary<long, bool> foldouts = new();
        private readonly HashSet<long> stepBaseline = new();
        private readonly HashSet<long> newNodeIds = new();
        private readonly HashSet<long> currentNodeIds = new();
        private readonly HashSet<long> visibleNodeIds = new();
        private readonly List<ActionQueueDebugNode> pendingNodeBuffer = new();
        private readonly Stack<ActionQueueDebugNode> traversalStack = new();
        private readonly List<bool> ancestorLineBuffer = new();
        private readonly List<ActionTypeEntry> actionTypes = new();
        private readonly List<ActionTypeEntry> filteredActionTypes = new();
        private readonly Dictionary<ActionTypeFilter, Button> actionTypeFilterButtons = new();
        private readonly Dictionary<DebuggerPage, Button> pageButtons = new();

        private ActionQueueRunner runner;
        private Vector2 overviewScroll;
        private Vector2 environmentScroll;
        private Vector2 treeScroll;
        private Vector2 detailsScroll;
        private long selectedNodeId;
        private long observedChainId;
        private bool observedHasChain;
        private bool waitingForStepResult;
        private bool debugBindingEnabled;
        private bool snapshotDirty = true;
        private long observedDebugVersion = -1;
        private double nextRunnerRefresh;
        private double nextSnapshotFallback;
        private ActionQueueDebugService boundDebugger;
        private IDisposable recordingLease;
        private ActionQueueDebugSnapshot cachedSnapshot;
        private ActionQueueDebugSnapshot searchSnapshot;
        private GUIStyle rightAlignedMiniLabel;
        private GUIStyle badgeStyle;
        private SearchField treeSearch;
        private string treeSearchText = string.Empty;
        private string treeSearchQuery = string.Empty;
        private string typeSearchQuery = string.Empty;
        private ListView actionTypeList;
        private Label actionTypeEmptyState;
        private ToolbarSearchField typeSearch;
        private IMGUIContainer runtimeDebugger;
        private VisualElement pageContent;
        private Label pageTitle;
        private Label pageSubtitle;
        private Label playModeBadge;
        private Label runnerBadge;
        private Label actionTypeBadge;
        private DebuggerPage currentPage = DebuggerPage.Overview;
        private ActionTypeFilter currentActionTypeFilter = ActionTypeFilter.All;

        [MenuItem("Tools/ActionChainWeaver/Debug Window")]
        public static void Open() => GetWindow<ActionQueueDebuggerWindow>("ActionChainWeaver Debug");

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            pageButtons.Clear();
            StyleSheet styleSheet = LoadStyleSheet();
            if (styleSheet != null && !rootVisualElement.styleSheets.Contains(styleSheet))
                rootVisualElement.styleSheets.Add(styleSheet);
            rootVisualElement.AddToClassList("aq-root");
            rootVisualElement.EnableInClassList("aq-light", !EditorGUIUtility.isProSkin);

            var split = new TwoPaneSplitView(0, 208f, TwoPaneSplitViewOrientation.Horizontal)
            {
                viewDataKey = "action-queue-debugger-split"
            };
            split.AddToClassList("aq-shell");
            split.Add(BuildNavigation());
            split.Add(BuildWorkspace());
            rootVisualElement.Add(split);
            RefreshActionTypeCatalog();
            ShowPage(currentPage);
            UpdateNavigationBadges();
        }

        private VisualElement BuildNavigation()
        {
            var navigation = new VisualElement();
            navigation.AddToClassList("aq-navigation");
            var brand = new VisualElement();
            brand.AddToClassList("aq-brand");
            var brandIcon = new Label("AQ");
            brandIcon.AddToClassList("aq-brand-icon");
            brand.Add(brandIcon);
            var brandText = new VisualElement();
            brandText.Add(new Label("ACTION QUEUE") { name = "brand-title" });
            brandText.Add(new Label("Debugger") { name = "brand-subtitle" });
            brand.Add(brandText);
            navigation.Add(brand);

            var menu = new ScrollView();
            menu.AddToClassList("aq-menu");
            AddNavigationSection(menu, "运行监控");
            AddNavigationItem(menu, DebuggerPage.Overview, "▦", "队列概览", "运行状态与待处理项");
            AddNavigationItem(menu, DebuggerPage.ExecutionChain, "⌘", "执行链", "Action / Reactor 因果树");
            AddNavigationSection(menu, "项目结构");
            AddNavigationItem(menu, DebuggerPage.ActionTypes, "◇", "Action 类型", "TypeCache 静态目录");
            AddNavigationSection(menu, "帮助");
            AddNavigationItem(menu, DebuggerPage.Guide, "?", "阅读指南", "筛选层级与图例");
            navigation.Add(menu);

            var footer = new VisualElement();
            footer.AddToClassList("aq-navigation-footer");
            playModeBadge = new Label();
            playModeBadge.AddToClassList("aq-status-pill");
            runnerBadge = new Label();
            runnerBadge.AddToClassList("aq-footer-detail");
            footer.Add(playModeBadge);
            footer.Add(runnerBadge);
            navigation.Add(footer);
            return navigation;
        }

        private VisualElement BuildWorkspace()
        {
            var workspace = new VisualElement();
            workspace.AddToClassList("aq-workspace");
            var header = new VisualElement();
            header.AddToClassList("aq-page-header");
            var titles = new VisualElement();
            titles.AddToClassList("aq-page-titles");
            pageTitle = new Label();
            pageTitle.AddToClassList("aq-page-title");
            pageSubtitle = new Label();
            pageSubtitle.AddToClassList("aq-page-subtitle");
            titles.Add(pageTitle);
            titles.Add(pageSubtitle);
            header.Add(titles);
            actionTypeBadge = new Label();
            actionTypeBadge.AddToClassList("aq-header-badge");
            header.Add(actionTypeBadge);
            workspace.Add(header);
            pageContent = new VisualElement();
            pageContent.AddToClassList("aq-page-content");
            workspace.Add(pageContent);
            return workspace;
        }

        private void AddNavigationSection(VisualElement parent, string title)
        {
            var label = new Label(title.ToUpperInvariant());
            label.AddToClassList("aq-menu-section");
            parent.Add(label);
        }

        private void AddNavigationItem(VisualElement parent, DebuggerPage page, string icon, string title, string subtitle)
        {
            var button = new Button(() => ShowPage(page));
            button.AddToClassList("aq-menu-item");
            var iconLabel = new Label(icon);
            iconLabel.AddToClassList("aq-menu-icon");
            button.Add(iconLabel);
            var copy = new VisualElement();
            copy.AddToClassList("aq-menu-copy");
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("aq-menu-title");
            var subtitleLabel = new Label(subtitle);
            subtitleLabel.AddToClassList("aq-menu-subtitle");
            copy.Add(titleLabel);
            copy.Add(subtitleLabel);
            button.Add(copy);
            parent.Add(button);
            pageButtons[page] = button;
        }

        private void ShowPage(DebuggerPage page)
        {
            currentPage = page;
            foreach (KeyValuePair<DebuggerPage, Button> pair in pageButtons)
                pair.Value.EnableInClassList("is-selected", pair.Key == page);
            actionTypeList = null;
            typeSearch = null;
            actionTypeEmptyState = null;
            actionTypeFilterButtons.Clear();
            runtimeDebugger = null;
            pageContent.Clear();
            switch (page)
            {
                case DebuggerPage.Overview:
                    SetPageHeading("队列概览", "观察当前 Runner、队列工作集和实际调度分类");
                    AddRuntimePage(DrawOverviewPage);
                    break;
                case DebuggerPage.ExecutionChain:
                    SetPageHeading("完整执行链", "沿 Action 与 Reactor 的父子关系定位执行路径");
                    AddRuntimePage(DrawExecutionChainPage);
                    break;
                case DebuggerPage.ActionTypes:
                    SetPageHeading("Action 类型目录", "项目中可创建的逻辑 Action；不统计运行时实例");
                    pageContent.Add(BuildTypeCatalogPage());
                    break;
                case DebuggerPage.Guide:
                    SetPageHeading("阅读指南", "快速理解节点、筛选层和系统边界");
                    pageContent.Add(BuildGuidePage());
                    break;
            }
            UpdateNavigationBadges();
        }

        private void AddRuntimePage(Action drawHandler)
        {
            runtimeDebugger = new IMGUIContainer(drawHandler) { name = "runtime-debugger" };
            runtimeDebugger.AddToClassList("aq-runtime-canvas");
            pageContent.Add(runtimeDebugger);
        }

        private void SetPageHeading(string title, string subtitle)
        {
            pageTitle.text = title;
            pageSubtitle.text = subtitle;
        }

        private VisualElement BuildTypeCatalogPage()
        {
            var panel = new VisualElement();
            panel.AddToClassList("aq-card");
            var toolbar = new Toolbar();
            toolbar.AddToClassList("aq-type-toolbar");
            toolbar.Add(new ToolbarButton(RefreshActionTypeCatalog) { text = "刷新目录" });
            var hint = new Label("搜索名称、类型、分类");
            hint.AddToClassList("aq-type-search-hint");
            toolbar.Add(hint);
            typeSearch = new ToolbarSearchField();
            typeSearch.value = typeSearchQuery;
            typeSearch.style.flexGrow = 1;
            typeSearch.style.minWidth = 180f;
            typeSearch.style.maxWidth = 360f;
            typeSearch.tooltip = "搜索名称、类型、分类";
            typeSearch.RegisterValueChangedCallback(change =>
            {
                typeSearchQuery = change.newValue?.Trim() ?? string.Empty;
                ApplyTypeFilter();
            });
            toolbar.Add(typeSearch);
            toolbar.Add(new ToolbarButton(ClearTypeSearch) { text = "清空" });
            panel.Add(toolbar);

            var filterTabs = new VisualElement();
            filterTabs.AddToClassList("aq-type-tabs");
            actionTypeFilterButtons.Clear();
            AddActionTypeFilterTab(filterTabs, ActionTypeFilter.All, "全部");
            AddActionTypeFilterTab(filterTabs, ActionTypeFilter.Command, "Command");
            AddActionTypeFilterTab(filterTabs, ActionTypeFilter.Composite, "Composite");
            AddActionTypeFilterTab(filterTabs, ActionTypeFilter.Signal, "Signal");
            panel.Add(filterTabs);

            actionTypeList = new ListView
            {
                itemsSource = filteredActionTypes,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                fixedItemHeight = 80f,
                selectionType = SelectionType.Single,
                makeItem = MakeActionTypeRow,
                bindItem = BindActionTypeRow
            };
            actionTypeList.AddToClassList("aq-type-list");
            panel.Add(actionTypeList);
            actionTypeEmptyState = new Label("没有符合当前分类和搜索条件的 Action 类型");
            actionTypeEmptyState.AddToClassList("aq-type-empty");
            panel.Add(actionTypeEmptyState);
            ApplyTypeFilter();
            return panel;
        }

        private static VisualElement MakeActionTypeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("aq-type-row");
            var header = new VisualElement();
            header.AddToClassList("aq-type-row-header");
            header.Add(new Label { name = "type-display" });
            header.Add(new Label { name = "type-kind" });
            row.Add(header);
            row.Add(new Label { name = "type-full-name" });
            row.Add(new Label { name = "type-category" });
            return row;
        }

        private void BindActionTypeRow(VisualElement element, int index)
        {
            ActionTypeEntry entry = filteredActionTypes[index];
            Label display = element.Q<Label>("type-display");
            Label kind = element.Q<Label>("type-kind");
            Label fullName = element.Q<Label>("type-full-name");
            Label category = element.Q<Label>("type-category");
            display.text = entry.DisplayName;
            kind.text = entry.Kind;
            fullName.text = entry.FullName;
            category.text = entry.Category;
            element.tooltip = $"{entry.DisplayName}\n{entry.FullName}\n{entry.Kind}\n{entry.Category}";
            fullName.tooltip = entry.FullName;
            category.tooltip = entry.Category;
        }

        private void ClearTypeSearch()
        {
            typeSearchQuery = string.Empty;
            if (typeSearch != null)
                typeSearch.value = string.Empty;
            ApplyTypeFilter();
        }

        private void AddActionTypeFilterTab(VisualElement parent, ActionTypeFilter filter, string label)
        {
            var button = new Button(() => SelectActionTypeFilter(filter)) { text = label };
            button.AddToClassList("aq-type-tab");
            parent.Add(button);
            actionTypeFilterButtons[filter] = button;
        }

        private void SelectActionTypeFilter(ActionTypeFilter filter)
        {
            if (currentActionTypeFilter == filter)
                return;
            currentActionTypeFilter = filter;
            ApplyTypeFilter();
        }

        private static StyleSheet LoadStyleSheet()
        {
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet != null)
                return styleSheet;
            Debug.LogWarning($"Action Queue Debugger stylesheet was not found at '{StyleSheetPath}'.");
            return null;
        }

        private VisualElement BuildGuidePage()
        {
            var scroll = new ScrollView();
            scroll.AddToClassList("aq-guide");
            scroll.Add(CreateGuideCard("Action 与 Reactor", "A 表示 Action，R 表示 Reactor。执行链按实际因果关系展示；展开父节点可查看 Reactor 命中和子 Action 插入。"));
            scroll.Add(CreateGuideCard("三层响应筛选", "ObservedActionType：类型粗筛，决定 Reactor 关心哪类 Action。\nMatches：Reactor 自筛，判断来源、目标、伤害类型等业务条件。\nReactionGate：外部准入，决定当前 Action 是否允许某个 Reactor 触发。"));
            scroll.Add(CreateGuideCard("系统边界", "ActionEngineGuardSet 负责调度上限等不可被玩法屏蔽的系统不变量。GameAction 与 Reactor 只表达游戏逻辑；表现层不属于 ActionQueue。"));
            scroll.Add(CreateGuideCard("树搜索与展开", "执行链工具栏可按名称、节点 ID、Detail 或 Outcome 搜索。搜索会保留匹配节点的父路径并强制展开；清空搜索后恢复原来的展开状态。全部展开和全部收起只影响视图。"));
            scroll.Add(CreateGuideCard("节点详情", "右侧详情会保留完整名称、Detail 与 Outcome，可复制整份详情。状态使用中文显示；节点卡片和长文本悬停时可查看完整内容。"));
            scroll.Add(CreateGuideCard("Runner 与断点", "第一行工具栏可刷新 Runner 或定位当前 Runner。开启断点后队列在节点边界暂停；“下一节点”只放行一个节点，“恢复执行”会关闭断点。Unity 编辑器 Pause 与 Action Queue 断点是两套独立暂停机制，Unity Pause 时请先取消编辑器暂停再单步。"));
            return scroll;
        }

        private static VisualElement CreateGuideCard(string title, string body)
        {
            var card = new VisualElement();
            card.AddToClassList("aq-guide-card");
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("aq-guide-title");
            var bodyLabel = new Label(body);
            bodyLabel.AddToClassList("aq-guide-body");
            card.Add(titleLabel);
            card.Add(bodyLabel);
            return card;
        }

        private void RefreshActionTypeCatalog()
        {
            actionTypes.Clear();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<GameAction>())
            {
                if (type.IsAbstract || type.ContainsGenericParameters)
                    continue;
                var display = (ActionDisplayAttribute)Attribute.GetCustomAttribute(type, typeof(ActionDisplayAttribute));
                actionTypes.Add(new ActionTypeEntry(type.FullName ?? type.Name, string.IsNullOrEmpty(display?.DisplayName) ? type.Name : display.DisplayName, display?.Category ?? "Uncategorized", GetExecutionKind(type)));
            }
            actionTypes.Sort((left, right) =>
            {
                int category = string.Compare(left.Category, right.Category, StringComparison.Ordinal);
                return category != 0 ? category : string.Compare(left.FullName, right.FullName, StringComparison.Ordinal);
            });
            ApplyTypeFilter();
        }

        private void ApplyTypeFilter()
        {
            string filter = typeSearchQuery.Trim();
            filteredActionTypes.Clear();
            foreach (ActionTypeEntry entry in actionTypes)
            {
                if (!MatchesActionTypeFilter(entry))
                    continue;
                if (filter.Length > 0 && entry.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && entry.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && entry.Category.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && entry.Kind.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                filteredActionTypes.Add(entry);
            }
            foreach (KeyValuePair<ActionTypeFilter, Button> pair in actionTypeFilterButtons)
                pair.Value.EnableInClassList("is-selected", pair.Key == currentActionTypeFilter);
            actionTypeList?.Rebuild();
            if (actionTypeList != null)
                actionTypeList.style.display = filteredActionTypes.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (actionTypeEmptyState != null)
                actionTypeEmptyState.style.display = filteredActionTypes.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateNavigationBadges();
        }

        private static ActionExecutionKind? GetExecutionKind(Type type)
        {
            if (typeof(SignalAction).IsAssignableFrom(type))
                return ActionExecutionKind.Signal;
            if (typeof(CompositeGameAction).IsAssignableFrom(type))
                return ActionExecutionKind.Composite;
            if (typeof(CommandAction).IsAssignableFrom(type))
                return ActionExecutionKind.Command;
            return null;
        }

        private bool MatchesActionTypeFilter(ActionTypeEntry entry)
        {
            if (currentActionTypeFilter == ActionTypeFilter.All)
                return true;
            if (!entry.ExecutionKind.HasValue)
                return false;
            return currentActionTypeFilter switch
            {
                ActionTypeFilter.Command => entry.ExecutionKind.Value == ActionExecutionKind.Command,
                ActionTypeFilter.Composite => entry.ExecutionKind.Value == ActionExecutionKind.Composite,
                ActionTypeFilter.Signal => entry.ExecutionKind.Value == ActionExecutionKind.Signal,
                _ => false
            };
        }

        private void OnEnable()
        {
            minSize = new Vector2(1000f, 480f);
            debugBindingEnabled = EditorApplication.isPlaying;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            RefreshRunners();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnbindDebugger();
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup >= nextRunnerRefresh)
            {
                nextRunnerRefresh = EditorApplication.timeSinceStartup + 1d;
                RefreshRunners();
            }
            if (!EditorApplication.isPlaying || boundDebugger == null || EditorApplication.timeSinceStartup < nextSnapshotFallback)
                return;
            nextSnapshotFallback = EditorApplication.timeSinceStartup + 0.2d;
            if (boundDebugger.Version == observedDebugVersion)
                return;
            snapshotDirty = true;
            runtimeDebugger?.MarkDirtyRepaint();
            Repaint();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            debugBindingEnabled = state == PlayModeStateChange.EnteredPlayMode;
            UnbindDebugger();
            ResetViewState();
            RefreshRunners();
            BindDebugger();
            UpdateNavigationBadges();
            Repaint();
        }

        private void RefreshRunners()
        {
            ActionQueueRunner previousRunner = runner;
            runners.Clear();
            foreach (ActionQueueRunner candidate in Resources.FindObjectsOfTypeAll<ActionQueueRunner>())
            {
                if (candidate != null && candidate.gameObject.scene.IsValid())
                    runners.Add(candidate);
            }
            SelectAvailableRunner(previousRunner);
            snapshotDirty = true;
            UpdateNavigationBadges();
        }

        private void RemoveDestroyedRunners()
        {
            for (int i = runners.Count - 1; i >= 0; i--)
            {
                ActionQueueRunner candidate = runners[i];
                if (candidate == null || !candidate.gameObject.scene.IsValid())
                    runners.RemoveAt(i);
            }
            SelectAvailableRunner(runner);
        }

        private void SelectAvailableRunner(ActionQueueRunner preferredRunner)
        {
            ActionQueueRunner nextRunner = null;
            if (preferredRunner != null)
            {
                foreach (ActionQueueRunner candidate in runners)
                {
                    if (candidate != preferredRunner)
                        continue;
                    nextRunner = preferredRunner;
                    break;
                }
            }
            if (nextRunner == null && runners.Count > 0)
                nextRunner = runners[0];
            if (ReferenceEquals(runner, nextRunner))
            {
                runner = nextRunner;
                BindDebugger();
                return;
            }
            UnbindDebugger();
            ResetViewState();
            runner = nextRunner;
            BindDebugger();
        }

        private void BindDebugger(bool forceRebind = false)
        {
            if (!debugBindingEnabled || runner == null)
            {
                if (forceRebind)
                    UnbindDebugger();
                return;
            }
            ActionQueueDebugService debugger = runner.Debugger;
            if (!forceRebind && ReferenceEquals(boundDebugger, debugger) && recordingLease != null)
                return;
            UnbindDebugger();
            boundDebugger = debugger;
            boundDebugger.StateChanged += OnDebuggerStateChanged;
            recordingLease = boundDebugger.AcquireRecording();
            snapshotDirty = true;
        }

        private void UnbindDebugger()
        {
            if (boundDebugger != null)
                boundDebugger.StateChanged -= OnDebuggerStateChanged;
            recordingLease?.Dispose();
            recordingLease = null;
            boundDebugger = null;
            cachedSnapshot = null;
            snapshotDirty = true;
            observedDebugVersion = -1;
        }

        private void OnDebuggerStateChanged()
        {
            snapshotDirty = true;
            runtimeDebugger?.MarkDirtyRepaint();
            Repaint();
        }

        private void UpdateNavigationBadges()
        {
            if (playModeBadge != null)
            {
                playModeBadge.text = EditorApplication.isPlaying ? "● PLAY MODE" : "○ EDIT MODE";
                playModeBadge.EnableInClassList("is-playing", EditorApplication.isPlaying);
            }
            if (runnerBadge != null)
                runnerBadge.text = runner == null ? "未连接 Runner" : $"{runner.gameObject.name}  ·  {runners.Count} Runner";
            if (actionTypeBadge == null)
                return;
            if (currentPage == DebuggerPage.ActionTypes)
                actionTypeBadge.text = $"{filteredActionTypes.Count}/{actionTypes.Count} TYPES";
            else if (currentPage == DebuggerPage.Guide)
                actionTypeBadge.text = "REFERENCE";
            else
                actionTypeBadge.text = EditorApplication.isPlaying ? "LIVE" : "OFFLINE";
        }

        private ActionQueueDebugSnapshot GetSnapshot(bool forceRefresh = false)
        {
            if (runner == null || boundDebugger == null)
                return new ActionQueueDebugSnapshot();
            if (forceRefresh || snapshotDirty || cachedSnapshot == null)
            {
                cachedSnapshot = runner.GetDebugSnapshot();
                snapshotDirty = false;
                observedDebugVersion = boundDebugger.Version;
            }
            return cachedSnapshot;
        }

        private void DrawOverviewPage()
        {
            EnsureStyles();
            DrawToolbar();
            if (!TryGetRuntimeSnapshot(out ActionQueueDebugSnapshot snapshot))
                return;
            UpdateStepTracking(snapshot);
            DrawStatus(snapshot);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true)))
                {
                    DrawPanelHeader("工作队列", "当前待处理内容");
                    overviewScroll = EditorGUILayout.BeginScrollView(overviewScroll);
                    DrawStringSection("待处理根 Action", snapshot.PendingRoots, "无");
                    DrawStringSection("内部工作队列", snapshot.PendingWorkItems, "队列为空");
                    DrawNodeSummarySection("待处理 Action / Reactor", CollectPendingNodes(snapshot.Roots));
                    EditorGUILayout.EndScrollView();
                }
                float runtimeWidth = runtimeDebugger == null ? 1000f : runtimeDebugger.contentRect.width;
                float environmentWidth = Mathf.Clamp(runtimeWidth * 0.4f, 260f, 360f);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(environmentWidth)))
                {
                    DrawPanelHeader("响应环境", "调度分类与已注册 Reactor");
                    environmentScroll = EditorGUILayout.BeginScrollView(environmentScroll);
                    DrawActionKindSummary(snapshot.Roots);
                    DrawStringSection("已注册 Reactor", snapshot.RegisteredReactors, "无");
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawExecutionChainPage()
        {
            EnsureStyles();
            DrawToolbar();
            if (!TryGetRuntimeSnapshot(out ActionQueueDebugSnapshot snapshot))
                return;
            UpdateStepTracking(snapshot);
            DrawStatus(snapshot);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawTreePanel(snapshot);
                DrawDetailsPanel(snapshot);
            }
        }

        private bool TryGetRuntimeSnapshot(out ActionQueueDebugSnapshot snapshot)
        {
            snapshot = null;
            if (!EditorApplication.isPlaying || !debugBindingEnabled)
            {
                EditorGUILayout.HelpBox("进入 Play Mode 后，此页面会显示场景中的 ActionQueueRunner。Action 类型目录和阅读指南仍可离线使用。", MessageType.Info);
                return false;
            }
            if (runner == null)
            {
                EditorGUILayout.HelpBox("当前场景没有 ActionQueueRunner。请在一个 GameObject 上添加该组件。", MessageType.Warning);
                return false;
            }
            if (boundDebugger == null)
            {
                EditorGUILayout.HelpBox("当前 Runner 尚未连接调试服务，请等待 Play Mode 初始化完成。", MessageType.Info);
                return false;
            }
            snapshot = GetSnapshot();
            return true;
        }

        #region Toolbar

        private void DrawToolbar()
        {
            RemoveDestroyedRunners();
            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    DrawRunnerPopup();
                    if (GUILayout.Button(new GUIContent("刷新", "刷新 Runner 列表并重新读取状态"), EditorStyles.toolbarButton, GUILayout.Width(48f)))
                    {
                        RefreshRunners();
                        snapshotDirty = true;
                    }
                    using (new EditorGUI.DisabledScope(runner == null))
                    {
                        if (GUILayout.Button(new GUIContent("定位", "在 Hierarchy 中定位当前 Runner"), EditorStyles.toolbarButton, GUILayout.Width(48f)))
                            EditorGUIUtility.PingObject(runner.gameObject);
                    }
                    GUILayout.FlexibleSpace();
                }
                bool canControl = EditorApplication.isPlaying && debugBindingEnabled && runner != null && boundDebugger != null;
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    bool breakpoint = canControl && boundDebugger.BreakpointMode;
                    using (new EditorGUI.DisabledScope(!canControl))
                    {
                        bool nextBreakpoint = GUILayout.Toggle(breakpoint, new GUIContent("断点模式", "在 ActionQueue 节点边界暂停"), EditorStyles.toolbarButton, GUILayout.Width(82f));
                        if (nextBreakpoint != breakpoint)
                        {
                            boundDebugger.SetBreakpointMode(nextBreakpoint);
                            newNodeIds.Clear();
                            waitingForStepResult = false;
                        }
                    }
                    bool unityPaused = EditorApplication.isPaused;
                    bool canStep = canControl && boundDebugger.BreakpointMode && boundDebugger.IsPaused && !unityPaused;
                    string stepTooltip = unityPaused ? "请先取消 Unity 编辑器 Pause，再放行一个 ActionQueue 节点" : "放行一个 ActionQueue 节点";
                    using (new EditorGUI.DisabledScope(!canStep))
                    {
                        if (GUILayout.Button(new GUIContent("下一节点", stepTooltip), EditorStyles.toolbarButton, GUILayout.Width(76f)))
                        {
                            BeginStepTracking(GetSnapshot(forceRefresh: true));
                            boundDebugger.ContinueOneNode();
                        }
                    }
                    using (new EditorGUI.DisabledScope(!canControl))
                    {
                        if (GUILayout.Button(new GUIContent("恢复执行", "关闭断点并恢复队列执行"), EditorStyles.toolbarButton, GUILayout.Width(76f)))
                            boundDebugger.SetBreakpointMode(false);
                    }
                    GUILayout.FlexibleSpace();
                    Color oldBackground = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(1f, 0.48f, 0.42f);
                    using (new EditorGUI.DisabledScope(!canControl))
                    {
                        if (GUILayout.Button(new GUIContent("停止并清除", "终止当前运行并清除 ActionQueue 队列"), EditorStyles.toolbarButton, GUILayout.Width(92f)))
                        {
                            runner.StopAndClear();
                            ResetViewState();
                            snapshotDirty = true;
                        }
                    }
                    GUI.backgroundColor = oldBackground;
                }
            }
        }

        private void DrawRunnerPopup()
        {
            if (runners.Count == 0)
            {
                GUILayout.Label("No ActionQueueRunner", EditorStyles.miniLabel);
                return;
            }
            string[] names = new string[runners.Count];
            int selectedIndex = 0;
            for (int i = 0; i < runners.Count; i++)
            {
                ActionQueueRunner candidate = runners[i];
#if UNITY_6000_4_OR_NEWER
                // Unity 6.4 switches the runner identifier display to EntityId.
                names[i] = $"{candidate.gameObject.name} ({candidate.GetEntityId()})";
#else
                names[i] = $"{candidate.gameObject.name} ({candidate.GetInstanceID()})";
#endif
                if (candidate == runner)
                    selectedIndex = i;
            }
            int nextIndex = EditorGUILayout.Popup(selectedIndex, names, EditorStyles.toolbarPopup, GUILayout.MinWidth(180f));
            if (nextIndex == selectedIndex)
                return;
            runner = runners[nextIndex];
            ResetViewState();
            BindDebugger(forceRebind: true);
        }

        private void DrawStatus(ActionQueueDebugSnapshot snapshot)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 64f);
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.12f, 0.13f, 0.15f) : new Color(0.89f, 0.9f, 0.92f));
            rect.xMin += 10f;
            rect.xMax -= 10f;
            string chain = snapshot.HasChain ? $"Chain #{snapshot.ChainId}" : "无执行链";
            if (snapshot.IsLastCompletedChain)
                chain += "  ·  上一条，已完成";
            float actionWidth = Mathf.Min(190f, Mathf.Max(140f, rect.width * 0.4f));
            GUI.Label(new Rect(rect.x, rect.y + 7f, rect.width - actionWidth - 8f, 18f), chain, EditorStyles.boldLabel);
            GUI.Label(new Rect(rect.xMax - actionWidth, rect.y + 7f, actionWidth, 18f), $"Action {snapshot.ExecutedActionCount}/{snapshot.MaxActionsPerChain}", rightAlignedMiniLabel);
            string status;
            if (snapshot.IsPaused)
                status = $"已暂停 · {snapshot.PausedNode}";
            else if (string.IsNullOrEmpty(snapshot.CurrentNode))
                status = "空闲";
            else
                status = $"执行中 · {snapshot.CurrentNode}";
            Color oldColor = GUI.contentColor;
            if (snapshot.IsPaused)
                GUI.contentColor = GetStateColor(ActionQueueDebugNodeState.Queued);
            GUI.Label(new Rect(rect.x, rect.y + 34f, rect.width, 20f), new GUIContent(status, status), EditorStyles.label);
            GUI.contentColor = oldColor;
        }

        #endregion

        #region Panels

        private void DrawTreePanel(ActionQueueDebugSnapshot snapshot)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(300f), GUILayout.ExpandWidth(true)))
            {
                DrawPanelHeader("完整信息链", "点击节点，在右侧查看详情");
                DrawTreeToolbar(snapshot);
                EnsureTreeSearch(snapshot);
                treeScroll = EditorGUILayout.BeginScrollView(treeScroll);
                if (snapshot.Roots.Count == 0)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("尚无 Action 记录", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(30f));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndScrollView();
                    return;
                }
                if (treeSearchQuery.Length > 0 && visibleNodeIds.Count == 0)
                {
                    EditorGUILayout.HelpBox("没有匹配的节点。可搜索名称、节点 ID、Detail 或 Outcome。", MessageType.Info);
                    EditorGUILayout.EndScrollView();
                    return;
                }
                EnsureValidSelection(snapshot.Roots);
                int maxDepth = GetMaxVisibleDepth(snapshot.Roots, 0);
                float treeWidth = Mathf.Max(300f, 240f + maxDepth * TreeIndent);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(treeWidth), GUILayout.ExpandWidth(true)))
                    {
                        ancestorLineBuffer.Clear();
                        int visibleRootCount = 0;
                        foreach (ActionQueueDebugNode rootNode in snapshot.Roots)
                        {
                            if (treeSearchQuery.Length == 0 || visibleNodeIds.Contains(rootNode.Id))
                                visibleRootCount++;
                        }
                        int visibleRootIndex = 0;
                        for (int i = 0; i < snapshot.Roots.Count; i++)
                        {
                            ActionQueueDebugNode rootNode = snapshot.Roots[i];
                            if (treeSearchQuery.Length > 0 && !visibleNodeIds.Contains(rootNode.Id))
                                continue;
                            visibleRootIndex++;
                            DrawTreeNode(rootNode, 0, visibleRootIndex == visibleRootCount, ancestorLineBuffer);
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawTreeToolbar(ActionQueueDebugSnapshot snapshot)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool searchDisabled = treeSearchQuery.Length > 0;
                using (new EditorGUI.DisabledScope(searchDisabled))
                {
                    if (GUILayout.Button(new GUIContent("全部展开", "展开当前快照中的全部节点"), EditorStyles.toolbarButton, GUILayout.Width(66f)))
                        SetAllFoldouts(snapshot.Roots, true);
                    if (GUILayout.Button(new GUIContent("全部收起", "收起当前快照中的全部节点，根节点仍保持可见"), EditorStyles.toolbarButton, GUILayout.Width(66f)))
                        SetAllFoldouts(snapshot.Roots, false);
                }
                GUILayout.FlexibleSpace();
                treeSearch ??= new SearchField();
                Rect searchRect = GUILayoutUtility.GetRect(130f, 20f, GUILayout.MinWidth(130f), GUILayout.MaxWidth(260f), GUILayout.ExpandWidth(true));
                EditorGUI.BeginChangeCheck();
                string nextQuery = treeSearch.OnGUI(searchRect, treeSearchText);
                if (EditorGUI.EndChangeCheck())
                    SetTreeSearchQuery(nextQuery);
            }
        }

        private void DrawDetailsPanel(ActionQueueDebugSnapshot snapshot)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(DetailsWidth)))
            {
                DrawPanelHeader("节点详情", "当前观察节点");
                detailsScroll = EditorGUILayout.BeginScrollView(detailsScroll);
                ActionQueueDebugNode selected = FindNode(snapshot.Roots, selectedNodeId);
                if (selected == null)
                {
                    EditorGUILayout.HelpBox("请在完整信息链中选择一个 Action 或 Reactor。", MessageType.Info);
                    EditorGUILayout.EndScrollView();
                    return;
                }
                DrawSelectedNodeCard(selected);
                if (GUILayout.Button(new GUIContent("复制详情", "复制当前节点的完整字段到剪贴板"), GUILayout.Height(24f)))
                    CopyNodeDetails(selected);
                float detailWidth = Mathf.Max(80f, DetailsWidth - 32f);
                DrawDetailField("名称", selected.Name, detailWidth);
                DrawDetailField("类型", selected.Kind == ActionQueueDebugNodeKind.Action ? "Action" : "Reactor", detailWidth);
                if (selected.ExecutionKind.HasValue)
                {
                    DrawDetailField("执行分类", selected.ExecutionKind.Value.ToString(), detailWidth);
                    DrawDetailField("开放钩子", selected.ReactionPhases.ToString(), detailWidth);
                }
                DrawDetailField("状态", StateName(selected.State), detailWidth, GetStateColor(selected.State));
                DrawDetailField("节点 ID", selected.Id.ToString(), detailWidth);
                DrawDetailField("父 Action ID", selected.ParentActionId == 0 ? "Root" : selected.ParentActionId.ToString(), detailWidth);
                DrawDetailField("详细信息", EmptyFallback(selected.Detail), detailWidth);
                DrawDetailField("结果", EmptyFallback(selected.Outcome), detailWidth);
                DrawDetailField("子 Action", selected.Children.Count.ToString(), detailWidth);
                DrawDetailField("触发 Reactor", selected.Reactors.Count.ToString(), detailWidth);
                if (newNodeIds.Contains(selected.Id))
                {
                    EditorGUILayout.Space(8f);
                    EditorGUILayout.HelpBox("此节点由刚刚放行的断点步骤新插入。", MessageType.Warning);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private static void DrawPanelHeader(string title, string subtitle)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 38f);
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.16f, 0.17f, 0.20f) : new Color(0.78f, 0.81f, 0.86f));
            GUI.Label(new Rect(rect.x + 9f, rect.y + 4f, rect.width - 18f, 18f), title, EditorStyles.boldLabel);
            GUI.Label(new Rect(rect.x + 9f, rect.y + 21f, rect.width - 18f, 14f), subtitle, EditorStyles.centeredGreyMiniLabel);
        }

        #endregion

        #region Tree

        private void DrawTreeNode(ActionQueueDebugNode node, int depth, bool isLastSibling, List<bool> ancestorContinues)
        {
            Rect row = EditorGUILayout.GetControlRect(false, TreeRowHeight);
            bool searchEnabled = treeSearchQuery.Length > 0;
            int visibleDescendantCount = searchEnabled ? CountVisibleChildren(node) : node.Reactors.Count + node.Children.Count;
            bool hasDescendants = visibleDescendantCount > 0;
            bool expanded = hasDescendants && (treeSearchQuery.Length > 0 || GetFoldout(node.Id));
            bool selected = node.Id == selectedNodeId;
            bool isNew = newNodeIds.Contains(node.Id);
            DrawNodeBackground(row, node.State, selected, isNew);
            DrawHierarchyLines(row, depth, isLastSibling, ancestorContinues);
            if (expanded)
                DrawChildStem(row, depth);

            float contentX = row.x + 5f + depth * TreeIndent;
            Rect foldoutRect = new Rect(contentX, row.y + 5f, 16f, 18f);
            if (hasDescendants)
            {
                bool nextExpanded = treeSearchQuery.Length > 0 || EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none);
                if (treeSearchQuery.Length == 0 && nextExpanded != expanded)
                    foldouts[node.Id] = nextExpanded;
                expanded = nextExpanded;
            }
            float badgeWidth = isNew ? 44f : 0f;
            const float stateWidth = 58f;
            float nameWidth = Mathf.Max(24f, row.xMax - (contentX + 22f) - badgeWidth - stateWidth - 12f);
            Rect selectRect = new Rect(contentX + 17f, row.y, Mathf.Max(24f, row.xMax - contentX - 17f), row.height);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && selectRect.Contains(Event.current.mousePosition))
            {
                selectedNodeId = node.Id;
                Event.current.Use();
                Repaint();
            }
            string kindMarker = node.Kind == ActionQueueDebugNodeKind.Action ? "A" : "R";
            string tooltip = $"{node.Name}\n{StateName(node.State)}\n{EmptyFallback(node.Detail)}";
            GUI.Label(new Rect(selectRect.x + 2f, selectRect.y + 4f, nameWidth, 20f), new GUIContent($"{kindMarker}  {node.Name}", tooltip), selected ? EditorStyles.boldLabel : EditorStyles.label);
            float badgeX = row.xMax - stateWidth - 4f;
            if (isNew)
            {
                DrawBadge(new Rect(badgeX - badgeWidth, row.y + 6f, badgeWidth - 4f, 16f), "NEW", new Color(1f, 0.58f, 0.12f));
                badgeX -= badgeWidth;
            }
            DrawBadge(new Rect(row.xMax - stateWidth, row.y + 6f, stateWidth - 4f, 16f), StateName(node.State), GetStateColor(node.State));
            if (!expanded)
                return;

            int descendantCount = visibleDescendantCount;
            int descendantIndex = 0;
            ancestorContinues.Add(!isLastSibling);
            foreach (ActionQueueDebugNode reactorNode in node.Reactors)
            {
                if (treeSearchQuery.Length == 0 || visibleNodeIds.Contains(reactorNode.Id))
                {
                    descendantIndex++;
                    DrawTreeNode(reactorNode, depth + 1, descendantIndex == descendantCount, ancestorContinues);
                }
            }
            foreach (ActionQueueDebugNode childNode in node.Children)
            {
                if (treeSearchQuery.Length == 0 || visibleNodeIds.Contains(childNode.Id))
                {
                    descendantIndex++;
                    DrawTreeNode(childNode, depth + 1, descendantIndex == descendantCount, ancestorContinues);
                }
            }
            ancestorContinues.RemoveAt(ancestorContinues.Count - 1);
        }

        private static void DrawHierarchyLines(Rect row, int depth, bool isLastSibling, List<bool> ancestorContinues)
        {
            if (Event.current.type != EventType.Repaint || depth == 0)
                return;
            Color lineColor = EditorGUIUtility.isProSkin ? new Color(0.44f, 0.47f, 0.52f, 0.9f) : new Color(0.34f, 0.38f, 0.44f, 0.9f);
            for (int i = 0; i < ancestorContinues.Count; i++)
            {
                if (!ancestorContinues[i])
                    continue;
                float ancestorX = row.x + 13f + i * TreeIndent;
                EditorGUI.DrawRect(new Rect(ancestorX, row.yMin, 1f, row.height), lineColor);
            }
            float branchX = row.x + 13f + (depth - 1) * TreeIndent;
            float centerY = Mathf.Round(row.center.y);
            float verticalHeight = isLastSibling ? centerY - row.yMin : row.height;
            EditorGUI.DrawRect(new Rect(branchX, row.yMin, 1f, verticalHeight), lineColor);
            EditorGUI.DrawRect(new Rect(branchX, centerY, TreeIndent - 7f, 1f), lineColor);
        }

        private static void DrawChildStem(Rect row, int depth)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            Color lineColor = EditorGUIUtility.isProSkin ? new Color(0.44f, 0.47f, 0.52f, 0.9f) : new Color(0.34f, 0.38f, 0.44f, 0.9f);
            float stemX = row.x + 13f + depth * TreeIndent;
            EditorGUI.DrawRect(new Rect(stemX, Mathf.Round(row.center.y), 1f, row.yMax - row.center.y), lineColor);
        }

        private static void DrawNodeBackground(Rect row, ActionQueueDebugNodeState state, bool selected, bool isNew)
        {
            if (selected)
            {
                EditorGUI.DrawRect(row, new Color(0.16f, 0.47f, 0.78f, 0.36f));
                EditorGUI.DrawRect(new Rect(row.x, row.y, 3f, row.height), new Color(0.25f, 0.72f, 1f));
                return;
            }
            if (isNew)
            {
                EditorGUI.DrawRect(row, new Color(1f, 0.53f, 0.08f, 0.18f));
                EditorGUI.DrawRect(new Rect(row.x, row.y, 3f, row.height), new Color(1f, 0.58f, 0.12f));
                return;
            }
            if (state == ActionQueueDebugNodeState.Executing)
                EditorGUI.DrawRect(row, new Color(0.18f, 0.65f, 0.92f, 0.12f));
            else if (((int)(row.y / TreeRowHeight) & 1) == 0)
                EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.018f));
        }

        private void DrawBadge(Rect rect, string text, Color color)
        {
            EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.22f));
            Color oldColor = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(rect, text, badgeStyle);
            GUI.contentColor = oldColor;
        }

        #endregion

        #region Search and Details

        private void EnsureTreeSearch(ActionQueueDebugSnapshot snapshot)
        {
            if (ReferenceEquals(searchSnapshot, snapshot))
                return;
            BuildSearchCache(snapshot);
            searchSnapshot = snapshot;
        }

        private void SetTreeSearchQuery(string query)
        {
            treeSearchText = query ?? string.Empty;
            string normalizedQuery = treeSearchText.Trim();
            if (treeSearchQuery == normalizedQuery)
                return;
            treeSearchQuery = normalizedQuery;
            searchSnapshot = null;
            runtimeDebugger?.MarkDirtyRepaint();
        }

        private void BuildSearchCache(ActionQueueDebugSnapshot snapshot)
        {
            visibleNodeIds.Clear();
            if (treeSearchQuery.Length == 0)
                return;
            foreach (ActionQueueDebugNode node in snapshot.Roots)
                BuildVisibleNodeIds(node);
        }

        private bool BuildVisibleNodeIds(ActionQueueDebugNode node)
        {
            bool matches = NodeMatchesSearch(node);
            foreach (ActionQueueDebugNode reactorNode in node.Reactors)
                matches |= BuildVisibleNodeIds(reactorNode);
            foreach (ActionQueueDebugNode childNode in node.Children)
                matches |= BuildVisibleNodeIds(childNode);
            if (matches)
                visibleNodeIds.Add(node.Id);
            return matches;
        }

        private bool NodeMatchesSearch(ActionQueueDebugNode node)
        {
            if (treeSearchQuery.Length == 0)
                return true;
            return (node.Name != null && node.Name.IndexOf(treeSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0) || node.Id.ToString().IndexOf(treeSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0 || (node.Detail != null && node.Detail.IndexOf(treeSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0) || (node.Outcome != null && node.Outcome.IndexOf(treeSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private int GetMaxVisibleDepth(List<ActionQueueDebugNode> nodes, int depth)
        {
            int maxDepth = depth;
            foreach (ActionQueueDebugNode node in nodes)
            {
                if (treeSearchQuery.Length > 0 && !visibleNodeIds.Contains(node.Id))
                    continue;
                maxDepth = Mathf.Max(maxDepth, GetMaxVisibleDepth(node, depth));
            }
            return maxDepth;
        }

        private int GetMaxVisibleDepth(ActionQueueDebugNode node, int depth)
        {
            int maxDepth = depth;
            if (treeSearchQuery.Length == 0 && !GetFoldout(node.Id))
                return maxDepth;
            foreach (ActionQueueDebugNode childNode in node.Reactors)
            {
                if (treeSearchQuery.Length == 0 || visibleNodeIds.Contains(childNode.Id))
                    maxDepth = Mathf.Max(maxDepth, GetMaxVisibleDepth(childNode, depth + 1));
            }
            foreach (ActionQueueDebugNode childNode in node.Children)
            {
                if (treeSearchQuery.Length == 0 || visibleNodeIds.Contains(childNode.Id))
                    maxDepth = Mathf.Max(maxDepth, GetMaxVisibleDepth(childNode, depth + 1));
            }
            return maxDepth;
        }

        private int CountVisibleChildren(ActionQueueDebugNode node)
        {
            int count = 0;
            foreach (ActionQueueDebugNode reactorNode in node.Reactors)
            {
                if (visibleNodeIds.Contains(reactorNode.Id))
                    count++;
            }
            foreach (ActionQueueDebugNode childNode in node.Children)
            {
                if (visibleNodeIds.Contains(childNode.Id))
                    count++;
            }
            return count;
        }

        private void SetAllFoldouts(List<ActionQueueDebugNode> roots, bool expanded)
        {
            foreach (ActionQueueDebugNode node in roots)
                SetNodeFoldouts(node, expanded);
            runtimeDebugger?.MarkDirtyRepaint();
        }

        private void SetNodeFoldouts(ActionQueueDebugNode node, bool expanded)
        {
            foldouts[node.Id] = expanded;
            foreach (ActionQueueDebugNode reactorNode in node.Reactors)
                SetNodeFoldouts(reactorNode, expanded);
            foreach (ActionQueueDebugNode childNode in node.Children)
                SetNodeFoldouts(childNode, expanded);
        }

        private void CopyNodeDetails(ActionQueueDebugNode node)
        {
            EditorGUIUtility.systemCopyBuffer = $"名称: {node.Name}\n类型: {(node.Kind == ActionQueueDebugNodeKind.Action ? "Action" : "Reactor")}\n执行分类: {node.ExecutionKind?.ToString() ?? "—"}\n状态: {StateName(node.State)}\n节点 ID: {node.Id}\n父 Action ID: {(node.ParentActionId == 0 ? "Root" : node.ParentActionId.ToString())}\nDetail: {EmptyFallback(node.Detail)}\nOutcome: {EmptyFallback(node.Outcome)}";
        }

        private static void DrawSelectedNodeCard(ActionQueueDebugNode node)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 58f);
            Color stateColor = GetStateColor(node.State);
            EditorGUI.DrawRect(rect, new Color(stateColor.r, stateColor.g, stateColor.b, 0.13f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), stateColor);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 7f, rect.width - 20f, 20f), new GUIContent($"{(node.Kind == ActionQueueDebugNodeKind.Action ? "A" : "R")}  {node.Name}", node.Name), EditorStyles.boldLabel);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 31f, rect.width - 20f, 18f), $"{(node.Kind == ActionQueueDebugNodeKind.Action ? "Action" : "Reactor")}  ·  #{node.Id}  ·  {StateName(node.State)}", EditorStyles.miniLabel);
        }

        private static void DrawDetailField(string label, string value, float width, Color? valueColor = null)
        {
            EditorGUILayout.Space(7f);
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            Color oldColor = GUI.contentColor;
            if (valueColor.HasValue)
                GUI.contentColor = valueColor.Value;
            float height = EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(value), width);
            EditorGUILayout.SelectableLabel(value, EditorStyles.wordWrappedLabel, GUILayout.Height(Mathf.Max(EditorGUIUtility.singleLineHeight, height)));
            GUI.contentColor = oldColor;
        }

        #endregion

        #region Tracking and Overview

        private void BeginStepTracking(ActionQueueDebugSnapshot snapshot)
        {
            stepBaseline.Clear();
            CollectNodeIds(snapshot.Roots, stepBaseline);
            newNodeIds.Clear();
            waitingForStepResult = true;
        }

        private void UpdateStepTracking(ActionQueueDebugSnapshot snapshot)
        {
            bool chainChanged = snapshot.HasChain != observedHasChain || snapshot.HasChain && snapshot.ChainId != observedChainId;
            if (chainChanged)
            {
                observedHasChain = snapshot.HasChain;
                observedChainId = snapshot.ChainId;
                stepBaseline.Clear();
                newNodeIds.Clear();
                waitingForStepResult = false;
                CollectNodeIds(snapshot.Roots, stepBaseline);
            }
            if (!waitingForStepResult)
                return;
            currentNodeIds.Clear();
            CollectNodeIds(snapshot.Roots, currentNodeIds);
            newNodeIds.Clear();
            foreach (long id in currentNodeIds)
            {
                if (!stepBaseline.Contains(id))
                    newNodeIds.Add(id);
            }
            if (snapshot.IsPaused || runner == null || !runner.IsRunning)
                waitingForStepResult = false;
        }

        private static void DrawNodeSummarySection(string title, List<ActionQueueDebugNode> nodes)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (nodes.Count == 0)
            {
                EditorGUILayout.LabelField("无", EditorStyles.miniLabel);
                return;
            }
            foreach (ActionQueueDebugNode node in nodes)
                EditorGUILayout.LabelField($"{(node.Kind == ActionQueueDebugNodeKind.Action ? "A" : "R")} {node.Name}", EditorStyles.label);
        }

        private void DrawActionKindSummary(List<ActionQueueDebugNode> roots)
        {
            int command = 0;
            int signal = 0;
            int composite = 0;
            int noHooks = 0;
            traversalStack.Clear();
            foreach (ActionQueueDebugNode rootNode in roots)
                traversalStack.Push(rootNode);
            while (traversalStack.Count > 0)
            {
                ActionQueueDebugNode node = traversalStack.Pop();
                if (node.Kind == ActionQueueDebugNodeKind.Action && node.ExecutionKind.HasValue)
                {
                    switch (node.ExecutionKind.Value)
                    {
                        case ActionExecutionKind.Command: command++; break;
                        case ActionExecutionKind.Signal: signal++; break;
                        case ActionExecutionKind.Composite: composite++; break;
                    }
                    if (node.ReactionPhases == ReactionPhases.None)
                        noHooks++;
                }
                foreach (ActionQueueDebugNode reactorNode in node.Reactors)
                    traversalStack.Push(reactorNode);
                foreach (ActionQueueDebugNode childNode in node.Children)
                    traversalStack.Push(childNode);
            }
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("实际调度 Action 分类", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Command {command}  ·  Signal {signal}  ·  Composite {composite}  ·  Hooks None {noHooks}", EditorStyles.wordWrappedLabel);
        }

        private static void DrawStringSection(string title, List<string> values, string emptyText)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (values.Count == 0)
            {
                EditorGUILayout.LabelField(emptyText, EditorStyles.label);
                return;
            }
            foreach (string value in values)
                EditorGUILayout.LabelField("• " + value, EditorStyles.wordWrappedLabel);
        }

        private List<ActionQueueDebugNode> CollectPendingNodes(List<ActionQueueDebugNode> roots)
        {
            pendingNodeBuffer.Clear();
            traversalStack.Clear();
            for (int i = roots.Count - 1; i >= 0; i--)
                traversalStack.Push(roots[i]);
            while (traversalStack.Count > 0)
            {
                ActionQueueDebugNode node = traversalStack.Pop();
                if (node.State == ActionQueueDebugNodeState.Queued || node.State == ActionQueueDebugNodeState.Executing)
                    pendingNodeBuffer.Add(node);
                for (int i = node.Children.Count - 1; i >= 0; i--)
                    traversalStack.Push(node.Children[i]);
                for (int i = node.Reactors.Count - 1; i >= 0; i--)
                    traversalStack.Push(node.Reactors[i]);
            }
            return pendingNodeBuffer;
        }

        private void EnsureValidSelection(List<ActionQueueDebugNode> roots)
        {
            if (FindNode(roots, selectedNodeId) != null)
                return;
            selectedNodeId = roots.Count > 0 ? roots[0].Id : 0;
        }

        private void CollectNodeIds(List<ActionQueueDebugNode> roots, HashSet<long> result)
        {
            traversalStack.Clear();
            foreach (ActionQueueDebugNode rootNode in roots)
                traversalStack.Push(rootNode);
            while (traversalStack.Count > 0)
            {
                ActionQueueDebugNode node = traversalStack.Pop();
                result.Add(node.Id);
                foreach (ActionQueueDebugNode reactorNode in node.Reactors)
                    traversalStack.Push(reactorNode);
                foreach (ActionQueueDebugNode childNode in node.Children)
                    traversalStack.Push(childNode);
            }
        }

        private ActionQueueDebugNode FindNode(List<ActionQueueDebugNode> roots, long id)
        {
            if (id == 0)
                return null;
            traversalStack.Clear();
            foreach (ActionQueueDebugNode rootNode in roots)
                traversalStack.Push(rootNode);
            while (traversalStack.Count > 0)
            {
                ActionQueueDebugNode node = traversalStack.Pop();
                if (node.Id == id)
                    return node;
                foreach (ActionQueueDebugNode reactorNode in node.Reactors)
                    traversalStack.Push(reactorNode);
                foreach (ActionQueueDebugNode childNode in node.Children)
                    traversalStack.Push(childNode);
            }
            return null;
        }

        private void ResetViewState()
        {
            selectedNodeId = 0;
            observedChainId = 0;
            observedHasChain = false;
            waitingForStepResult = false;
            foldouts.Clear();
            visibleNodeIds.Clear();
            searchSnapshot = null;
            treeSearchText = string.Empty;
            treeSearchQuery = string.Empty;
            stepBaseline.Clear();
            newNodeIds.Clear();
            cachedSnapshot = null;
            snapshotDirty = true;
        }

        private void EnsureStyles()
        {
            rightAlignedMiniLabel ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, fontSize = 11 };
            badgeStyle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 11 };
        }

        private bool GetFoldout(long id) => !foldouts.TryGetValue(id, out bool expanded) || expanded;

        private static string EmptyFallback(string value) => string.IsNullOrEmpty(value) ? "—" : value;

        private static string StateName(ActionQueueDebugNodeState state)
        {
            return state switch
            {
                ActionQueueDebugNodeState.Queued => "等待",
                ActionQueueDebugNodeState.Executing => "执行中",
                ActionQueueDebugNodeState.Resolved => "完成",
                ActionQueueDebugNodeState.Skipped => "跳过",
                _ => state.ToString()
            };
        }

        private static Color GetStateColor(ActionQueueDebugNodeState state)
        {
            if (EditorGUIUtility.isProSkin)
            {
                return state switch
                {
                    ActionQueueDebugNodeState.Queued => new Color(1f, 0.72f, 0.2f),
                    ActionQueueDebugNodeState.Executing => new Color(0.22f, 0.78f, 1f),
                    ActionQueueDebugNodeState.Resolved => new Color(0.32f, 0.9f, 0.48f),
                    ActionQueueDebugNodeState.Skipped => new Color(0.65f, 0.67f, 0.72f),
                    _ => Color.white
                };
            }
            return state switch
            {
                ActionQueueDebugNodeState.Queued => new Color(0.58f, 0.32f, 0.03f),
                ActionQueueDebugNodeState.Executing => new Color(0.03f, 0.32f, 0.58f),
                ActionQueueDebugNodeState.Resolved => new Color(0.05f, 0.42f, 0.17f),
                ActionQueueDebugNodeState.Skipped => new Color(0.3f, 0.32f, 0.36f),
                _ => Color.black
            };
        }

        #endregion

        private enum DebuggerPage { Overview, ExecutionChain, ActionTypes, Guide }
        private enum ActionTypeFilter { All, Command, Composite, Signal }

        private readonly struct ActionTypeEntry
        {
            public ActionTypeEntry(string fullName, string displayName, string category, ActionExecutionKind? executionKind)
            {
                FullName = fullName;
                DisplayName = displayName;
                Category = category;
                ExecutionKind = executionKind;
            }

            public string FullName { get; }
            public string DisplayName { get; }
            public string Category { get; }
            public ActionExecutionKind? ExecutionKind { get; }
            public string Kind => ExecutionKind?.ToString() ?? "Custom (建议改用标准基类)";
        }
    }
}
