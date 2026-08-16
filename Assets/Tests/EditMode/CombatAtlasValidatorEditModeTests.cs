using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PlayGround.Editor.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

namespace PlayGround.Tests.EditMode
{
    public sealed class CombatAtlasValidatorEditModeTests
    {
        private const string AtlasPath = "Assets/Atlas/Skills.spriteatlasv2";

        [Test]
        public void ProductionAtlasSatisfiesRuntimeLookupContract()
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            Assert.That(atlas, Is.Not.Null, $"Missing combat atlas at '{AtlasPath}'.");

            List<CombatAtlasIssue> errors = CombatAtlasValidator.ValidateAtlas(atlas)
                .Where(issue => issue.Severity == MessageType.Error)
                .ToList();

            Assert.That(
                errors,
                Is.Empty,
                string.Join(Environment.NewLine, errors.Select(issue => issue.Message)));
        }
    }
}
