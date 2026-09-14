using System;
using System.IO;
using System.Linq;
using AgentVFX.Editor;
using NUnit.Framework;
using UnityEditor;

namespace PlayGround.Tests.EditMode
{
    // Guide §27: after any Unity/VFX Graph upgrade, this class is the fast health
    // check for the AgentVFX bridge (Assets/AgentVFX/InternalAccess/). Per
    // Docs/project-overview.md, agents write and never run these -- run in the
    // Unity EditMode test runner and export results per Docs/testing.md.
    public class AgentVfxCompatibilityTests
    {
        private const string TestDir = "Assets/AgentGenerated/__AgentVfxCompatibilityTests__";
        private string _assetPath;

        [SetUp]
        public void SetUp()
        {
            var absoluteDir = Path.Combine(Directory.GetCurrentDirectory(), TestDir);
            Directory.CreateDirectory(absoluteDir);

            _assetPath = $"{TestDir}/CompatTest_{Guid.NewGuid():N}.vfx";
            AgentVfxApi.CreateGraph(_assetPath);
            AssetDatabase.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestDir))
                AssetDatabase.DeleteAsset(TestDir);
        }

        // A copy of Assets/Vfx/LineSeg/MagicBoltTrail.vfx, imported under
        // AgentGenerated (own GUID, decoupled from the production asset) via
        // `unity command import_asset`. Tests must never point at project
        // content directly -- a bridge bug or a bad test assumption could
        // mutate/delete real work. This fixture exists specifically to exercise
        // shapes a bridge-created throwaway graph wouldn't have: compound
        // Vector3 slots, nested SetAttribute blocks, VFXAttributeParameter/
        // SampleBuffer operator chains, multiple wired-together contexts.
        private const string RealGraphFixturePath = "Assets/AgentGenerated/TestFixtures/MagicBoltTrail.vfx";

        [Test]
        public void CanReadRealProductionGraph()
        {
            var graph = AgentVfxApi.ReadGraph(RealGraphFixturePath);

            Assert.AreEqual(RealGraphFixturePath, graph.asset);
            Assert.IsTrue(graph.nodes.Length > 0, "Expected at least one node.");
            Assert.IsTrue(graph.connections.Length > 0, "Expected at least one connection.");
            Assert.IsTrue(
                graph.nodes.Any(n => n.kind == "Context" && n.displayName.Contains("Initialize")),
                "Expected an Initialize Particle context.");
            Assert.IsTrue(
                graph.nodes.Where(n => n.kind == "Block").All(b => !string.IsNullOrEmpty(b.parentId)),
                "Every Block should have a parent context id.");
        }

        [Test]
        public void CanAccessVfxInternals()
        {
            StringAssert.Contains("VFXGraph", AgentVfxApi.Ping());
        }

        [Test]
        public void CanOpenGraph()
        {
            var graph = AgentVfxApi.ReadGraph(_assetPath);
            Assert.AreEqual(_assetPath, graph.asset);
        }

        [Test]
        public void CanEnumerateGraph()
        {
            var graph = AgentVfxApi.ReadGraph(_assetPath);
            Assert.IsNotNull(graph.nodes);
            Assert.IsNotNull(graph.connections);
        }

        [Test]
        public void CanListNodeTypes()
        {
            var types = AgentVfxApi.ListNodeTypes();
            Assert.IsTrue(types.Length > 0);
            Assert.IsTrue(types.Any(t => t.kind == "Context"));
            Assert.IsTrue(types.Any(t => t.kind == "Block"));
            Assert.IsTrue(types.Any(t => t.kind == "Operator"));
        }

        [Test]
        public void CanCreateContext()
        {
            var contextType = AgentVfxApi.ListNodeTypes().First(t => t.kind == "Context");
            var nodeId = AgentVfxApi.CreateNode(_assetPath, contextType.id, null, 0f, 0f);

            var node = AgentVfxApi.ReadGraph(_assetPath).nodes.Single(n => n.id == nodeId);
            Assert.AreEqual("Context", node.kind);
        }

        [Test]
        public void CanCreateBlock()
        {
            // Not every block type is valid inside every context type (VFX Graph
            // enforces this itself), so probe pairings via the actual API rather
            // than assuming the first context/block found are compatible.
            var contexts = AgentVfxApi.ListNodeTypes().Where(t => t.kind == "Context").ToArray();
            var blocks = AgentVfxApi.ListNodeTypes().Where(t => t.kind == "Block").ToArray();

            string contextNodeId = null;
            string blockNodeId = null;

            foreach (var c in contexts)
            {
                var cNodeId = AgentVfxApi.CreateNode(_assetPath, c.id, null, 0f, 0f);

                foreach (var b in blocks)
                {
                    try
                    {
                        blockNodeId = AgentVfxApi.CreateNode(_assetPath, b.id, cNodeId, 0f, 0f);
                        contextNodeId = cNodeId;
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        // Incompatible pairing -- vfx_node_create correctly rejected
                        // it (see AgentVfxNodeOps.CreateNode); try the next block.
                    }
                }

                if (blockNodeId != null)
                    break;

                AgentVfxApi.DeleteNode(cNodeId);
            }

            Assert.IsNotNull(blockNodeId, "Could not find any compatible context/block pairing in this VFX Graph version.");

            var block = AgentVfxApi.ReadGraph(_assetPath).nodes.Single(n => n.id == blockNodeId);
            Assert.AreEqual("Block", block.kind);
            Assert.AreEqual(contextNodeId, block.parentId);
        }

        [Test]
        public void CanCreateOperator()
        {
            var operatorType = AgentVfxApi.ListNodeTypes().First(t => t.kind == "Operator");
            var nodeId = AgentVfxApi.CreateNode(_assetPath, operatorType.id, null, 0f, 0f);

            var node = AgentVfxApi.ReadGraph(_assetPath).nodes.Single(n => n.id == nodeId);
            Assert.AreEqual("Operator", node.kind);
        }

        [Test]
        public void CanConnectCompatibleSlots()
        {
            // Two instances of the exact same operator type always have
            // matching slot[i] types, so this avoids guessing at real type
            // names in this package version.
            var operatorType = AgentVfxApi.ListNodeTypes()
                .Where(t => t.kind == "Operator")
                .FirstOrDefault(t =>
                {
                    var detail = AgentVfxApi.DescribeNodeType(t.id);
                    return detail.inputNames.Length > 0 && detail.outputNames.Length > 0;
                });

            if (operatorType == null)
            {
                Assert.Ignore("No operator type with both inputs and outputs found in this VFX Graph version.");
                return;
            }

            var a = AgentVfxApi.CreateNode(_assetPath, operatorType.id, null, 0f, 0f);
            var b = AgentVfxApi.CreateNode(_assetPath, operatorType.id, null, 200f, 0f);

            var nodeA = AgentVfxApi.ReadGraph(_assetPath).nodes.Single(n => n.id == a);
            var nodeB = AgentVfxApi.ReadGraph(_assetPath).nodes.Single(n => n.id == b);

            var result = AgentVfxApi.ConnectSlots(nodeA.outputSlotIds[0], nodeB.inputSlotIds[0]);
            Assert.IsTrue(result.success, result.message);

            var slot = AgentVfxApi.ReadSlot(nodeB.inputSlotIds[0]);
            Assert.IsTrue(slot.hasLink);
        }

        [Test]
        public void RejectsInvalidConnection()
        {
            var operators = AgentVfxApi.ListNodeTypes().Where(t => t.kind == "Operator").ToArray();

            (string typeId, string outputTypeName)? source = null;
            (string typeId, string inputTypeName)? target = null;

            foreach (var t in operators)
            {
                var detail = AgentVfxApi.DescribeNodeType(t.id);
                if (source == null && detail.outputNames.Length > 0)
                    source = (t.id, null);
                if (target == null && detail.inputNames.Length > 0)
                    target = (t.id, null);
            }

            if (source == null || target == null)
            {
                Assert.Ignore("Could not find two operator types with mismatched slots to test against.");
                return;
            }

            var a = AgentVfxApi.CreateNode(_assetPath, source.Value.typeId, null, 0f, 0f);
            var b = AgentVfxApi.CreateNode(_assetPath, target.Value.typeId, null, 200f, 0f);
            var nodeA = AgentVfxApi.ReadGraph(_assetPath).nodes.Single(n => n.id == a);
            var nodeB = AgentVfxApi.ReadGraph(_assetPath).nodes.Single(n => n.id == b);

            var outSlot = AgentVfxApi.ReadSlot(nodeA.outputSlotIds[0]);
            var inSlot = AgentVfxApi.ReadSlot(nodeB.inputSlotIds[0]);

            if (outSlot.typeName == inSlot.typeName)
            {
                Assert.Ignore("Picked operators happened to share a slot type; re-run or pick explicit types.");
                return;
            }

            var result = AgentVfxApi.ConnectSlots(nodeA.outputSlotIds[0], nodeB.inputSlotIds[0]);
            Assert.IsFalse(result.success);
        }

        [Test]
        public void CanSetValue()
        {
            var operatorType = AgentVfxApi.ListNodeTypes()
                .Where(t => t.kind == "Operator")
                .FirstOrDefault(t => AgentVfxApi.DescribeNodeType(t.id).inputNames.Length > 0);

            if (operatorType == null)
            {
                Assert.Ignore("No operator type with an input slot found.");
                return;
            }

            var nodeId = AgentVfxApi.CreateNode(_assetPath, operatorType.id, null, 0f, 0f);
            var node = AgentVfxApi.ReadGraph(_assetPath).nodes.Single(n => n.id == nodeId);
            var slot = AgentVfxApi.ReadSlot(node.inputSlotIds[0]);

            if (slot.typeName != "Single" && slot.typeName != "float")
            {
                Assert.Ignore($"First input slot is '{slot.typeName}', not float -- skipping value-set assertion.");
                return;
            }

            AgentVfxApi.SetSlotValue(slot.id, "3.5");
            var updated = AgentVfxApi.ReadSlot(slot.id);
            Assert.AreEqual("3.5", updated.valueJson);
        }

        [Test]
        public void CanSave()
        {
            AgentVfxApi.SaveGraph(_assetPath);
            Assert.IsTrue(File.Exists(Path.Combine(Directory.GetCurrentDirectory(), _assetPath)));
        }

        [Test]
        public void CanReload()
        {
            AgentVfxApi.SaveGraph(_assetPath);
            AssetDatabase.Refresh();

            var graph = AgentVfxApi.ReadGraph(_assetPath);
            Assert.AreEqual(_assetPath, graph.asset);
        }

        [Test]
        public void CanCompile()
        {
            var result = AgentVfxApi.Compile(_assetPath);
            Assert.IsNotNull(result.errors);
        }

        [Test]
        public void CanReadCompilerErrors()
        {
            AgentVfxApi.Compile(_assetPath);
            var result = AgentVfxApi.GetErrors(_assetPath);
            Assert.IsNotNull(result.errors);
        }
    }
}
