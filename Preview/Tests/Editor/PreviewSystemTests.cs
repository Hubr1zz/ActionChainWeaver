using System;
using NUnit.Framework;

namespace GameFramework.Preview.Tests
{
    public sealed class PreviewSystemTests
    {
        [Test]
        public void PreviewPipeline_EvaluatesWithoutChangingRegistrationOrder()
        {
            var pipeline = new PreviewPipeline<int, int>(value => value);
            pipeline.Register(new AddRule("late", 0, 2));
            pipeline.Register(new AddRule("early", 10, 3));

            PreviewResult<int> result = pipeline.Evaluate(1);

            Assert.That(result.Value, Is.EqualTo(6));
            Assert.That(result.Trace.Entries[0].RuleId, Is.EqualTo("early"));
            Assert.That(result.Trace.Entries[1].RuleId, Is.EqualTo("late"));
        }

        [Test]
        public void SimulationPreview_MaxDepthLimitsLevels()
        {
            var preview = new SimulationPreview<int>(new CountdownExpander());

            SimulationPreviewResult<int> result = preview.Build(
                3,
                new SimulationPreviewOptions { MaxDepth = 1 });

            Assert.That(result.NodeCount, Is.EqualTo(2));
            Assert.That(result.Root.Children[0].Cutoff, Is.EqualTo(SimulationCutoff.DepthLimit));
        }

        [Test]
        public void SimulationPreview_DefaultShowsExactlyOneChildLevel()
        {
            var preview = new SimulationPreview<int>(new CountdownExpander());

            SimulationPreviewResult<int> result = preview.Build(3);

            Assert.That(result.NodeCount, Is.EqualTo(2));
            Assert.That(result.Root.Children, Has.Count.EqualTo(1));
            Assert.That(result.Root.Children[0].Children, Is.Empty);
        }

        [Test]
        public void SimulationPreview_MinusOneExpandsToLeaf()
        {
            var preview = new SimulationPreview<int>(new CountdownExpander());

            SimulationPreviewResult<int> result = preview.Build(
                3,
                new SimulationPreviewOptions { MaxDepth = -1 });

            Assert.That(result.NodeCount, Is.EqualTo(4));
            Assert.That(result.WasTruncated, Is.False);
        }

        [Test]
        public void SimulationPreview_NodeBudgetStillProtectsUnlimitedDepth()
        {
            var preview = new SimulationPreview<int>(new CountdownExpander());

            SimulationPreviewResult<int> result = preview.Build(
                100,
                new SimulationPreviewOptions { MaxDepth = -1, MaxNodes = 3 });

            Assert.That(result.NodeCount, Is.EqualTo(3));
            Assert.That(result.WasTruncated, Is.True);
        }

        [Test]
        public void SimulationPreview_UncertaintyStopsOnlyThatBranchWithReason()
        {
            var preview = new SimulationPreview<int>(new UncertainExpander());

            SimulationPreviewResult<int> result = preview.Build(1);
            SimulationPreviewNode<int> uncertain = result.Root.Children[0];

            Assert.That(uncertain.Cutoff, Is.EqualTo(SimulationCutoff.Uncertainty));
            Assert.That(uncertain.Uncertainty.Kind, Is.EqualTo(SimulationUncertaintyKind.PlayerInput));
            Assert.That(uncertain.Uncertainty.Message, Does.Contain("选择"));
            Assert.That(uncertain.Children, Is.Empty);
        }

