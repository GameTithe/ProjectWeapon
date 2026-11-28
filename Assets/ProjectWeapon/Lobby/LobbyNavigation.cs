#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ProjectWeapon.Lobby
{
    public enum LobbyDestination
    {
        EquippedWeapons,
        Warehouse,
        Sortie,
        Catalog,
        WeaponDraw
    }

    [DisallowMultipleComponent]
    public sealed class LobbyNavigation : MonoBehaviour
    {
        [Serializable]
        public sealed class Page
        {
            [SerializeField] private LobbyDestination destination;
            [SerializeField] private GameObject content = null!;
            [SerializeField] private Button open = null!;
            [SerializeField] private Button back = null!;

            public Page(LobbyDestination destination, GameObject content, Button open, Button back)
            {
                this.destination = destination;
                this.content = content;
                this.open = open;
                this.back = back;
            }

            public LobbyDestination Destination => destination;
            public GameObject Content => content;
            public Button Open => open;
            public Button Back => back;
        }

        [SerializeField] private GameObject home = null!;
        [SerializeField] private Page[] destinations = Array.Empty<Page>();
        private readonly Dictionary<LobbyDestination, Page> pages = new Dictionary<LobbyDestination, Page>();
        private readonly Dictionary<LobbyDestination, UnityAction> openHandlers = new Dictionary<LobbyDestination, UnityAction>();
        private Func<LobbyDestination, bool>? tryEnter;
        private bool switching;

        public LobbyDestination? CurrentDestination { get; private set; }
        public event Action<LobbyDestination>? OpenRejected;

        public void Initialize(Func<LobbyDestination, bool> tryEnterDestination)
            => Initialize(home, destinations, tryEnterDestination);

        public void Initialize(GameObject lobbyHome, IReadOnlyList<Page> pagesToBind,
            Func<LobbyDestination, bool> tryEnterDestination)
        {
            if (tryEnter != null)
                throw new InvalidOperationException("The lobby is already bound.");
            if (!lobbyHome)
                throw new ArgumentNullException(nameof(lobbyHome));
            if (pagesToBind == null)
                throw new ArgumentNullException(nameof(pagesToBind));
            if (tryEnterDestination == null)
                throw new ArgumentNullException(nameof(tryEnterDestination));

            var destinationsByPage = new Dictionary<LobbyDestination, Page>();
            var content = new HashSet<GameObject> { lobbyHome };
            var buttons = new HashSet<Button>();
            if (transform.IsChildOf(lobbyHome.transform))
                throw new ArgumentException("Place the navigation component outside the screens.", nameof(lobbyHome));

            foreach (Page binding in pagesToBind)
            {
                if (binding == null || !binding.Content || !binding.Open || !binding.Back ||
                    !binding.Open.transform.IsChildOf(lobbyHome.transform) ||
                    !binding.Back.transform.IsChildOf(binding.Content.transform) ||
                    binding.Content.transform.parent != lobbyHome.transform.parent ||
                    transform.IsChildOf(binding.Content.transform) ||
                    !Enum.IsDefined(typeof(LobbyDestination), binding.Destination) ||
                    !content.Add(binding.Content) || !buttons.Add(binding.Open) || !buttons.Add(binding.Back) ||
                    !destinationsByPage.TryAdd(binding.Destination, binding))
                    throw new ArgumentException("Every destination needs a distinct screen and both buttons.", nameof(pagesToBind));
            }

            if (destinationsByPage.Count != Enum.GetValues(typeof(LobbyDestination)).Length)
                throw new ArgumentException("All five lobby destinations must be bound.", nameof(pagesToBind));

            home = lobbyHome;
            tryEnter = tryEnterDestination;
            foreach (var entry in destinationsByPage)
            {
                LobbyDestination destination = entry.Key;
                pages.Add(destination, entry.Value);
                openHandlers.Add(destination, () => TryOpen(destination));
            }

            ApplyDestination(null);
            if (isActiveAndEnabled)
                AddListeners();
        }

        public bool TryOpen(LobbyDestination destination)
        {
            if (tryEnter == null || !isActiveAndEnabled || switching || CurrentDestination.HasValue || !pages.ContainsKey(destination))
                return false;
            switching = true;
            try
            {
                if (!tryEnter(destination))
                {
                    OpenRejected?.Invoke(destination);
                    return false;
                }

                ApplyDestination(destination);
                return true;
            }
            finally
            {
                switching = false;
            }
        }

        public bool TryReturnToLobby()
        {
            if (!isActiveAndEnabled || switching || !CurrentDestination.HasValue)
                return false;

            switching = true;
            try
            {
                ApplyDestination(null);
                return true;
            }
            finally
            {
                switching = false;
            }
        }

        private void ApplyDestination(LobbyDestination? destination)
        {
            CurrentDestination = destination;
            home.SetActive(!destination.HasValue);
            foreach (var entry in pages)
                entry.Value.Content.SetActive(entry.Key == destination);
        }

        private void OnBack() => TryReturnToLobby();
        private void OnEnable() => AddListeners();
        private void OnDisable() => RemoveListeners();

        private void AddListeners()
        {
            foreach (var entry in pages)
            {
                entry.Value.Open.onClick.AddListener(openHandlers[entry.Key]);
                entry.Value.Back.onClick.AddListener(OnBack);
            }
        }

        private void RemoveListeners()
        {
            foreach (var entry in pages)
            {
                if (entry.Value.Open)
                    entry.Value.Open.onClick.RemoveListener(openHandlers[entry.Key]);
                if (entry.Value.Back)
                    entry.Value.Back.onClick.RemoveListener(OnBack);
            }
        }
    }
}
