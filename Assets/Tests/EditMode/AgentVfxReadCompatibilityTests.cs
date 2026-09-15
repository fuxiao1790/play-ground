using System.Linq;
using System.Reflection;
using AgentVFX.Editor;
using NUnit.Framework;
using Unity.Pipeline.Commands;

namespace PlayGround.Tests.EditMode
{
    public class AgentVfxReadCompatibilityTests
    {
        private const string GraphPath = "Assets/Vfx/LineSeg/MagicBoltTrail.vfx";

        [Test]
        public void ReadGraph_ReportsNodesSlotsAndFlow()
        {
            var graph = AgentVfxApi.ReadGraph(GraphPath);

            Assert.AreEqual(GraphPath, graph.asset);
            Assert.That(graph.nodes, Is.Not.Empty);
            Assert.That(graph.slots, Is.Not.Empty);
            Assert.That(graph.nodes.Any(node => node.kind == "Context"), Is.True);
            Assert.That(graph.slots.All(slot => graph.nodes.Any(node => node.id == slot.nodeId)), Is.True);
            Assert.That(graph.slots.Where(slot => slot.parentSlotId != null)
                .All(slot => graph.slots.Any(parent => parent.id == slot.parentSlotId)), Is.True);
        }

        [Test]
        public void ReadGraph_ReportsSettingsAndSlotValues()
        {
            var graph = AgentVfxApi.ReadGraph(GraphPath);

            Assert.That(graph.nodes.Any(node => node.settings != null), Is.True);
            Assert.That(graph.slots.All(slot => slot.valueJson != null), Is.True);
        }

        [Test]
        public void ReadSlot_UsesGraphSnapshotId()
        {
            var graph = AgentVfxApi.ReadGraph(GraphPath);
            var expected = graph.slots.First();

            var slot = AgentVfxApi.ReadSlot(expected.id);

            Assert.AreEqual(expected.id, slot.id);
            Assert.AreEqual(expected.nodeId, slot.nodeId);
        }

        [Test]
        public void ReadGraph_RejectsAssetOutsideVfxRoot()
        {
            Assert.Throws<System.InvalidOperationException>(() =>
                AgentVfxApi.ReadGraph("Assets/Tests/not-a-vfx-graph.vfx"));
        }

        [Test]
        public void Commands_ExposeOnlyReadOperations()
        {
            var names = typeof(AgentVfxCommands)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<CliCommandAttribute>()?.Name)
                .Where(name => name != null)
                .OrderBy(name => name)
                .ToArray();

            CollectionAssert.AreEquivalent(new[]
            {
                "vfx_blackboard_read",
                "vfx_graph_read",
                "vfx_ping",
                "vfx_slot_read",
                "vfx_type_describe",
                "vfx_types_list",
            }, names);
        }
    }
}
