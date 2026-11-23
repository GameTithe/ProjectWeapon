using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProjectWeapon.ExtractionPresentation.Tests
{
    public sealed class ExtractionFeedbackViewTests
    {
        private GameObject root;
        private ExtractionFeedbackView view;
        private LineRenderer outline;
        private LineRenderer arc;
        private LineRenderer completion;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Extraction Feedback Test");
            view = root.AddComponent<ExtractionFeedbackView>();
            outline = CreateLine("Outline");
            arc = CreateLine("Progress");
            completion = CreateLine("Completion");

            var serializedView = new SerializedObject(view);
            serializedView.FindProperty("areaOutline").objectReferenceValue = outline;
            serializedView.FindProperty("progressArc").objectReferenceValue = arc;
            serializedView.FindProperty("completionMark").objectReferenceValue = completion;
            serializedView.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(root);

        [Test]
        public void MarkerKeepsInnerRouteClearAndMovesWithTheWorldAnchor()
        {
            var feedback = new ExtractionFeedback();
            var id = Guid.NewGuid();
            const float radius = 2;
            root.transform.position = new Vector3(-4, 3, 0);
            feedback.OnRunStarted(id);
            feedback.SetAvailability(id, true);
            feedback.OnExtractionStarted(id, 1, TimeSpan.Zero, TimeSpan.FromSeconds(3));

            view.Render(feedback.Sample(TimeSpan.FromSeconds(1.5)), radius);

            Assert.That(outline.enabled, Is.True);
            Assert.That(outline.loop, Is.True);
            Assert.That(outline.useWorldSpace, Is.False);
            Assert.That(arc.enabled, Is.True);
            Assert.That(arc.loop, Is.False);
            Assert.That(arc.GetPosition(0).y, Is.EqualTo(radius * 1.10f).Within(0.001f));
            Assert.That(arc.GetPosition(arc.positionCount - 1).y, Is.EqualTo(-radius * 1.10f).Within(0.001f));
            Assert.That(outline.transform.TransformPoint(outline.GetPosition(0)),
                Is.EqualTo(new Vector3(-4, 3 + radius, 0)));
            AssertPathInteriorClear(outline, radius);
            AssertPathInteriorClear(arc, radius);
        }

        [Test]
        public void AvailabilityIsVisibleAndCancellationImmediatelyHidesProgress()
        {
            var feedback = new ExtractionFeedback();
            var id = Guid.NewGuid();
            feedback.OnRunStarted(id);
            view.Render(feedback.Sample(TimeSpan.Zero), 2);
            float idleWidth = outline.widthMultiplier;

            feedback.SetAvailability(id, true);
            view.Render(feedback.Sample(TimeSpan.Zero), 2);
            Assert.That(outline.widthMultiplier, Is.GreaterThan(idleWidth));

            feedback.OnExtractionStarted(id, 1, TimeSpan.Zero, TimeSpan.FromSeconds(3));
            view.Render(feedback.Sample(TimeSpan.FromSeconds(1.5)), 2);
            Assert.That(arc.enabled, Is.True);

            feedback.OnExtractionCancelled(id, 1);
            view.Render(feedback.Sample(TimeSpan.FromSeconds(1.5)), 2);
            Assert.That(arc.enabled, Is.False);
            Assert.That(completion.enabled, Is.False);

            feedback.OnRunResultFinalized(id, true);
            view.Render(feedback.Sample(TimeSpan.FromSeconds(3)), 2);
            Assert.That(arc.enabled, Is.False);
            Assert.That(completion.enabled, Is.True);
            AssertPathInteriorClear(completion, 2);
        }

        private LineRenderer CreateLine(string lineName)
        {
            var child = new GameObject(lineName);
            child.transform.SetParent(root.transform, false);
            return child.AddComponent<LineRenderer>();
        }

        private static void AssertPathInteriorClear(LineRenderer line, float radius)
        {
            int segmentCount = line.loop ? line.positionCount : line.positionCount - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                Vector3 first = line.GetPosition(i);
                Vector3 second = line.GetPosition((i + 1) % line.positionCount);
                Vector3 segment = second - first;
                float t = Mathf.Clamp01(-Vector3.Dot(first, segment) / segment.sqrMagnitude);
                float innerDistance = (first + segment * t).magnitude - line.widthMultiplier * 0.5f;
                Assert.That(innerDistance, Is.GreaterThan(radius * 0.9f));
            }
        }
    }
}
