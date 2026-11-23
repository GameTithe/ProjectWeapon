using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProjectWeapon.Weapons.Tests
{
    public sealed class WeaponDefinitionValidatorTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in created)
            {
                Object.DestroyImmediate(item);
            }

            created.Clear();
        }

        [Test]
        public void AcceptsUniqueDefinitionsAtGradeBounds()
        {
            var errors = WeaponDefinitionValidator.Validate(new[]
            {
                Create("old_sword", 1),
                Create("high_sword", 5)
            });

            Assert.That(errors, Is.Empty);
        }

        [TestCase(0)]
        [TestCase(6)]
        public void RejectsGradeOutsideSupportedBounds(int grade)
        {
            var errors = WeaponDefinitionValidator.Validate(new[] { Create("sword", grade) });

            Assert.That(errors, Has.Some.Contains("Grade must be between 1 and 5."));
        }

        [Test]
        public void RejectsDuplicateDefinitionIds()
        {
            var errors = WeaponDefinitionValidator.Validate(new[]
            {
                Create("sword", 1),
                Create("sword", 2)
            });

            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain("Duplicate definition ID 'sword'."));
        }

        [Test]
        public void ReportsEachMissingReference()
        {
            var errors = WeaponDefinitionValidator.Validate(new[]
            {
                Create("sword", 1, includeReferences: false)
            });

            Assert.That(errors, Has.Count.EqualTo(3));
            Assert.That(errors, Has.Some.Contains("Basic attack is missing."));
            Assert.That(errors, Has.Some.Contains("Skill is missing."));
            Assert.That(errors, Has.Some.Contains("Attack VFX is missing."));
        }

        [TestCase("")]
        [TestCase(" sword")]
        [TestCase("sword ")]
        public void RejectsInvalidId(string invalidId)
        {
            var errors = WeaponDefinitionValidator.Validate(new[] { Create(invalidId, 1) });

            Assert.That(errors, Has.Some.Contains("Definition ID is missing"));
        }

        [Test]
        public void ReportsMissingDefinitionAndDisplayName()
        {
            var errors = WeaponDefinitionValidator.Validate(new[]
            {
                null,
                Create("sword", 1, displayName: "")
            });

            Assert.That(errors, Has.Count.EqualTo(2));
            Assert.That(errors, Has.Some.Contains("Weapon definition is missing."));
            Assert.That(errors, Has.Some.Contains("Display name is missing."));
        }

        private WeaponDefinition Create(
            string id, int grade, bool includeReferences = true, string displayName = "Sword")
        {
            var definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            created.Add(definition);
            definition.name = displayName;
            using (var serialized = new SerializedObject(definition))
            {
                serialized.FindProperty("definitionId").stringValue = id;
                serialized.FindProperty("displayName").stringValue = displayName;
                serialized.FindProperty("grade").intValue = grade;
                if (includeReferences)
                {
                    var attack = ScriptableObject.CreateInstance<WeaponActionDataStub>();
                    var skill = ScriptableObject.CreateInstance<WeaponActionDataStub>();
                    var vfx = new GameObject("VFX test object");
                    created.Add(attack);
                    created.Add(skill);
                    created.Add(vfx);
                    serialized.FindProperty("basicAttack").objectReferenceValue = attack;
                    serialized.FindProperty("skill").objectReferenceValue = skill;
                    serialized.FindProperty("attackVfx").objectReferenceValue = vfx;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return definition;
        }

        private sealed class WeaponActionDataStub : ScriptableObject
        {
        }
    }
}
