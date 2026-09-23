using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GameFramework.Preview
{
    /// <summary>
    /// 把一个不可变的模拟节点展开成描述和后继节点。实现负责复制模拟状态，禁止执行真实 Action。
    /// </summary>
    public interface ISimulationExpander<TNode>
    {
        SimulationExpansion<TNode> Expand(TNode node);
    }

    public readonly struct SimulationExpansion<TNode>
    {
        public SimulationExpansion(
            string label,
            string summary,
            IReadOnlyList<TNode> children = null,
            SimulationUncertainty uncertainty = default,
            PreviewDisclosure disclosure = default)
        {
            Label = label ?? string.Empty;
            Summary = summary ?? string.Empty;
            Uncertainty = uncertainty;
            Disclosure = disclosure;
            if (children == null || children.Count == 0)
            {
                Children = Array.Empty<TNode>();
            }
            else
            {
                var snapshot = new TNode[children.Count];
                for (int i = 0; i < children.Count; i++)
                    snapshot[i] = children[i];
                Children = snapshot;
            }
        }

        public string Label { get; }
        public string Summary { get; }
        public IReadOnlyList<TNode> Children { get; }
        public SimulationUncertainty Uncertainty { get; }
        public PreviewDisclosure Disclosure { get; }
    }

    public enum PreviewDisclosureKind
    {
        Trigger,
        NumericChange
    }

    /// <summary>
    /// 玩家界面应该披露什么。由领域 Expander 明确声明，不从 Action 实现细节中猜测。
    /// Trigger 只说明会触发；NumericChange 显示已经确定的一阶数值修正。
    /// </summary>
    public readonly struct PreviewDisclosure
    {
        public PreviewDisclosure(PreviewDisclosureKind kind, string text)
        {
            Kind = kind;
            Text = text ?? string.Empty;
            IsSpecified = true;
        }

        public PreviewDisclosureKind Kind { get; }
        public string Text { get; }
        public bool IsSpecified { get; }

        public static PreviewDisclosure Trigger(string text) =>
            new(PreviewDisclosureKind.Trigger, text);

        public static PreviewDisclosure NumericChange(string text) =>
            new(PreviewDisclosureKind.NumericChange, text);
    }

    public enum SimulationUncertaintyKind
    {
        None,
        RandomOutcome,
        PlayerInput,
        NetworkResult,
        HiddenInformation,
        ExternalState,
        ProjectDefined
    }

    /// <summary>
    /// 由领域模拟器在遇到不应向玩家披露后续结果的节点时显式返回。
    /// 随机节点无论是否有固定种子都视为披露边界；PreviewSystem 不反射检查实现代码。
    /// </summary>
    public readonly struct SimulationUncertainty
    {
        public SimulationUncertainty(SimulationUncertaintyKind kind, string message)
        {
            if (kind == SimulationUncertaintyKind.None)
                throw new ArgumentException("Use default for a deterministic expansion.", nameof(kind));
            Kind = kind;
            Message = message ?? string.Empty;
        }

        public SimulationUncertaintyKind Kind { get; }
        public string Message { get; }
        public bool IsUncertain => Kind != SimulationUncertaintyKind.None;
    }

    public enum SimulationCutoff
    {
        None,
        DepthLimit,
        NodeLimit,
        Uncertainty
    }

    public sealed class SimulationPreviewOptions
    {
        private int _maxDepth = 1;
        private int _maxNodes = 1024;

        /// <summary>
        /// 玩家预览默认只展开一级子节点。-1 保留给开发调试；0 表示只生成根节点。
        /// </summary>
        public int MaxDepth
        {
            get => _maxDepth;
            set => _maxDepth = value >= -1
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "MaxDepth must be -1 or greater.");
        }

        /// <summary>即使 MaxDepth=-1，也用节点预算阻止循环或无限生成。</summary>
        public int MaxNodes
        {
            get => _maxNodes;
            set => _maxNodes = value >= 1
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "MaxNodes must be positive.");
        }
    }

    public sealed class SimulationPreviewNode<TNode>
    {
        private readonly List<SimulationPreviewNode<TNode>> _children = new();
        private readonly ReadOnlyCollection<SimulationPreviewNode<TNode>> _view;

        internal SimulationPreviewNode(TNode value, int depth, string label, string summary)
        {
            Value = value;
            Depth = depth;
            Label = label;
            Summary = summary;
            _view = _children.AsReadOnly();
        }

        public TNode Value { get; }
        public int Depth { get; }
        public string Label { get; }
        public string Summary { get; }
        public PreviewDisclosure Disclosure { get; internal set; }
        public SimulationCutoff Cutoff { get; internal set; }
        public SimulationUncertainty Uncertainty { get; internal set; }
        public IReadOnlyList<SimulationPreviewNode<TNode>> Children => _view;

        internal void AddChild(SimulationPreviewNode<TNode> child) => _children.Add(child);
    }

    public readonly struct SimulationPreviewResult<TNode>
    {
        internal SimulationPreviewResult(SimulationPreviewNode<TNode> root, int nodeCount)
        {
            Root = root;
            NodeCount = nodeCount;
        }

        public SimulationPreviewNode<TNode> Root { get; }
        public int NodeCount { get; }
        public bool WasTruncated => ContainsCutoff(Root);

        private static bool ContainsCutoff(SimulationPreviewNode<TNode> node)
        {
            var pending = new Stack<SimulationPreviewNode<TNode>>();
            pending.Push(node);
            while (pending.Count > 0)
            {
                SimulationPreviewNode<TNode> current = pending.Pop();
                if (current.Cutoff != SimulationCutoff.None)
                    return true;
                foreach (SimulationPreviewNode<TNode> child in current.Children)
                    pending.Push(child);
            }
            return false;
        }
    }

    public readonly struct PlayerPreviewLine
    {
        public PlayerPreviewLine(
            PreviewDisclosureKind kind,
            string text,
            string note = null)
        {
            Kind = kind;
            Text = text ?? string.Empty;
            Note = note ?? string.Empty;
        }

        public PreviewDisclosureKind Kind { get; }
        public string Text { get; }
        public string Note { get; }
    }

    /// <summary>
    /// 把模拟树收敛成玩家可见的一阶说明。它只读取根的直接子节点，绝不显示二级后代。
    /// </summary>
    public static class PlayerPreviewFormatter
    {
        public static IReadOnlyList<PlayerPreviewLine> Format<TNode>(
            SimulationPreviewResult<TNode> preview)
        {
            var lines = new List<PlayerPreviewLine>(preview.Root.Children.Count);
            foreach (SimulationPreviewNode<TNode> child in preview.Root.Children)
            {
                PreviewDisclosure disclosure = child.Disclosure.IsSpecified
                    ? child.Disclosure
                    : PreviewDisclosure.Trigger($"{child.Label}将会触发");
                lines.Add(new PlayerPreviewLine(
                    disclosure.Kind,
                    disclosure.Text,
                    child.Uncertainty.IsUncertain ? child.Uncertainty.Message : null));
            }
            return lines.AsReadOnly();
        }
    }

    /// <summary>
    /// 只展开显式模拟模型，不执行 ActionQueue，也不会自动运行 Reactor。
    /// 若要预览 Reactor，项目 Adapter 必须把对应规则显式建模进 Expander。
    /// </summary>
    public sealed class SimulationPreview<TNode>
    {
        private readonly ISimulationExpander<TNode> _expander;

        public SimulationPreview(ISimulationExpander<TNode> expander)
        {
            _expander = expander ?? throw new ArgumentNullException(nameof(expander));
        }

        public SimulationPreviewResult<TNode> Build(
            TNode root,
            SimulationPreviewOptions options = null)
        {
            options ??= new SimulationPreviewOptions();
            SimulationExpansion<TNode> rootExpansion = _expander.Expand(root);
            var rootNode = new SimulationPreviewNode<TNode>(
                root, 0, rootExpansion.Label, rootExpansion.Summary);
            rootNode.Disclosure = rootExpansion.Disclosure;
            int count = 1;

            if (rootExpansion.Uncertainty.IsUncertain)
            {
                rootNode.Uncertainty = rootExpansion.Uncertainty;
                rootNode.Cutoff = SimulationCutoff.Uncertainty;
                return new SimulationPreviewResult<TNode>(rootNode, count);
            }

            if (rootExpansion.Children.Count == 0)
                return new SimulationPreviewResult<TNode>(rootNode, count);
            if (options.MaxDepth == 0)
            {
                rootNode.Cutoff = SimulationCutoff.DepthLimit;
                return new SimulationPreviewResult<TNode>(rootNode, count);
            }

            var pending = new Stack<ExpansionFrame>();
            pending.Push(new ExpansionFrame(rootNode, rootExpansion.Children));
            while (pending.Count > 0)
            {
                ExpansionFrame frame = pending.Peek();
                if (frame.NextIndex >= frame.Children.Count)
                {
                    pending.Pop();
                    continue;
                }

                if (count >= options.MaxNodes)
                {
                    frame.Node.Cutoff = SimulationCutoff.NodeLimit;
                    break;
                }

                TNode childValue = frame.Children[frame.NextIndex++];
                SimulationExpansion<TNode> childExpansion = _expander.Expand(childValue);
                var childNode = new SimulationPreviewNode<TNode>(
                    childValue,
                    frame.Node.Depth + 1,
                    childExpansion.Label,
                    childExpansion.Summary);
                childNode.Disclosure = childExpansion.Disclosure.IsSpecified
                    ? childExpansion.Disclosure
                    : PreviewDisclosure.Trigger($"{childExpansion.Label}将会触发");
                frame.Node.AddChild(childNode);
                count++;

                if (childExpansion.Uncertainty.IsUncertain)
                {
                    childNode.Uncertainty = childExpansion.Uncertainty;
                    childNode.Cutoff = SimulationCutoff.Uncertainty;
                    continue;
                }

                if (childExpansion.Children.Count == 0)
                    continue;
                if (options.MaxDepth >= 0 && childNode.Depth >= options.MaxDepth)
                {
                    childNode.Cutoff = SimulationCutoff.DepthLimit;
                    continue;
                }
                pending.Push(new ExpansionFrame(childNode, childExpansion.Children));
            }

            return new SimulationPreviewResult<TNode>(rootNode, count);
        }

        private sealed class ExpansionFrame
        {
            public ExpansionFrame(
                SimulationPreviewNode<TNode> node,
                IReadOnlyList<TNode> children)
            {
                Node = node;
                Children = children;
            }

            public SimulationPreviewNode<TNode> Node { get; }
            public IReadOnlyList<TNode> Children { get; }
            public int NextIndex { get; set; }
        }
    }
}