        [Test]
        public void PlayerPreviewFormatter_ShowsTriggersAndDirectNumericChangesOnly()
        {
            var preview = new SimulationPreview<int>(new DisclosureExpander());

            SimulationPreviewResult<int> result = preview.Build(99);
            var lines = PlayerPreviewFormatter.Format(result);

            Assert.That(lines, Has.Count.EqualTo(4));
            Assert.That(lines[0].Kind, Is.EqualTo(PreviewDisclosureKind.Trigger));
            Assert.That(lines[0].Text, Does.Contain("燃烧"));
            Assert.That(lines[1].Text, Does.Contain("50% 概率"));
            Assert.That(lines[2].Note, Does.Contain("等待玩家输入"));
            Assert.That(lines[3].Kind, Is.EqualTo(PreviewDisclosureKind.NumericChange));
            Assert.That(lines[3].Text, Is.EqualTo("伤害减少 50%"));
            foreach (PlayerPreviewLine line in lines)
                Assert.That(line.Text, Does.Not.Contain("隐藏的二级结果"));
        }

        [Test]
        public void PlayerPreviewFormatter_DoesNotLeakSecondLevelEvenFromDebugTree()
        {
            var preview = new SimulationPreview<int>(new DisclosureExpander());
            SimulationPreviewResult<int> debugTree = preview.Build(
                99,
                new SimulationPreviewOptions { MaxDepth = -1 });

            Assert.That(debugTree.Root.Children[0].Children[0].Label,
                Is.EqualTo("隐藏的二级结果"));

            var playerLines = PlayerPreviewFormatter.Format(debugTree);
            foreach (PlayerPreviewLine line in playerLines)
                Assert.That(line.Text, Does.Not.Contain("隐藏的二级结果"));
        }

        private sealed class AddRule : IPreviewRule<int, int>
        {
            private readonly int _amount;

            public AddRule(string id, int priority, int amount)
            {
                Id = id;
                Priority = priority;
                _amount = amount;
            }

            public string Id { get; }
            public int Priority { get; }

            public int Evaluate(int input, int current, PreviewTrace trace)
            {
                trace.Add(Id, _amount.ToString());
                return current + _amount;
            }
        }

        private sealed class CountdownExpander : ISimulationExpander<int>
        {
            public SimulationExpansion<int> Expand(int node)
            {
                return new SimulationExpansion<int>(
                    node.ToString(),
                    "countdown",
                    node > 0 ? new[] { node - 1 } : Array.Empty<int>());
            }
        }

        private sealed class UncertainExpander : ISimulationExpander<int>
        {
            public SimulationExpansion<int> Expand(int node)
            {
                if (node == 0)
                {
                    return new SimulationExpansion<int>(
                        "选择后续目标",
                        "尚未选择",
                        uncertainty: new SimulationUncertainty(
                            SimulationUncertaintyKind.PlayerInput,
                            "需要玩家继续选择目标。"));
                }
                return new SimulationExpansion<int>("root", "", new[] { 0 });
            }
        }

        private sealed class DisclosureExpander : ISimulationExpander<int>
        {
            public SimulationExpansion<int> Expand(int node)
            {
                return node switch
                {
                    99 => new SimulationExpansion<int>("造成 80 点伤害", "", new[] { 1, 2, 3, 4 }),
                    1 => new SimulationExpansion<int>(
                        "燃烧", "", new[] { 10 },
                        disclosure: PreviewDisclosure.Trigger("目标获得 2 层燃烧将会触发")),
                    2 => new SimulationExpansion<int>(
                        "随机追加攻击", "", new[] { 20 },
                        new SimulationUncertainty(
                            SimulationUncertaintyKind.RandomOutcome,
                            "随机结果不会预先展示。"),
                        PreviewDisclosure.Trigger("50% 概率追加一次攻击将会触发")),
                    3 => new SimulationExpansion<int>(
                        "选择目标", "", new[] { 30 },
                        new SimulationUncertainty(
                            SimulationUncertaintyKind.PlayerInput,
                            "等待玩家输入。"),
                        PreviewDisclosure.Trigger("选择另一名目标将会触发")),
                    4 => new SimulationExpansion<int>(
                        "减伤", "",
                        disclosure: PreviewDisclosure.NumericChange("伤害减少 50%")),
                    _ => new SimulationExpansion<int>("隐藏的二级结果", "")
                };
            }
        }
    }
}
