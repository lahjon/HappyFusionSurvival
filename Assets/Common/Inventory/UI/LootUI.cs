using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Common.Inventory.UI
{
	/// <summary>
	/// Procedural loot panel UI. Built lazily by <see cref="LootSession"/>:
	/// one Canvas holding a centered panel with header + container grid + player grid,
	/// plus a top-level drag ghost layer. Slot widgets are reused; only the data
	/// shown in them changes when the container / player inventory ticks.
	/// </summary>
	public sealed class LootUI : MonoBehaviour
	{
		public Color PanelColor = new Color(0f, 0f, 0f, 0.7f);
		public Color SlotColor = new Color(0f, 0f, 0f, 0.55f);
		public Color HeaderColor = new Color(1f, 0.85f, 0.1f, 1f);
		public Color SectionLabelColor = new Color(0.8f, 0.85f, 0.95f, 1f);
		public float SlotSize = 72f;
		public float SlotSpacing = 6f;
		public int ContainerColumns = 3;
		public int PlayerColumns = 8;

		private LootSession _session;
		private IPlayerInventory _playerInventory;
		private LootContainer _container;

		private RectTransform _panel;
		private TMP_Text _title;
		private Button _takeAllButton;
		private Button _closeButton;
		private RectTransform _containerGrid;
		private RectTransform _playerGrid;
		private RectTransform _ghostLayer;

		private SlotWidgetFactory.Widget[] _containerWidgets;
		private SlotWidgetFactory.Widget[] _playerWidgets;
		private InventoryTooltip _tooltip;
		private int _hoveredKind = -1;
		private int _hoveredIndex = -1;

		private Action<LootContainer> _openedHandler;
		private Action _containerSlotsHandler;
		private Action _playerSlotsHandler;

		public void Bind(LootSession session, IPlayerInventory playerInventory)
		{
			_session = session;
			_playerInventory = playerInventory;

			BuildHierarchy();
			SetPanelVisible(false);

			_openedHandler = OnOpenedChanged;
			_containerSlotsHandler = RefreshContainer;
			_playerSlotsHandler = RefreshPlayer;

			_session.OpenedChanged += _openedHandler;
			SubscribePlayer();
		}

		private void OnDestroy()
		{
			if (_session != null && _openedHandler != null)
			{
				_session.OpenedChanged -= _openedHandler;
			}
			UnsubscribeContainer();
			UnsubscribePlayer();
		}

		private void OnOpenedChanged(LootContainer next)
		{
			UnsubscribeContainer();
			_container = next;

			if (_container != null)
			{
				if (_title != null) _title.text = string.IsNullOrEmpty(_container.DisplayName) ? "Container" : _container.DisplayName;
				_container.SlotsChanged += _containerSlotsHandler;
				RefreshContainer();
				RefreshPlayer();
				SetPanelVisible(true);
				Debug.Log("[VERIFY] LootUI open PASS ✓");
			}
			else
			{
				SetPanelVisible(false);
			}
		}

		private void SubscribePlayer()
		{
			if (_playerInventory != null)
			{
				_playerInventory.SlotsChanged += _playerSlotsHandler;
			}
		}

		private void UnsubscribePlayer()
		{
			if (_playerInventory != null && _playerSlotsHandler != null)
			{
				_playerInventory.SlotsChanged -= _playerSlotsHandler;
			}
		}

		private void UnsubscribeContainer()
		{
			if (_container != null && _containerSlotsHandler != null)
			{
				_container.SlotsChanged -= _containerSlotsHandler;
			}
		}

		private void SetPanelVisible(bool visible)
		{
			if (_panel != null) _panel.gameObject.SetActive(visible);
			if (_ghostLayer != null) _ghostLayer.gameObject.SetActive(visible);
		}

		private void RefreshContainer()
		{
			if (_containerWidgets == null || _container == null) return;
			for (int i = 0; i < _containerWidgets.Length && i < _container.Slots.Length; i++)
			{
				SlotWidgetFactory.ApplySlot(_containerWidgets[i], _container.Slots[i]);
			}
			if (_hoveredKind == LootContainer.InvContainer) ShowTooltipForHover();
		}

		private void RefreshPlayer()
		{
			if (_playerWidgets == null || _playerInventory == null) return;
			var slots = _playerInventory.Slots;
			for (int i = 0; i < _playerWidgets.Length && i < slots.Length; i++)
			{
				SlotWidgetFactory.ApplySlot(_playerWidgets[i], slots[i]);
			}
			if (_hoveredKind == LootContainer.InvPlayer) ShowTooltipForHover();
		}

		private void ShowTooltipForHover()
		{
			if (_tooltip == null || _hoveredKind < 0 || _hoveredIndex < 0)
			{
				if (_tooltip != null) _tooltip.Hide();
				return;
			}

			InventorySlot slot;
			if (_hoveredKind == LootContainer.InvContainer && _container != null)
				slot = _container.Slots[_hoveredIndex];
			else if (_hoveredKind == LootContainer.InvPlayer && _playerInventory != null)
				slot = _playerInventory.Slots[_hoveredIndex];
			else
			{
				_tooltip.Hide();
				return;
			}

			if (slot.IsEmpty || ItemDatabase.Instance == null) { _tooltip.Hide(); return; }
			var def = ItemDatabase.Instance.GetById(slot.ItemId);
			if (def == null) { _tooltip.Hide(); return; }
			_tooltip.Show(def, slot.Count);
		}

		private void BuildHierarchy()
		{
			// Canvas
			var canvasGO = new GameObject("LootCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			canvasGO.transform.SetParent(transform, false);
			var canvas = canvasGO.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 50;
			var scaler = canvasGO.GetComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;

			var canvasRT = (RectTransform)canvasGO.transform;

			// Panel
			var panelGO = new GameObject("LootPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
			panelGO.transform.SetParent(canvasGO.transform, false);
			_panel = (RectTransform)panelGO.transform;
			_panel.anchorMin = new Vector2(0.5f, 0.5f);
			_panel.anchorMax = new Vector2(0.5f, 0.5f);
			_panel.pivot = new Vector2(0.5f, 0.5f);
			_panel.sizeDelta = new Vector2(720f, 500f);
			var panelImg = panelGO.GetComponent<Image>();
			panelImg.color = PanelColor;
			var panelV = panelGO.GetComponent<VerticalLayoutGroup>();
			panelV.padding = new RectOffset(16, 16, 16, 16);
			panelV.spacing = 12f;
			panelV.childControlWidth = true;
			panelV.childControlHeight = false;
			panelV.childForceExpandWidth = true;
			panelV.childForceExpandHeight = false;

			// Header
			BuildHeader(panelGO.transform);

			// Container grid
			AddSectionLabel(panelGO.transform, "Container");
			_containerGrid = BuildGrid(panelGO.transform, LootContainer.Capacity, ContainerColumns, out _containerWidgets, SlotInventoryKind.Container);

			// Spacer
			AddSectionLabel(panelGO.transform, "Inventory");

			// Player grid
			int playerSlots = (_playerInventory != null) ? _playerInventory.Slots.Length : 8;
			_playerGrid = BuildGrid(panelGO.transform, playerSlots, PlayerColumns, out _playerWidgets, SlotInventoryKind.Player);

			// Drag ghost layer (last sibling — renders above all slot icons)
			var ghostGO = new GameObject("DragGhostLayer", typeof(RectTransform));
			ghostGO.transform.SetParent(canvasGO.transform, false);
			ghostGO.transform.SetAsLastSibling();
			_ghostLayer = (RectTransform)ghostGO.transform;
			_ghostLayer.anchorMin = Vector2.zero;
			_ghostLayer.anchorMax = Vector2.one;
			_ghostLayer.offsetMin = Vector2.zero;
			_ghostLayer.offsetMax = Vector2.zero;

			// Wire ghost layer into draggables
			WireGhostLayer(_containerWidgets);
			WireGhostLayer(_playerWidgets);

			// Tooltip — child of canvas so it can float over slots
			var tooltipGO = new GameObject("LootTooltip", typeof(RectTransform));
			tooltipGO.transform.SetParent(canvasGO.transform, false);
			_tooltip = tooltipGO.AddComponent<InventoryTooltip>();
		}

		private void WireGhostLayer(SlotWidgetFactory.Widget[] widgets)
		{
			if (widgets == null) return;
			for (int i = 0; i < widgets.Length; i++)
			{
				var drag = widgets[i].Root.GetComponent<DraggableSlot>();
				if (drag != null) drag.GhostLayer = _ghostLayer;
			}
		}

		private void BuildHeader(Transform parent)
		{
			var headerGO = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
			headerGO.transform.SetParent(parent, false);
			var hg = headerGO.GetComponent<HorizontalLayoutGroup>();
			hg.spacing = 8f;
			hg.childAlignment = TextAnchor.MiddleLeft;
			hg.childControlWidth = false;
			hg.childControlHeight = false;
			hg.childForceExpandWidth = false;
			hg.childForceExpandHeight = false;
			var le = headerGO.GetComponent<LayoutElement>();
			le.preferredHeight = 36f;

			var titleGO = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
			titleGO.transform.SetParent(headerGO.transform, false);
			_title = titleGO.GetComponent<TextMeshProUGUI>();
			_title.text = "Container";
			_title.color = HeaderColor;
			_title.fontSize = 22f;
			_title.fontStyle = FontStyles.Bold;
			_title.alignment = TextAlignmentOptions.MidlineLeft;
			_title.raycastTarget = false;
			titleGO.GetComponent<LayoutElement>().preferredWidth = 480f;

			_takeAllButton = BuildButton(headerGO.transform, "TakeAllButton", "Take All", 110f, OnTakeAllClicked);
			_closeButton   = BuildButton(headerGO.transform, "CloseButton",   "X",        40f,  OnCloseClicked);
		}

		private Button BuildButton(Transform parent, string name, string label, float width, UnityEngine.Events.UnityAction onClick)
		{
			var btnGO = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
			btnGO.transform.SetParent(parent, false);
			var img = btnGO.GetComponent<Image>();
			img.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);
			var btn = btnGO.GetComponent<Button>();
			btn.onClick.AddListener(onClick);

			var le = btnGO.GetComponent<LayoutElement>();
			le.preferredWidth = width;
			le.preferredHeight = 32f;

			var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
			textGO.transform.SetParent(btnGO.transform, false);
			var trt = (RectTransform)textGO.transform;
			trt.anchorMin = Vector2.zero;
			trt.anchorMax = Vector2.one;
			trt.offsetMin = Vector2.zero;
			trt.offsetMax = Vector2.zero;
			var t = textGO.GetComponent<TextMeshProUGUI>();
			t.text = label;
			t.color = Color.white;
			t.fontSize = 16f;
			t.alignment = TextAlignmentOptions.Center;
			t.raycastTarget = false;
			return btn;
		}

		private void AddSectionLabel(Transform parent, string label)
		{
			var go = new GameObject($"Label_{label}", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
			go.transform.SetParent(parent, false);
			var t = go.GetComponent<TextMeshProUGUI>();
			t.text = label;
			t.color = SectionLabelColor;
			t.fontSize = 14f;
			t.alignment = TextAlignmentOptions.MidlineLeft;
			t.raycastTarget = false;
			go.GetComponent<LayoutElement>().preferredHeight = 18f;
		}

		private RectTransform BuildGrid(Transform parent, int slotCount, int columns, out SlotWidgetFactory.Widget[] widgets, SlotInventoryKind kind)
		{
			int rows = Mathf.CeilToInt(slotCount / (float)columns);
			float w = columns * SlotSize + (columns - 1) * SlotSpacing;
			float h = rows * SlotSize + (rows - 1) * SlotSpacing;

			var gridGO = new GameObject($"{kind}Grid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
			gridGO.transform.SetParent(parent, false);
			var grid = gridGO.GetComponent<GridLayoutGroup>();
			grid.cellSize = new Vector2(SlotSize, SlotSize);
			grid.spacing = new Vector2(SlotSpacing, SlotSpacing);
			grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
			grid.startAxis = GridLayoutGroup.Axis.Horizontal;
			grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
			grid.constraintCount = columns;
			grid.childAlignment = TextAnchor.MiddleCenter;

			var le = gridGO.GetComponent<LayoutElement>();
			le.preferredHeight = h;

			widgets = new SlotWidgetFactory.Widget[slotCount];
			for (int i = 0; i < slotCount; i++)
			{
				var widget = SlotWidgetFactory.Build(gridGO.transform, $"{kind}_{i}", SlotSize, SlotColor, Color.white, showLabel: false);
				widget.Hover.SlotIndex = i;
				int capturedKind = (int)kind;
				int capturedIndex = i;
				widget.Hover.Entered += idx => { _hoveredKind = capturedKind; _hoveredIndex = idx; ShowTooltipForHover(); };
				widget.Hover.Exited  += idx => { if (_hoveredKind == capturedKind && _hoveredIndex == idx) { _hoveredKind = -1; _hoveredIndex = -1; if (_tooltip != null) _tooltip.Hide(); } };

				var drag = widget.Root.gameObject.AddComponent<DraggableSlot>();
				drag.Kind = kind;
				drag.Index = i;
				drag.SourceIcon = widget.Icon;
				drag.DroppedOutside += OnSlotDroppedOutside;

				var drop = widget.Root.gameObject.AddComponent<DroppableSlot>();
				drop.Kind = kind;
				drop.Index = i;
				drop.Session = _session;

				widgets[i] = widget;
			}

			return (RectTransform)gridGO.transform;
		}

		private void OnSlotDroppedOutside(DraggableSlot src)
		{
			if (src == null || _playerInventory == null) return;
			// Only player-side slots drop into the world. Container slots stay in the container.
			if (src.Kind != SlotInventoryKind.Player) return;

			_playerInventory.RequestDropSlot(src.Index);
		}

		private void OnTakeAllClicked()
		{
			if (_session != null) _session.RequestTakeAll();
		}

		private void OnCloseClicked()
		{
			if (_session != null) _session.RequestClose();
		}
	}

}
