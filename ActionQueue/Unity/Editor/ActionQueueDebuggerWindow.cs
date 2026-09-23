using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardGame.ActionQueue.Editor
{
    public sealed class ActionQueueDebuggerWindow : EditorWindow
    {
        private const float DetailsWidth = 315f;
        private const float TreeIndent = 20f;
        private const float TreeRowHeight = 22f;
        private const string StyleSheetPath = "Assets/TurnBasedPack/ActionQueue/Editor/ActionQueueDebuggerWindow.uss";

        private readonly List<ActionQueueRunner> _runners = new();
        private readonly Dictionary<long, bool> _foldouts = new();
        private readonly HashSet<long> _stepBaseline = new();
        private readonly HashSet<long> _newNodeIds = new();
        private readonly HashSet<long> _currentNodeIds = new();
        private readonly List<ActionQueueDebugNode> _pendingNodeBuffer = new();
        private readonly Stack<ActionQueueDebugNode> _traversalStack = new();
        private readonly List<bool> _ancestorLineBuffer = new();

        private ActionQueueRunner _runner;
        private Vector2 _overviewScroll;
        private Vector2 environmentScroll;
        private Vector2 _treeScroll;
        private Vector2 _detailsScroll;
        private long _selectedNodeId;
        private long _observedChainId;
        private bool _observedHasChain;
        private bool _waitingForStepResult;
        private bool _debugBindingEnabled;
        private bool _snapshotDirty = true;
        private long _observedDebugVersion = -1;
        private double _nextRunnerRefresh;
        private double _nextSnapshotFallback;
        private ActionQueueDebugService _boundDebugger;
        private IDisposable _recordingLease;
        private ActionQueueDebugSnapshot _cachedSnapshot;
        private GUIStyle _rightAlignedMiniLabel;
        private GUIStyle _badgeStyle;
        private readonly List<ActionTypeEntry> _actionTypes = new();
        private readonly List<ActionTypeEntry> _filteredActionTypes = new();
        private readonly Dictionary<ActionTypeFilter, Button> actionTypeFilterButtons = new();
        private ListView _actionTypeList;
        private Label actionTypeEmptyState;
        private ToolbarSearchField _typeSearch;
        private IMGUIContainer _runtimeDebugger;
        private readonly Dictionary<DebuggerPage, Button> pageButtons = new();
        private VisualElement pageContent;
        private Label pageTitle;
        private Label pageSubtitle;
        private Label playModeBadge;
        private Label runnerBadge;
        private Label actionTypeBadge;
        private DebuggerPage currentPage = DebuggerPage.Overview;
        private ActionTypeFilter currentActionTypeFilter = ActionTypeFilter.All;

        [MenuItem("Window/Card Game/Action Queue Debugger")]
        public static void Open()
        {
            GetWindow<ActionQueueDebuggerWindow>("Action Queue Debugger");
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            pageButtons.Clear();
            StyleSheet styleSheet = LoadStyleSheet();
            if (styleSheet != null)
                rootVisualElement.styleSheets.Add(styleSheet);
            rootVisualElement.AddToClassList("aq-root");

            var split = new TwoPaneSplitView(0, 236f, TwoPaneSplitViewOrientation.Horizontal);
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

            _runtimeDebugger = null;
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
            _runtimeDebugger = new IMGUIContainer(drawHandler) { name = "runtime-debugger" };
            _runtimeDebugger.AddToClassList("aq-runtime-canvas");
            pageContent.Add(_runtimeDebugger);
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
            var spacer = new ToolbarSpacer();
            spacer.style.flexGrow = 1;
            toolbar.Add(spacer);
            _typeSearch = new ToolbarSearchField();
            _typeSearch.style.width = 280f;
            _typeSearch.RegisterValueChangedCallback(_ => ApplyTypeFilter());
            toolbar.Add(_typeSearch);
            panel.Add(toolbar);

            var filterTabs = new VisualElement();
            filterTabs.AddToClassList("aq-type-tabs");
            actionTypeFilterButtons.Clear();
            AddActionTypeFilterTab(filterTabs, ActionTypeFilter.All, "全部");
            AddActionTypeFilterTab(filterTabs, ActionTypeFilter.Command, "Command");
            AddActionTypeFilterTab(filterTabs, ActionTypeFilter.Composite, "Composite");
            AddActionTypeFilterTab(filterTabs, ActionTypeFilter.Signal, "Signal");
            panel.Add(filterTabs);

            _actionTypeList = new ListView
            {
                itemsSource = _filteredActionTypes,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                selectionType = SelectionType.Single,
                makeItem = () => new Label(),
                bindItem = (element, index) =>
                {
                    ActionTypeEntry entry = _filteredActionTypes[index];
                    ((Label)element).text = $"{entry.DisplayName}    ·    {entry.Kind}\n{entry.FullName}\n{entry.Category}";
                    element.tooltip = entry.Category;
                    element.AddToClassList("aq-type-row");
                }
            };
            _actionTypeList.AddToClassList("aq-type-list");
            panel.Add(_actionTypeList);
            actionTypeEmptyState = new Label("没有符合当前分类和搜索条件的 Action 类型");
            actionTypeEmptyState.AddToClassList("aq-type-empty");
            panel.Add(actionTypeEmptyState);
            ApplyTypeFilter();
            return panel;
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

            string[] candidates = AssetDatabase.FindAssets("ActionQueueDebuggerWindow t:StyleSheet");
            if (candidates.Length == 0)
                return null;

            return AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(candidates[0]));
        }

        private VisualElement BuildGuidePage()
        {
            var scroll = new ScrollView();
            scroll.AddToClassList("aq-guide");
            scroll.Add(CreateGuideCard("Action 与 Reactor", "◆ 表示 Action，⚡ 表示 Reactor。执行链按实际因果关系展示；展开父节点可查看 Reactor 命中和子 Action 插入。"));
            scroll.Add(CreateGuideCard("三层响应筛选", "ObservedActionType：类型粗筛，决定 Reactor 关心哪类 Action。\nMatches：Reactor 自筛，判断来源、目标、伤害类型等业务条件。\nReactionGate：外部准入，决定当前 Action 是否允许某个 Reactor 触发。"));
            scroll.Add(CreateGuideCard("系统边界", "ActionEngineGuardSet 负责调度上限等不可被玩法屏蔽的系统不变量。GameAction 与 Reactor 只表达游戏逻辑；表现层不属于 ActionQueue。"));
            scroll.Add(CreateGuideCard("断点模式", "开启断点后，队列会在节点边界暂停。点击“继续下一个节点”只放行一个节点；本次新增节点会以 NEW 标记突出显示。"));
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
            _actionTypes.Clear();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<GameAction>())
            {
                if (type.IsAbstract || type.ContainsGenericParameters)
                    continue;
                var display = (ActionDisplayAttribute)Attribute.GetCustomAttribute(
                    type, typeof(ActionDisplayAttribute));
                ActionExecutionKind? executionKind = GetExecutionKind(type);
                _actionTypes.Add(new ActionTypeEntry(
                    type.FullName ?? type.Name,
                    string.IsNullOrEmpty(display?.DisplayName) ? type.Name : display.DisplayName,
                    display?.Category ?? "Uncategorized",
                    executionKind));
            }
            _actionTypes.Sort((left, right) =>
            {
                int category = string.Compare(left.Category, right.Category, StringComparison.Ordinal);
                return category != 0
                    ? category
                    : string.Compare(left.FullName, right.FullName, StringComparison.Ordinal);
            });
            ApplyTypeFilter();
        }

        private void ApplyTypeFilter()
        {
            string filter = _typeSearch?.value?.Trim() ?? string.Empty;
            _filteredActionTypes.Clear();
            foreach (ActionTypeEntry entry in _actionTypes)
            {
                if (!MatchesActionTypeFilter(entry))
                    continue;

                if (filter.Length > 0 && entry.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && entry.Category.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && entry.Kind.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                _filteredActionTypes.Add(entry);
            }

            foreach (KeyValuePair<ActionTypeFilter, Button> pair in actionTypeFilterButtons)
                pair.Value.EnableInClassList("is-selected", pair.Key == currentActionTypeFilter);

            _actionTypeList?.Rebuild();
            if (_actionTypeList != null)
                _actionTypeList.style.display = _filteredActionTypes.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (actionTypeEmptyState != null)
                actionTypeEmptyState.style.display = _filteredActionTypes.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
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
            _debugBindingEnabled = EditorApplication.isPlaying;
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
            if (EditorApplication.timeSinceStartup >= _nextRunnerRefresh)
            {
                _nextRunnerRefresh = EditorApplication.timeSinceStartup + 1d;
                RefreshRunners();
            }

            if (EditorApplication.isPlaying &&
                _boundDebugger != null &&
                EditorApplication.timeSinceStartup >= _nextSnapshotFallback)
            {
                _nextSnapshotFallback = EditorApplication.timeSinceStartup + 0.2d;
                if (_boundDebugger.Version != _observedDebugVersion)
                {
                    _snapshotDirty = true;
                    _runtimeDebugger?.MarkDirtyRepaint();
                    Repaint();
                }
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            _debugBindingEnabled = state == PlayModeStateChange.EnteredPlayMode;
            UnbindDebugger();
            ResetViewState();
            RefreshRunners();
            BindDebugger();
            UpdateNavigationBadges();
            Repaint();
        }

        private void RefreshRunners()
        {
            ActionQueueRunner previousRunner = _runner;
            _runners.Clear();
            foreach (ActionQueueRunner runner in Resources.FindObjectsOfTypeAll<ActionQueueRunner>())
            {
                if (runner != null && runner.gameObject.scene.IsValid())
                    _runners.Add(runner);
            }

            SelectAvailableRunner(previousRunner);
            UpdateNavigationBadges();
        }

        private void RemoveDestroyedRunners()
        {
            for (int i = _runners.Count - 1; i >= 0; i--)
            {
                ActionQueueRunner runner = _runners[i];
                if (runner == null || !runner.gameObject.scene.IsValid())
                    _runners.RemoveAt(i);
            }

            SelectAvailableRunner(_runner);
        }

        private void SelectAvailableRunner(ActionQueueRunner preferredRunner)
        {
            ActionQueueRunner nextRunner = null;
            if (preferredRunner != null)
            {
                foreach (ActionQueueRunner runner in _runners)
                {
                    if (runner == preferredRunner)
                    {
                        nextRunner = preferredRunner;
                        break;
                    }
                }
            }

            if (nextRunner == null && _runners.Count > 0)
                nextRunner = _runners[0];

            if (!ReferenceEquals(_runner, nextRunner))
            {
                UnbindDebugger();
                ResetViewState();
                _runner = nextRunner;
                BindDebugger();
                return;
            }

            _runner = nextRunner;
            BindDebugger();
        }

        private void BindDebugger(bool forceRebind = false)
        {
            if (!_debugBindingEnabled || _runner == null)
            {
                if (forceRebind)
                    UnbindDebugger();
                return;
            }

            ActionQueueDebugService debugger = _runner.Debugger;
            if (!forceRebind && ReferenceEquals(_boundDebugger, debugger) && _recordingLease != null)
                return;

            UnbindDebugger();
            _boundDebugger = debugger;
            _boundDebugger.StateChanged += OnDebuggerStateChanged;
            _recordingLease = _boundDebugger.AcquireRecording();
            _snapshotDirty = true;
        }

        private void UnbindDebugger()
        {
            if (_boundDebugger != null)
                _boundDebugger.StateChanged -= OnDebuggerStateChanged;

            _recordingLease?.Dispose();
            _recordingLease = null;
            _boundDebugger = null;
            _cachedSnapshot = null;
            _snapshotDirty = true;
            _observedDebugVersion = -1;
        }

        private void OnDebuggerStateChanged()
        {
            _snapshotDirty = true;
            _runtimeDebugger?.MarkDirtyRepaint();
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
                runnerBadge.text = _runner == null ? "未连接 Runner" : $"{_runner.gameObject.name}  ·  {_runners.Count} Runner";

            if (actionTypeBadge != null)
            {
                if (currentPage == DebuggerPage.ActionTypes)
                    actionTypeBadge.text = $"{_filteredActionTypes.Count}/{_actionTypes.Count} TYPES";
                else if (currentPage == DebuggerPage.Guide)
                    actionTypeBadge.text = "REFERENCE";
                else
                    actionTypeBadge.text = EditorApplication.isPlaying ? "LIVE" : "OFFLINE";
            }
        }

        private ActionQueueDebugSnapshot GetSnapshot(bool forceRefresh = false)
        {
            if (_runner == null)
                return new ActionQueueDebugSnapshot();

            if (forceRefresh || _snapshotDirty || _cachedSnapshot == null)
            {
                _cachedSnapshot = _runner.GetDebugSnapshot();
                _snapshotDirty = false;
                _observedDebugVersion = _boundDebugger?.Version ?? -1;
            }

            return _cachedSnapshot;
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
                    _overviewScroll = EditorGUILayout.BeginScrollView(_overviewScroll);
                    DrawStringSection("待处理根 Action", snapshot.PendingRoots, "无");
                    DrawStringSection("内部工作队列", snapshot.PendingWorkItems, "队列为空");
                    List<ActionQueueDebugNode> pendingNodes = CollectPendingNodes(snapshot.Roots);
                    DrawNodeSummarySection("待处理 Action / Reactor", pendingNodes);
                    EditorGUILayout.EndScrollView();
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(390f)))
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
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("进入 Play Mode 后，此页面会显示场景中的 ActionQueueRunner。Action 类型目录和阅读指南仍可离线使用。", MessageType.Info);
                return false;
            }

            if (_runner == null)
            {
                EditorGUILayout.HelpBox("当前场景没有 ActionQueueRunner。请在一个 GameObject 上添加该组件。", MessageType.Warning);
                return false;
            }

            snapshot = GetSnapshot();
            return true;
        }

        #region Toolbar

        private void DrawToolbar()
        {
            RemoveDestroyedRunners();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(48f)))
                    RefreshRunners();

                DrawRunnerPopup();
                GUILayout.FlexibleSpace();

                bool breakpoint = _runner != null && _runner.Debugger.BreakpointMode;
                bool nextBreakpoint = GUILayout.Toggle(
                    breakpoint,
                    "断点模式",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(76f));

                if (_runner != null && nextBreakpoint != breakpoint)
                {
                    _runner.Debugger.SetBreakpointMode(nextBreakpoint);
                    _newNodeIds.Clear();
                    _waitingForStepResult = false;
                }

                bool canContinue = _runner != null &&
                                   _runner.Debugger.BreakpointMode &&
                                   _runner.Debugger.IsPaused;
                using (new EditorGUI.DisabledScope(!canContinue))
                {
                    if (GUILayout.Button(
                            "继续下一个节点",
                            EditorStyles.toolbarButton,
                            GUILayout.Width(110f)))
                    {
                        BeginStepTracking(GetSnapshot(forceRefresh: true));
                        _runner.Debugger.ContinueOneNode();
                    }
                }

                Color oldBackground = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.48f, 0.42f);
                using (new EditorGUI.DisabledScope(_runner == null))
                {
                    if (GUILayout.Button("停止并清除", EditorStyles.toolbarButton, GUILayout.Width(86f)))
                    {
                        _runner.StopAndClear();
                        ResetViewState();
                        _snapshotDirty = true;
                    }
                }

                GUI.backgroundColor = oldBackground;
            }
        }

        private void DrawRunnerPopup()
        {
            if (_runners.Count == 0)
            {
                GUILayout.Label("No ActionQueueRunner", EditorStyles.miniLabel);
                return;
            }

            string[] names = new string[_runners.Count];
            int selectedIndex = 0;
            for (int i = 0; i < _runners.Count; i++)
            {
                ActionQueueRunner runner = _runners[i];
                names[i] = $"{runner.gameObject.name} ({runner.GetInstanceID()})";
                if (runner == _runner)
                    selectedIndex = i;
            }

            int nextIndex = EditorGUILayout.Popup(
                selectedIndex,
                names,
                EditorStyles.toolbarPopup,
                GUILayout.MinWidth(180f));

            if (nextIndex != selectedIndex)
            {
                _runner = _runners[nextIndex];
                ResetViewState();
                BindDebugger(forceRebind: true);
            }
        }

        private void DrawStatus(ActionQueueDebugSnapshot snapshot)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 30f);
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(0.12f, 0.13f, 0.15f)
                : new Color(0.86f, 0.88f, 0.91f));

            rect.xMin += 10f;
            rect.xMax -= 10f;
            string chain = snapshot.HasChain
                ? $"Chain {snapshot.ChainId}" +
                  (snapshot.IsLastCompletedChain ? "  ·  上一条，已完成" : string.Empty)
                : "没有可显示的 Chain";

            GUI.Label(new Rect(rect.x, rect.y + 6f, 230f, 18f), chain, EditorStyles.boldLabel);
            GUI.Label(
                new Rect(rect.x + 235f, rect.y + 6f, 150f, 18f),
                $"Action {snapshot.ExecutedActionCount}/{snapshot.MaxActionsPerChain}",
                EditorStyles.miniLabel);

            string status = snapshot.IsPaused
                ? $"● 已暂停  {snapshot.PausedNode}"
                : string.IsNullOrEmpty(snapshot.CurrentNode)
                    ? "空闲"
                    : $"▶ {snapshot.CurrentNode}";

            Color oldColor = GUI.contentColor;
            if (snapshot.IsPaused)
                GUI.contentColor = new Color(1f, 0.72f, 0.2f);
            GUI.Label(
                new Rect(rect.xMax - 420f, rect.y + 6f, 420f, 18f),
                status,
                _rightAlignedMiniLabel);
            GUI.contentColor = oldColor;
        }

        #endregion

        #region Panels

        private void DrawTreePanel(ActionQueueDebugSnapshot snapshot)
        {
            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox,
                       GUILayout.MinWidth(360f),
                       GUILayout.ExpandWidth(true)))
            {
                DrawPanelHeader("完整信息链", "点击节点，在右侧查看详情");
                _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll);

                if (snapshot.Roots.Count == 0)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        "尚无 Action 记录",
                        EditorStyles.centeredGreyMiniLabel,
                        GUILayout.Height(30f));
                    GUILayout.FlexibleSpace();
                }
                else
                {
                    EnsureValidSelection(snapshot.Roots);
                    _ancestorLineBuffer.Clear();
                    for (int i = 0; i < snapshot.Roots.Count; i++)
                    {
                        DrawTreeNode(
                            snapshot.Roots[i],
                            0,
                            i == snapshot.Roots.Count - 1,
                            _ancestorLineBuffer);
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawDetailsPanel(ActionQueueDebugSnapshot snapshot)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(DetailsWidth)))
            {
                DrawPanelHeader("节点详情", "当前观察节点");
                _detailsScroll = EditorGUILayout.BeginScrollView(_detailsScroll);

                ActionQueueDebugNode selected = FindNode(snapshot.Roots, _selectedNodeId);
                if (selected == null)
                {
                    EditorGUILayout.HelpBox(
                        "请在完整信息链中选择一个 Action 或 Reactor。",
                        MessageType.Info);
                    EditorGUILayout.EndScrollView();
                    return;
                }

                DrawSelectedNodeCard(selected);
                DrawDetailField("名称", selected.Name);
                DrawDetailField("类型", selected.Kind.ToString());
                if (selected.ExecutionKind.HasValue)
                {
                    DrawDetailField("执行分类", selected.ExecutionKind.Value.ToString());
                    DrawDetailField("开放钩子", selected.ReactionPhases.ToString());
                }
                DrawDetailField("状态", selected.State.ToString(), GetStateColor(selected.State));
                DrawDetailField("节点 ID", selected.Id.ToString());
                DrawDetailField(
                    "父 Action ID",
                    selected.ParentActionId == 0 ? "Root" : selected.ParentActionId.ToString());
                DrawDetailField("详细信息", EmptyFallback(selected.Detail));
                DrawDetailField("结果", EmptyFallback(selected.Outcome));
                DrawDetailField("子 Action", selected.Children.Count.ToString());
                DrawDetailField("触发 Reactor", selected.Reactors.Count.ToString());

                if (_newNodeIds.Contains(selected.Id))
                {
                    EditorGUILayout.Space(8f);
                    EditorGUILayout.HelpBox(
                        "此节点由刚刚放行的断点步骤新插入。",
                        MessageType.Warning);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private static void DrawPanelHeader(string title, string subtitle)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 38f);
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.17f, 0.20f)
                : new Color(0.78f, 0.81f, 0.86f);
            EditorGUI.DrawRect(rect, background);
            GUI.Label(
                new Rect(rect.x + 9f, rect.y + 4f, rect.width - 18f, 18f),
                title,
                EditorStyles.boldLabel);
            GUI.Label(
                new Rect(rect.x + 9f, rect.y + 21f, rect.width - 18f, 14f),
                subtitle,
                EditorStyles.centeredGreyMiniLabel);
        }

        #endregion


        #region Tree Drawing

        private void DrawTreeNode(
            ActionQueueDebugNode node,
            int depth,
            bool isLastSibling,
            List<bool> ancestorContinues)
        {
            Rect row = EditorGUILayout.GetControlRect(false, TreeRowHeight);
            bool hasDescendants = node.Reactors.Count > 0 || node.Children.Count > 0;
            bool expanded = hasDescendants && GetFoldout(node.Id);
            bool selected = node.Id == _selectedNodeId;
            bool isNew = _newNodeIds.Contains(node.Id);

            DrawNodeBackground(row, node.State, selected, isNew);
            DrawHierarchyLines(row, depth, isLastSibling, ancestorContinues);
            if (expanded)
                DrawChildStem(row, depth);

            float contentX = row.x + 5f + depth * TreeIndent;
            Rect foldoutRect = new Rect(contentX, row.y + 2f, 16f, 18f);
            if (hasDescendants)
            {
                bool nextExpanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none);
                if (nextExpanded != expanded)
                    _foldouts[node.Id] = nextExpanded;
                expanded = nextExpanded;
            }

            Rect selectRect = new Rect(contentX + 17f, row.y, row.xMax - contentX - 17f, row.height);
            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                selectRect.Contains(Event.current.mousePosition))
            {
                _selectedNodeId = node.Id;
                Event.current.Use();
                Repaint();
            }

            Color oldColor = GUI.contentColor;
            GUI.contentColor = GetStateColor(node.State);
            string selectionIcon = selected ? "◉ " : "";
            string kindIcon = node.Kind == ActionQueueDebugNodeKind.Action ? "◆" : "⚡";
            GUI.Label(
                new Rect(selectRect.x + 2f, selectRect.y + 2f, selectRect.width - 106f, 18f),
                $"{selectionIcon}{kindIcon}  {node.Name}",
                selected ? EditorStyles.boldLabel : EditorStyles.label);
            GUI.contentColor = oldColor;

            if (isNew)
                DrawBadge(new Rect(row.xMax - 102f, row.y + 3f, 38f, 16f), "NEW", new Color(1f, 0.58f, 0.12f));

            DrawBadge(
                new Rect(row.xMax - 61f, row.y + 3f, 57f, 16f),
                ShortState(node.State),
                GetStateColor(node.State));

            if (!expanded)
                return;

            int descendantCount = node.Reactors.Count + node.Children.Count;
            int descendantIndex = 0;
            ancestorContinues.Add(!isLastSibling);

            foreach (ActionQueueDebugNode reactor in node.Reactors)
            {
                descendantIndex++;
                DrawTreeNode(
                    reactor,
                    depth + 1,
                    descendantIndex == descendantCount,
                    ancestorContinues);
            }

            foreach (ActionQueueDebugNode child in node.Children)
            {
                descendantIndex++;
                DrawTreeNode(
                    child,
                    depth + 1,
                    descendantIndex == descendantCount,
                    ancestorContinues);
            }

            ancestorContinues.RemoveAt(ancestorContinues.Count - 1);
        }

        private static void DrawHierarchyLines(
            Rect row,
            int depth,
            bool isLastSibling,
            List<bool> ancestorContinues)
        {
            if (Event.current.type != EventType.Repaint || depth == 0)
                return;

            Color lineColor = EditorGUIUtility.isProSkin
                ? new Color(0.44f, 0.47f, 0.52f, 0.9f)
                : new Color(0.34f, 0.38f, 0.44f, 0.9f);

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
            EditorGUI.DrawRect(
                new Rect(branchX, centerY, TreeIndent - 7f, 1f),
                lineColor);
        }

        private static void DrawChildStem(Rect row, int depth)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            Color lineColor = EditorGUIUtility.isProSkin
                ? new Color(0.44f, 0.47f, 0.52f, 0.9f)
                : new Color(0.34f, 0.38f, 0.44f, 0.9f);
            float stemX = row.x + 13f + depth * TreeIndent;
            EditorGUI.DrawRect(
                new Rect(stemX, Mathf.Round(row.center.y), 1f, row.yMax - row.center.y),
                lineColor);
        }

        private static void DrawNodeBackground(
            Rect row,
            ActionQueueDebugNodeState state,
            bool selected,
            bool isNew)
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
            Color background = new Color(color.r, color.g, color.b, 0.22f);
            EditorGUI.DrawRect(rect, background);
            Color oldColor = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(
                rect,
                text,
                _badgeStyle);
            GUI.contentColor = oldColor;
        }

        #endregion

        #region Details and Tracking

        private void BeginStepTracking(ActionQueueDebugSnapshot snapshot)
        {
            _stepBaseline.Clear();
            CollectNodeIds(snapshot.Roots, _stepBaseline);
            _newNodeIds.Clear();
            _waitingForStepResult = true;
        }

        private void UpdateStepTracking(ActionQueueDebugSnapshot snapshot)
        {
            bool chainChanged = snapshot.HasChain != _observedHasChain ||
                                (snapshot.HasChain && snapshot.ChainId != _observedChainId);
            if (chainChanged)
            {
                _observedHasChain = snapshot.HasChain;
                _observedChainId = snapshot.ChainId;
                _stepBaseline.Clear();
                _newNodeIds.Clear();
                _waitingForStepResult = false;
                CollectNodeIds(snapshot.Roots, _stepBaseline);
            }

            if (!_waitingForStepResult)
                return;

            _currentNodeIds.Clear();
            CollectNodeIds(snapshot.Roots, _currentNodeIds);
            _newNodeIds.Clear();
            foreach (long id in _currentNodeIds)
            {
                if (!_stepBaseline.Contains(id))
                    _newNodeIds.Add(id);
            }

            if (snapshot.IsPaused || _runner == null || !_runner.IsRunning)
                _waitingForStepResult = false;
        }

        private void EnsureValidSelection(List<ActionQueueDebugNode> roots)
        {
            if (FindNode(roots, _selectedNodeId) != null)
                return;

            _selectedNodeId = roots.Count > 0 ? roots[0].Id : 0;
        }

        private void ResetViewState()
        {
            _selectedNodeId = 0;
            _observedChainId = 0;
            _observedHasChain = false;
            _waitingForStepResult = false;
            _foldouts.Clear();
            _stepBaseline.Clear();
            _newNodeIds.Clear();
            _cachedSnapshot = null;
            _snapshotDirty = true;
        }

        private static void DrawSelectedNodeCard(ActionQueueDebugNode node)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 58f);
            Color stateColor = GetStateColor(node.State);
            EditorGUI.DrawRect(rect, new Color(stateColor.r, stateColor.g, stateColor.b, 0.13f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), stateColor);
            GUI.Label(
                new Rect(rect.x + 12f, rect.y + 7f, rect.width - 20f, 20f),
                $"◉  {node.Name}",
                EditorStyles.boldLabel);
            GUI.Label(
                new Rect(rect.x + 12f, rect.y + 31f, rect.width - 20f, 18f),
                $"{node.Kind}  ·  #{node.Id}",
                EditorStyles.miniLabel);
        }

        private static void DrawDetailField(string label, string value, Color? valueColor = null)
        {
            EditorGUILayout.Space(7f);
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            Color oldColor = GUI.contentColor;
            if (valueColor.HasValue)
                GUI.contentColor = valueColor.Value;
            EditorGUILayout.SelectableLabel(
                value,
                EditorStyles.wordWrappedLabel,
                GUILayout.MinHeight(EditorGUIUtility.singleLineHeight));
            GUI.contentColor = oldColor;
        }

        private static void DrawNodeSummarySection(
            string title,
            List<ActionQueueDebugNode> nodes)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (nodes.Count == 0)
            {
                EditorGUILayout.LabelField("无", EditorStyles.miniLabel);
                return;
            }

            foreach (ActionQueueDebugNode node in nodes)
            {
                Color oldColor = GUI.contentColor;
                GUI.contentColor = GetStateColor(node.State);
                string kind = node.Kind == ActionQueueDebugNodeKind.Action ? "◆" : "⚡";
                EditorGUILayout.LabelField($"{kind} {node.Name}", EditorStyles.miniLabel);
                GUI.contentColor = oldColor;
            }
        }

        private void DrawActionKindSummary(List<ActionQueueDebugNode> roots)
        {
            int command = 0;
            int signal = 0;
            int composite = 0;
            int noHooks = 0;
            _traversalStack.Clear();
            foreach (ActionQueueDebugNode root in roots)
                _traversalStack.Push(root);
            while (_traversalStack.Count > 0)
            {
                ActionQueueDebugNode node = _traversalStack.Pop();
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
                foreach (ActionQueueDebugNode reactor in node.Reactors)
                    _traversalStack.Push(reactor);
                foreach (ActionQueueDebugNode child in node.Children)
                    _traversalStack.Push(child);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("实际调度 Action 分类", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"Command {command}  ·  Signal {signal}  ·  Composite {composite}  ·  Hooks None {noHooks}",
                EditorStyles.wordWrappedMiniLabel);
        }

        private static void DrawStringSection(string title, List<string> values, string emptyText)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (values.Count == 0)
            {
                EditorGUILayout.LabelField(emptyText, EditorStyles.miniLabel);
                return;
            }

            foreach (string value in values)
                EditorGUILayout.LabelField("• " + value, EditorStyles.wordWrappedMiniLabel);
        }

        private List<ActionQueueDebugNode> CollectPendingNodes(
            List<ActionQueueDebugNode> roots)
        {
            _pendingNodeBuffer.Clear();
            _traversalStack.Clear();
            for (int i = roots.Count - 1; i >= 0; i--)
                _traversalStack.Push(roots[i]);

            while (_traversalStack.Count > 0)
            {
                ActionQueueDebugNode node = _traversalStack.Pop();
                if (node.State == ActionQueueDebugNodeState.Queued ||
                    node.State == ActionQueueDebugNodeState.Executing)
                    _pendingNodeBuffer.Add(node);

                for (int i = node.Children.Count - 1; i >= 0; i--)
                    _traversalStack.Push(node.Children[i]);
                for (int i = node.Reactors.Count - 1; i >= 0; i--)
                    _traversalStack.Push(node.Reactors[i]);
            }

            return _pendingNodeBuffer;
        }

        private void CollectNodeIds(
            List<ActionQueueDebugNode> roots,
            HashSet<long> result)
        {
            _traversalStack.Clear();
            foreach (ActionQueueDebugNode root in roots)
                _traversalStack.Push(root);

            while (_traversalStack.Count > 0)
            {
                ActionQueueDebugNode node = _traversalStack.Pop();
                result.Add(node.Id);
                foreach (ActionQueueDebugNode reactor in node.Reactors)
                    _traversalStack.Push(reactor);
                foreach (ActionQueueDebugNode child in node.Children)
                    _traversalStack.Push(child);
            }
        }

        private ActionQueueDebugNode FindNode(
            List<ActionQueueDebugNode> roots,
            long id)
        {
            if (id == 0)
                return null;

            _traversalStack.Clear();
            foreach (ActionQueueDebugNode root in roots)
                _traversalStack.Push(root);

            while (_traversalStack.Count > 0)
            {
                ActionQueueDebugNode node = _traversalStack.Pop();
                if (node.Id == id)
                    return node;

                foreach (ActionQueueDebugNode reactor in node.Reactors)
                    _traversalStack.Push(reactor);
                foreach (ActionQueueDebugNode child in node.Children)
                    _traversalStack.Push(child);
            }

            return null;
        }

        private void EnsureStyles()
        {
            _rightAlignedMiniLabel ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight
            };
            _badgeStyle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
        }

        private bool GetFoldout(long id)
        {
            return !_foldouts.TryGetValue(id, out bool expanded) || expanded;
        }

        private static string EmptyFallback(string value)
        {
            return string.IsNullOrEmpty(value) ? "—" : value;
        }

        private static string ShortState(ActionQueueDebugNodeState state)
        {
            return state switch
            {
                ActionQueueDebugNodeState.Queued => "QUEUED",
                ActionQueueDebugNodeState.Executing => "ACTIVE",
                ActionQueueDebugNodeState.Resolved => "DONE",
                ActionQueueDebugNodeState.Skipped => "SKIP",
                _ => state.ToString().ToUpperInvariant()
            };
        }

        private static Color GetStateColor(ActionQueueDebugNodeState state)
        {
            return state switch
            {
                ActionQueueDebugNodeState.Queued => new Color(1f, 0.72f, 0.2f),
                ActionQueueDebugNodeState.Executing => new Color(0.22f, 0.78f, 1f),
                ActionQueueDebugNodeState.Resolved => new Color(0.32f, 0.9f, 0.48f),
                ActionQueueDebugNodeState.Skipped => new Color(0.58f, 0.6f, 0.64f),
                _ => Color.white
            };
        }

        #endregion

        private enum DebuggerPage
        {
            Overview,
            ExecutionChain,
            ActionTypes,
            Guide
        }

        private enum ActionTypeFilter
        {
            All,
            Command,
            Composite,
            Signal
        }

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
