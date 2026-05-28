using Fusion;
using Starter.Common.Inventory;
using Starter.Common.Quests;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only quest turn-in menu. Mirrors <see cref="CraftingHUD"/>'s procedural build style:
	/// attach to one RectTransform under a Canvas and the rest is generated at runtime.
	/// Shows/hides on <see cref="QuestSession.OpenedChanged"/>; the hotbar stays visible alongside
	/// so the player can see what they've got.
	/// </summary>
	public sealed class QuestPanelUI : MonoBehaviour
	{
		private const int MaxRows = QuestGiver.MaxOffers;

		[Header("Layout")]
		public Vector2 PanelSize = new Vector2(640f, 460f);
		public float RowHeight = 92f;
		public float RowSpacing = 6f;

		[Header("Colors")]
		public Color PanelColor = new Color(0f, 0f, 0f, 0.85f);
		public Color HeaderColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
		public Color RowAvailable = new Color(0.12f, 0.12f, 0.12f, 0.9f);
		public Color RowMissing = new Color(0.35f, 0.05f, 0.05f, 0.9f);
		public Color RowDoneByYou = new Color(0.1f, 0.35f, 0.1f, 0.9f);
		public Color RowDoneByOther = new Color(0.1f, 0.1f, 0.1f, 0.6f);
		public Color CompleteEnabled = new Color(0.15f, 0.65f, 0.2f, 1f);
		public Color CompleteDisabled = new Color(0.25f, 0.25f, 0.25f, 0.8f);
		public Color TextColor = Color.white;
		public Color TextOK = new Color(0.7f, 1f, 0.7f, 1f);
		public Color TextMissing = new Color(1f, 0.4f, 0.4f, 1f);

		private QuestSession _session;
		private Inventory _inventory;

		private GameObject _root;
		private TMP_Text _headerLabel;
		private RowWidget[] _rows = new RowWidget[MaxRows];
		private TMP_Text _emptyHint;

		private struct RowWidget
		{
			public GameObject Root;
			public Image Background;
			public TMP_Text Title;
			public TMP_Text RequestedText;
			public TMP_Text RewardText;
			public Image CompleteButtonBg;
			public Button CompleteButton;
			public TMP_Text CompleteLabel;
		}

		private void Awake()
		{
			BuildLayout();
			SetVisible(false);
		}

		private void Update()
		{
			if (_session == null)
			{
				TryBind();
				return;
			}

			// Cheap repaint while the panel is open — both inventory state and CompletedBy
			// can change while looking at it, and the panel has at most MaxOffers rows.
			if (_session.Current != null)
			{
				RefreshRows();
			}
		}

		private void OnDestroy()
		{
			Unbind();
		}

		private void TryBind()
		{
			var gm = FindAnyObjectByType<GameManager>();
			if (gm == null || gm.LocalPlayer == null) return;

			var session = gm.LocalPlayer.GetComponent<QuestSession>();
			var inv = gm.LocalPlayer.GetComponent<Inventory>();
			if (session == null || inv == null) return;

			_session = session;
			_inventory = inv;

			_session.OpenedChanged += OnOpenedChanged;
		}

		private void Unbind()
		{
			if (_session != null) _session.OpenedChanged -= OnOpenedChanged;
			_session = null;
			_inventory = null;
		}

		private void OnOpenedChanged(QuestGiver giver)
		{
			SetVisible(giver != null);
			if (giver == null) return;

			_headerLabel.text = giver.DisplayName;
			RebuildRows(giver);
		}

		private void SetVisible(bool visible)
		{
			if (_root != null) _root.SetActive(visible);
		}

		private void RebuildRows(QuestGiver giver)
		{
			int count = giver.OfferCount;
			for (int i = 0; i < MaxRows; i++)
			{
				_rows[i].Root.SetActive(i < count);
			}
			if (_emptyHint != null) _emptyHint.gameObject.SetActive(count == 0);
			RefreshRows();
		}

		private void RefreshRows()
		{
			var giver = _session.Current;
			if (giver == null) return;

			PlayerRef localPlayer = giver.Runner != null ? giver.Runner.LocalPlayer : PlayerRef.None;

			int count = giver.OfferCount;
			for (int i = 0; i < count; i++)
			{
				var offer = giver.GetOffer(i);
				var row = _rows[i];
				if (offer == null)
				{
					row.Root.SetActive(false);
					continue;
				}

				row.Title.text = offer.DisplayName;
				row.RequestedText.text = BuildRequestedText(offer, out bool hasAllItems);
				row.RewardText.text = BuildRewardText(offer);

				var claim = giver.CompletedBy[i];
				bool doneByYou = claim == localPlayer && claim != PlayerRef.None;
				bool doneByOther = claim != PlayerRef.None && !doneByYou;
				bool hasRoom = HasRoomForRewards(offer);

				if (doneByYou)
				{
					row.Background.color = RowDoneByYou;
					SetButton(row, false, "COMPLETED");
				}
				else if (doneByOther)
				{
					row.Background.color = RowDoneByOther;
					SetButton(row, false, "TAKEN");
				}
				else if (!hasAllItems)
				{
					row.Background.color = RowMissing;
					SetButton(row, false, "MISSING ITEMS");
				}
				else if (!hasRoom)
				{
					row.Background.color = RowMissing;
					SetButton(row, false, "INVENTORY FULL");
				}
				else
				{
					row.Background.color = RowAvailable;
					SetButton(row, true, "TURN IN");
				}
			}
		}

		private string BuildRequestedText(QuestDefinition offer, out bool hasAll)
		{
			hasAll = true;
			if (offer.Requested == null || offer.Requested.Length == 0)
			{
				return "(nothing requested)";
			}

			var sb = new System.Text.StringBuilder(64);
			sb.Append("<b>Wants:</b>\n");
			for (int i = 0; i < offer.Requested.Length; i++)
			{
				var req = offer.Requested[i];
				if (req.Item == null || req.Count <= 0) continue;

				int have = _inventory != null ? InventoryOps.CountItem(_inventory.Slots, req.Item.Id) : 0;
				int need = req.Count;
				bool ok = have >= need;
				if (!ok) hasAll = false;

				string colorTag = ColorUtility.ToHtmlStringRGB(ok ? TextOK : TextMissing);
				sb.Append("  ").Append(req.Item.DisplayName).Append("  <color=#").Append(colorTag)
					.Append('>').Append(have).Append('/').Append(need).Append("</color>\n");
			}
			return sb.ToString();
		}

		private string BuildRewardText(QuestDefinition offer)
		{
			var sb = new System.Text.StringBuilder(64);
			sb.Append("<b>Reward:</b>\n");
			bool any = false;
			if (offer.ItemRewards != null)
			{
				for (int i = 0; i < offer.ItemRewards.Length; i++)
				{
					var rew = offer.ItemRewards[i];
					if (rew.Item == null || rew.Count <= 0) continue;
					sb.Append("  ").Append(rew.Item.DisplayName).Append(" x").Append(rew.Count).Append('\n');
					any = true;
				}
			}
			if (offer.MoneyReward > 0)
			{
				sb.Append("  $").Append(offer.MoneyReward).Append('\n');
				any = true;
			}
			if (!any) sb.Append("  (none)");
			return sb.ToString();
		}

		private bool HasRoomForRewards(QuestDefinition offer)
		{
			if (offer.ItemRewards == null || _inventory == null) return true;
			for (int i = 0; i < offer.ItemRewards.Length; i++)
			{
				var rew = offer.ItemRewards[i];
				if (rew.Item == null || rew.Count <= 0) continue;
				if (InventoryOps.RoomFor(_inventory.Slots, rew.Item.Id) < rew.Count) return false;
			}
			return true;
		}

		private void SetButton(RowWidget row, bool enabled, string label)
		{
			row.CompleteButton.interactable = enabled;
			row.CompleteButtonBg.color = enabled ? CompleteEnabled : CompleteDisabled;
			row.CompleteLabel.text = label;
		}

		private void OnRowComplete(int offerIndex)
		{
			if (_session == null) return;
			_session.RequestComplete(offerIndex);
		}

		// --- Layout build ---

		private void BuildLayout()
		{
			var rt = transform as RectTransform;
			if (rt == null)
			{
				Debug.LogError("[QuestPanelUI] Must live on a RectTransform (under a Canvas).");
				return;
			}

			rt.anchorMin = new Vector2(0.5f, 0.5f);
			rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = Vector2.zero;
			rt.sizeDelta = PanelSize;

			_root = new GameObject("QuestPanel", typeof(RectTransform), typeof(Image));
			_root.transform.SetParent(transform, false);
			var panelRT = (RectTransform)_root.transform;
			panelRT.anchorMin = Vector2.zero;
			panelRT.anchorMax = Vector2.one;
			panelRT.offsetMin = Vector2.zero;
			panelRT.offsetMax = Vector2.zero;
			_root.GetComponent<Image>().color = PanelColor;

			var header = new GameObject("Header", typeof(RectTransform), typeof(Image));
			header.transform.SetParent(_root.transform, false);
			var hrt = (RectTransform)header.transform;
			hrt.anchorMin = new Vector2(0f, 1f);
			hrt.anchorMax = new Vector2(1f, 1f);
			hrt.pivot = new Vector2(0.5f, 1f);
			hrt.anchoredPosition = Vector2.zero;
			hrt.sizeDelta = new Vector2(0f, 36f);
			header.GetComponent<Image>().color = HeaderColor;
			_headerLabel = AddText(header.transform, "HeaderLabel", "Quest Giver", 18, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);

			var list = new GameObject("OfferList", typeof(RectTransform));
			list.transform.SetParent(_root.transform, false);
			var lrt = (RectTransform)list.transform;
			lrt.anchorMin = new Vector2(0f, 0f);
			lrt.anchorMax = new Vector2(1f, 1f);
			lrt.pivot = new Vector2(0.5f, 1f);
			lrt.anchoredPosition = new Vector2(0f, -42f);
			lrt.sizeDelta = new Vector2(-16f, -50f);

			for (int i = 0; i < MaxRows; i++)
			{
				_rows[i] = BuildRow(list.transform, i);
			}

			_emptyHint = AddText(list.transform, "EmptyHint", "No offers available", 14, TextAlignmentOptions.Center,
				new Vector2(0f, 1f), new Vector2(1f, 1f));
			var ehrt = _emptyHint.rectTransform;
			ehrt.pivot = new Vector2(0.5f, 1f);
			ehrt.anchoredPosition = new Vector2(0f, -10f);
			ehrt.sizeDelta = new Vector2(0f, 24f);
			_emptyHint.gameObject.SetActive(false);
		}

		private RowWidget BuildRow(Transform parent, int index)
		{
			var row = new GameObject($"Offer_{index}", typeof(RectTransform), typeof(Image));
			row.transform.SetParent(parent, false);
			var rrt = (RectTransform)row.transform;
			rrt.anchorMin = new Vector2(0f, 1f);
			rrt.anchorMax = new Vector2(1f, 1f);
			rrt.pivot = new Vector2(0f, 1f);
			rrt.anchoredPosition = new Vector2(0f, -index * (RowHeight + RowSpacing));
			rrt.sizeDelta = new Vector2(0f, RowHeight);
			var bg = row.GetComponent<Image>();
			bg.color = RowAvailable;

			var title = AddText(row.transform, "Title", string.Empty, 16, TextAlignmentOptions.TopLeft,
				new Vector2(0f, 1f), new Vector2(1f, 1f));
			title.fontStyle = FontStyles.Bold;
			var trt = title.rectTransform;
			trt.pivot = new Vector2(0f, 1f);
			trt.anchoredPosition = new Vector2(10f, -4f);
			trt.sizeDelta = new Vector2(-20f, 22f);
			title.raycastTarget = false;

			var requested = AddText(row.transform, "Requested", string.Empty, 13, TextAlignmentOptions.TopLeft,
				new Vector2(0f, 0f), new Vector2(0.55f, 1f));
			var reqRt = requested.rectTransform;
			reqRt.pivot = new Vector2(0f, 1f);
			reqRt.anchoredPosition = new Vector2(10f, -28f);
			reqRt.sizeDelta = new Vector2(0f, RowHeight - 36f);
			requested.raycastTarget = false;
			requested.richText = true;

			var reward = AddText(row.transform, "Reward", string.Empty, 13, TextAlignmentOptions.TopLeft,
				new Vector2(0.55f, 0f), new Vector2(0.78f, 1f));
			var rewRt = reward.rectTransform;
			rewRt.pivot = new Vector2(0f, 1f);
			rewRt.anchoredPosition = new Vector2(0f, -28f);
			rewRt.sizeDelta = new Vector2(0f, RowHeight - 36f);
			reward.raycastTarget = false;
			reward.richText = true;

			var btnGO = new GameObject("CompleteButton", typeof(RectTransform), typeof(Image), typeof(Button));
			btnGO.transform.SetParent(row.transform, false);
			var brt = (RectTransform)btnGO.transform;
			brt.anchorMin = new Vector2(1f, 0.5f);
			brt.anchorMax = new Vector2(1f, 0.5f);
			brt.pivot = new Vector2(1f, 0.5f);
			brt.anchoredPosition = new Vector2(-10f, 0f);
			brt.sizeDelta = new Vector2(140f, 40f);
			var btnBg = btnGO.GetComponent<Image>();
			btnBg.color = CompleteDisabled;
			var button = btnGO.GetComponent<Button>();
			int captured = index;
			button.onClick.AddListener(() => OnRowComplete(captured));

			var btnLabel = AddText(btnGO.transform, "Label", "TURN IN", 14, TextAlignmentOptions.Center,
				Vector2.zero, Vector2.one);
			btnLabel.raycastTarget = false;

			return new RowWidget
			{
				Root = row,
				Background = bg,
				Title = title,
				RequestedText = requested,
				RewardText = reward,
				CompleteButtonBg = btnBg,
				CompleteButton = button,
				CompleteLabel = btnLabel,
			};
		}

		private TMP_Text AddText(Transform parent, string name, string text, float fontSize,
			TextAlignmentOptions alignment, Vector2 anchorMin, Vector2 anchorMax)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
			go.transform.SetParent(parent, false);
			var t = go.GetComponent<TextMeshProUGUI>();
			t.text = text;
			t.fontSize = fontSize;
			t.alignment = alignment;
			t.color = TextColor;
			t.raycastTarget = false;
			var rt = t.rectTransform;
			rt.anchorMin = anchorMin;
			rt.anchorMax = anchorMax;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
			return t;
		}
	}
}
