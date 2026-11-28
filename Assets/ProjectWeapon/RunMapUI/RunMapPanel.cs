#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Run;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.RunMapUI
{
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class RunMapPanel : MonoBehaviour
    {
        private readonly HashSet<int> launcherPointers = new HashSet<int>();
        private RunState run = null!;
        private ZoneCatalog catalog = null!;
        private Func<RunMapData?> readMap = null!;
        private RunMapView? view;
        private RunId openedFor;
        private int ownedFrame = -1;

        public bool IsOpen => view?.IsOpen == true;
        public bool OwnsGameplayInput => IsOpen || launcherPointers.Count != 0 || ownedFrame == Time.frameCount;

        public void Initialize(RunState run, ZoneCatalog catalog, TMP_FontAsset font, Func<RunMapData?> readMap)
        {
            if (view != null)
                throw new InvalidOperationException("This run map is already initialized.");
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            this.readMap = readMap ?? throw new ArgumentNullException(nameof(readMap));
            if (!catalog)
                throw new ArgumentNullException(nameof(catalog));
            if (!font)
                throw new ArgumentNullException(nameof(font));
            if (!Resources.Load<TMP_Settings>("TMP Settings"))
                throw new InvalidOperationException("Import TMP Essential Resources before initializing the run map.");

            this.catalog = catalog;
            view = new RunMapView((RectTransform)transform, font, Launch, Close, OnPress, OnRelease);
            SyncLifecycle();
        }

        public bool Open()
        {
            if (view == null || !isActiveAndEnabled)
                return false;
            RunSession? session = SyncLifecycle();
            if (session == null)
                return false;

            openedFor = session.Id;
            ownedFrame = Time.frameCount;
            view.SetOpen(true);
            Refresh();
            return true;
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            ownedFrame = Time.frameCount;
            view!.SetOpen(false);
            openedFor = default;
        }

        /// <summary>Refresh when the layout owner marks a room or connection as known.</summary>
        public void Refresh()
        {
            if (SyncLifecycle() == null || !IsOpen)
                return;
            if (RunMapDisplay.Read(run, catalog, readMap()) is RunMapDisplay display)
                view!.Render(display);
        }

        private void Update() => SyncLifecycle();

        private void OnDisable()
        {
            Close();
            ReleaseLauncher();
        }

        private RunSession? SyncLifecycle()
        {
            if (view == null)
                return null;

            RunSession? session = RunMapDisplay.LiveSession(run);
            if (IsOpen && session?.Id != openedFor)
                Close();
            if (session == null)
                ReleaseLauncher();

            view.SetLauncher(session != null);
            return session;
        }

        private void Launch() => Open();

        private void OnPress(int pointerId)
        {
            if (RunMapDisplay.LiveSession(run) != null)
                launcherPointers.Add(pointerId);
        }

        private void OnRelease(int pointerId)
        {
            if (launcherPointers.Remove(pointerId))
                ownedFrame = Time.frameCount;
        }

        private void ReleaseLauncher()
        {
            if (launcherPointers.Count == 0)
                return;

            launcherPointers.Clear();
            ownedFrame = Time.frameCount;
        }
    }
}
