using System.Collections.Generic;
using Starter.Common.Crafting;
using Starter.Common.Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only crafting menu. Mirrors <see cref="InventoryHUD"/>'s procedural build style:
	/// attach to one RectTransform under a Canvas and the rest is generated at runtime.
	/// Shows/hides on <see cref="CraftingSession.OpenedChanged"/>; the hotbar stays visible
	/// alongside so ingredient counts are readable.
	/// </summary>
	public sealed class CraftingHUD : MonoBehaviour
	{
		private const int MaxRows = 12;
		private const int MaxIngredientRows = 6;

		[Header("Layout")]
		public Vector2 PanelSize = new Vector2(640f, 420f);
		public float ListWidth = 220f;
		public float RowHeight = 48f;
		public float IngredientRowHeight = 24f;

		[Header("Colors")]
		public Color PanelColor = new Color(0f, 0f, 0f, 0.85f);
		public Color HeaderColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
		public Color RowNormal = new Color(0.12f, 0.12f, 0.12f, 0.9f);
		public Color RowSelected = new Color(1f, 0.85f, 0.1f, 0.85f);
		public Color RowMissing = new Color(0.35f, 0.05f, 0.05f, 0.9f);
		public Color CraftEnabled = new Color(0.15f, 0.65f, 0.2f, 1f);
		public Color CraftDisabled = new Color(0.25f, 0.25f, 0.25f, 0.8f);
		public Color TextColor = Color.white;
		public Color TextMissing = new Color(1f, 0.4f, 0.4f, 1f);
		public Color TextOK = new Color(0.7f, 1f, 0.7f, 1f);

		private CraftingSession _session;
		private RecipeBook _book;
		private Inventory _inventory;

		private GameObject _root;
		private TMP_Text _headerLabel;
		private RowWidget[] _rows = new RowWidget[MaxRows];

		private Image _detailIcon;
		private TMP_Text _detailName;
		private TMP_Text _detailDescription;
		private IngredientWidget[] _ingredientRows = new IngredientWidget[MaxIngredientRows];
		private Button _craftButton;
		private Image _craftButtonBg;
		private TMP_Text _craftLabel;
		private TMP_Text _emptyHint;

		private readonly List<int> _displayedRecipeIds = new();
		private int _selectedRecipeId;

		private struct RowWidget
		{
			public GameObject Root;
			public Image Background;
			public Image Icon;
			public TMP_Text Label;
			public Button Button;
			public int RecipeId;
		}

		private struct IngredientWidget
		{
			public GameObject Root;
			public Image Icon;
			public TMP_Text Label;
			public TMP_Text Counts;
		}

		private void Awake()
		{
			BuildLayout();
			SetVisible(false);
		}

		private void Update()
		{
			if (_session != null) return;
			TryBind();
		}

		private void OnDestroy()
		{
			Unbind();
		}

		private void TryBind()
		{
			var gm = FindFirstObjectByType<GameManager>();
			if (gm == null || gm.LocalPlayer == null) return;

			var session = gm.LocalPlayer.GetComponent<CraftingSession>();
			var book = gm.LocalPlayer.GetComponent<RecipeBook>();
			var inv = gm.LocalPlayer.GetComponent<Inventory>();
			if (session == null || book == null || inv == null) return;

			_session = session;
			_book = book;
			_inventory = inv;

			_session.OpenedChanged += OnOpenedChanged;
			_book.KnownChanged += OnKnownChanged;
			_inventory.SlotsChanged += OnSlotsChanged;
		}

		private void Unbind()
		{
			if (_session != null) _session.OpenedChanged -= OnOpenedChanged;
			if (_book != null) _book.KnownChanged -= OnKnownChanged;
			if (_inventory != null) _inventory.SlotsChanged -= OnSlotsChanged;
			_session = null;
			_book = null;
			_inventory = null;
		}

		private void OnOpenedChanged(CraftingBench bench)
		{
			SetVisible(bench != null);
			if (bench == null) return;

			_headerLabel.text = bench.DisplayName;
			RebuildRecipeList(bench);
		}

		private void OnKnownChanged()
		{
			if (_session?.Current != null) RebuildRecipeList(_session.Current);
		}

		private void OnSlotsChanged()
		{
			if (_session?.Current == null) return;
			RefreshRowAvailability();
			RefreshDetail();
		}

		private void SetVisible(bool visible)
		{
			if (_root != null) _root.SetActive(visible);
		}

		private void RebuildRecipeList(CraftingBench bench)
		{
			_displayedRecipeIds.Clear();

			if (bench.AllowedRecipes != null && RecipeDatabase.Instance != null && _book != null)
			{
				for (int i = 0; i < bench.AllowedRecipes.Length; i++)
				{
					var r = bench.AllowedRecipes[i];
					if (r == null || r.Id == 0) continue;
					if (!_book.IsKnown(r.Id)) continue;
					_displayedRecipeIds.Add(r.Id);
					if (_displayedRecipeIds.Count >= MaxRows) break;
				}
			}

			for (int i = 0; i < MaxRows; i++)
			{
				var row = _rows[i];
				if (i < _displayedRecipeIds.Count)
				{
					int recipeId = _displayedRecipeIds[i];
					var recipe = RecipeDatabase.Instance.GetById(recipeId);
					row.RecipeId = recipeId;
					row.Root.SetActive(true);
					row.Label.text = recipe != null ? recipe.DisplayName : $"#{recipeId}";
					if (recipe != null && recipe.Output != null)
					{
						row.Icon.sprite = recipe.Output.Icon;
						row.Icon.enabled = recipe.Output.Icon != null;
					}
					else
					{
						row.Icon.enabled = false;
					}
					_rows[i] = row;
				}
				else
				{
					row.RecipeId = 0;
					row.Root.SetActive(false);
					_rows[i] = row;
				}
			}

			if (_emptyHint != null) _emptyHint.gameObject.SetActive(_displayedRecipeIds.Count == 0);

			if (_displayedRecipeIds.Count == 0)
			{
				_selectedRecipeId = 0;
			}
			else if (_displayedRecipeIds.IndexOf(_selectedRecipeId) < 0)
			{
				_selectedRecipeId = _displayedRecipeIds[0];
			}

			RefreshRowAvailability();
			RefreshDetail();
		}

		private void RefreshRowAvailability()
		{
			if (RecipeDatabase.Instance == null || _inventory == null) return;

			for (int i = 0; i < MaxRows; i++)
			{
				var row = _rows[i];
				if (row.RecipeId == 0 || row.Background == null) continue;

				bool selected = row.RecipeId == _selectedRecipeId;
				bool missing = !HasAllIngredients(RecipeDatabase.Instance.GetById(row.RecipeId));
				row.Background.color = selected ? RowSelected : (missing ? RowMissing : RowNormal);
			}
		}

		private void RefreshDetail()
		{
			RecipeDefinition recipe = _selectedRecipeId != 0 && RecipeDatabase.Instance != null
				? RecipeDatabase.Instance.GetById(_selectedRecipeId)
				: null;

			if (recipe == null)
			{
				_detailIcon.enabled = false;
				_detailName.text = string.Empty;
				_detailDescription.text = string.Empty;
				for (int i = 0; i < MaxIngredientRows; i++)
				{
					if (_ingredientRows[i].Root != null) _ingredientRows[i].Root.SetActive(false);
				}
				SetCraftEnabled(false, string.Empty);
				return;
			}

			if (recipe.Output != null && recipe.Output.Icon != null)
			{
				_detailIcon.sprite = recipe.Output.Icon;
				_detailIcon.enabled = true;
			}
			else
			{
				_detailIcon.enabled = false;
			}

			_detailName.text = recipe.OutputCount > 1
				? $"{recipe.DisplayName}  x{recipe.OutputCount}"
				: recipe.DisplayName;
			_detailDescription.text = recipe.Description ?? string.Empty;

			bool allOK = true;
			int count = recipe.Ingredients != null ? Mathf.Min(recipe.Ingredients.Length, MaxIngredientRows) : 0;

			for (int i = 0; i < MaxIngredientRows; i++)
			{
				var w = _ingredientRows[i];
				if (i < count)
				{
					var ing = recipe.Ingredients[i];
					w.Root.SetActive(true);
					if (ing.Item != null)
					{
						w.Icon.sprite = ing.Item.Icon;
						w.Icon.enabled = ing.Item.Icon != null;
						w.Label.text = ing.Item.DisplayName;
						int have = InventoryOps.CountItem(_inventory.Slots, ing.Item.Id);
						int need = ing.Count;
						bool ok = have >= need;
						if (!ok) allOK = false;
						w.Counts.text = $"{have}/{need}";
						w.Counts.color = ok ? TextOK : TextMissing;
					}
					else
					{
						w.Icon.enabled = false;
						w.Label.text = "(missing item)";
						w.Counts.text = string.Empty;
						allOK = false;
					}
				}
				else
				{
					if (w.Root != null) w.Root.SetActive(false);
				}
			}

			bool hasRoom = recipe.Output != null
				&& InventoryOps.RoomFor(_inventory.Slots, recipe.Output.Id) >= recipe.OutputCount;

			string reason = !allOK ? "Missing ingredients" : (!hasRoom ? "Inventory full" : string.Empty);
			SetCraftEnabled(allOK && hasRoom, reason);
		}

		private bool HasAllIngredients(RecipeDefinition recipe)
		{
			if (recipe == null || _inventory == null) return false;
			if (recipe.Ingredients == null) return true;
			for (int i = 0; i < recipe.Ingredients.Length; i++)
			{
				var ing = recipe.Ingredients[i];
				if (ing.Item == null || ing.Count <= 0) continue;
				if (InventoryOps.CountItem(_inventory.Slots, ing.Item.Id) < ing.Count) return false;
			}
			return true;
		}

		private void SetCraftEnabled(bool enabled, string disabledReason)
		{
			_craftButton.interactable = enabled;
			_craftButtonBg.color = enabled ? CraftEnabled : CraftDisabled;
			_craftLabel.text = enabled ? "CRAFT" : (string.IsNullOrEmpty(disabledReason) ? "CRAFT" : disabledReason.ToUpperInvariant());
		}

		private void OnRowClicked(int recipeId)
		{
			if (recipeId == 0) return;
			_selectedRecipeId = recipeId;
			RefreshRowAvailability();
			RefreshDetail();
		}

		private void OnCraftClicked()
		{
			if (_session == null || _selectedRecipeId == 0) return;
			_session.RequestCraft(_selectedRecipeId);
		}

		// --- Layout build ---

		private void BuildLayout()
		{
			var rt = transform as RectTransform;
			if (rt == null)
			{
				Debug.LogError("[CraftingHUD] Must live on a RectTransform (under a Canvas).");
				return;
			}

			rt.anchorMin = new Vector2(0.5f, 0.5f);
			rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = Vector2.zero;
			rt.sizeDelta = PanelSize;

			_root = new GameObject("CraftingPanel", typeof(RectTransform), typeof(Image));
			_root.transform.SetParent(transform, false);
			var panelRT = (RectTransform)_root.transform;
			panelRT.anchorMin = Vector2.zero;
			panelRT.anchorMax = Vector2.one;
			panelRT.offsetMin = Vector2.zero;
			panelRT.offsetMax = Vector2.zero;
			_root.GetComponent<Image>().color = PanelColor;

			// Header
			var header = new GameObject("Header", typeof(RectTransform), typeof(Image));
			header.transform.SetParent(_root.transform, false);
			var hrt = (RectTransform)header.transform;
			hrt.anchorMin = new Vector2(0f, 1f);
			hrt.anchorMax = new Vector2(1f, 1f);
			hrt.pivot = new Vector2(0.5f, 1f);
			hrt.anchoredPosition = Vector2.zero;
			hrt.sizeDelta = new Vector2(0f, 36f);
			header.GetComponent<Image>().color = HeaderColor;
			_headerLabel = AddText(header.transform, "HeaderLabel", "Workbench", 18, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);

			// List column
			var list = new GameObject("RecipeList", typeof(RectTransform));
			list.transform.SetParent(_root.transform, false);
			var lrt = (RectTransform)list.transform;
			lrt.anchorMin = new Vector2(0f, 0f);
			lrt.anchorMax = new Vector2(0f, 1f);
			lrt.pivot = new Vector2(0f, 1f);
			lrt.anchoredPosition = new Vector2(8f, -42f);
			lrt.sizeDelta = new Vector2(ListWidth, -50f);

			for (int i = 0; i < MaxRows; i++)
			{
				_rows[i] = BuildRow(list.transform, i);
			}

			_emptyHint = AddText(list.transform, "EmptyHint", "No recipes known", 14, TextAlignmentOptions.Center,
				new Vector2(0f, 1f), new Vector2(1f, 1f));
			var ehrt = _emptyHint.rectTransform;
			ehrt.pivot = new Vector2(0.5f, 1f);
			ehrt.anchoredPosition = new Vector2(0f, -10f);
			ehrt.sizeDelta = new Vector2(0f, 24f);
			_emptyHint.gameObject.SetActive(false);

			// Detail column
			var detail = new GameObject("DetailPanel", typeof(RectTransform));
			detail.transform.SetParent(_root.transform, false);
			var drt = (RectTransform)detail.transform;
			drt.anchorMin = new Vector2(0f, 0f);
			drt.anchorMax = new Vector2(1f, 1f);
			drt.pivot = new Vector2(0f, 0f);
			drt.offsetMin = new Vector2(ListWidth + 16f, 8f);
			drt.offsetMax = new Vector2(-8f, -42f);

			var iconGO = new GameObject("OutputIcon", typeof(RectTransform), typeof(Image));
			iconGO.transform.SetParent(detail.transform, false);
			var iconRT = (RectTransform)iconGO.transform;
			iconRT.anchorMin = new Vector2(0f, 1f);
			iconRT.anchorMax = new Vector2(0f, 1f);
			iconRT.pivot = new Vector2(0f, 1f);
			iconRT.anchoredPosition = new Vector2(0f, 0f);
			iconRT.sizeDelta = new Vector2(72f, 72f);
			_detailIcon = iconGO.GetComponent<Image>();
			_detailIcon.preserveAspect = true;
			_detailIcon.raycastTarget = false;

			_detailName = AddText(detail.transform, "OutputName", string.Empty, 22, TextAlignmentOptions.TopLeft,
				new Vector2(0f, 1f), new Vector2(1f, 1f));
			var dnrt = _detailName.rectTransform;
			dnrt.pivot = new Vector2(0f, 1f);
			dnrt.anchoredPosition = new Vector2(84f, 0f);
			dnrt.sizeDelta = new Vector2(-84f, 32f);

			_detailDescription = AddText(detail.transform, "Description", string.Empty, 14, TextAlignmentOptions.TopLeft,
				new Vector2(0f, 1f), new Vector2(1f, 1f));
			var ddrt = _detailDescription.rectTransform;
			ddrt.pivot = new Vector2(0f, 1f);
			ddrt.anchoredPosition = new Vector2(84f, -36f);
			ddrt.sizeDelta = new Vector2(-84f, 40f);
			_detailDescription.textWrappingMode = TextWrappingModes.Normal;

			var ingredientsHeader = AddText(detail.transform, "IngredientsHeader", "Ingredients", 14,
				TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(1f, 1f));
			var ihrt = ingredientsHeader.rectTransform;
			ihrt.pivot = new Vector2(0f, 1f);
			ihrt.anchoredPosition = new Vector2(0f, -90f);
			ihrt.sizeDelta = new Vector2(0f, 20f);

			for (int i = 0; i < MaxIngredientRows; i++)
			{
				_ingredientRows[i] = BuildIngredientRow(detail.transform, i);
			}

			// Craft button
			var btnGO = new GameObject("CraftButton", typeof(RectTransform), typeof(Image), typeof(Button));
			btnGO.transform.SetParent(detail.transform, false);
			var brt = (RectTransform)btnGO.transform;
			brt.anchorMin = new Vector2(0.5f, 0f);
			brt.anchorMax = new Vector2(0.5f, 0f);
			brt.pivot = new Vector2(0.5f, 0f);
			brt.anchoredPosition = new Vector2(0f, 8f);
			brt.sizeDelta = new Vector2(220f, 44f);
			_craftButtonBg = btnGO.GetComponent<Image>();
			_craftButtonBg.color = CraftDisabled;
			_craftButton = btnGO.GetComponent<Button>();
			_craftButton.onClick.AddListener(OnCraftClicked);

			_craftLabel = AddText(btnGO.transform, "Label", "CRAFT", 18, TextAlignmentOptions.Center,
				Vector2.zero, Vector2.one);
			_craftLabel.raycastTarget = false;
		}

		private RowWidget BuildRow(Transform parent, int index)
		{
			var row = new GameObject($"Row_{index}", typeof(RectTransform), typeof(Image), typeof(Button));
			row.transform.SetParent(parent, false);
			var rrt = (RectTransform)row.transform;
			rrt.anchorMin = new Vector2(0f, 1f);
			rrt.anchorMax = new Vector2(1f, 1f);
			rrt.pivot = new Vector2(0f, 1f);
			rrt.anchoredPosition = new Vector2(0f, -index * (RowHeight + 4f));
			rrt.sizeDelta = new Vector2(0f, RowHeight);
			var bg = row.GetComponent<Image>();
			bg.color = RowNormal;

			var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
			iconGO.transform.SetParent(row.transform, false);
			var irt = (RectTransform)iconGO.transform;
			irt.anchorMin = new Vector2(0f, 0.5f);
			irt.anchorMax = new Vector2(0f, 0.5f);
			irt.pivot = new Vector2(0f, 0.5f);
			irt.anchoredPosition = new Vector2(6f, 0f);
			irt.sizeDelta = new Vector2(RowHeight - 12f, RowHeight - 12f);
			var icon = iconGO.GetComponent<Image>();
			icon.preserveAspect = true;
			icon.raycastTarget = false;
			icon.enabled = false;

			var label = AddText(row.transform, "Label", string.Empty, 14, TextAlignmentOptions.MidlineLeft,
				new Vector2(0f, 0f), new Vector2(1f, 1f));
			var lrt2 = label.rectTransform;
			lrt2.offsetMin = new Vector2(RowHeight, 0f);
			lrt2.offsetMax = new Vector2(-4f, 0f);
			label.raycastTarget = false;

			var button = row.GetComponent<Button>();
			button.targetGraphic = bg;
			int captured = index;
			button.onClick.AddListener(() => OnRowClicked(_rows[captured].RecipeId));

			return new RowWidget
			{
				Root = row,
				Background = bg,
				Icon = icon,
				Label = label,
				Button = button,
				RecipeId = 0,
			};
		}

		private IngredientWidget BuildIngredientRow(Transform parent, int index)
		{
			var row = new GameObject($"Ingredient_{index}", typeof(RectTransform));
			row.transform.SetParent(parent, false);
			var rrt = (RectTransform)row.transform;
			rrt.anchorMin = new Vector2(0f, 1f);
			rrt.anchorMax = new Vector2(1f, 1f);
			rrt.pivot = new Vector2(0f, 1f);
			rrt.anchoredPosition = new Vector2(0f, -114f - index * (IngredientRowHeight + 2f));
			rrt.sizeDelta = new Vector2(0f, IngredientRowHeight);

			var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
			iconGO.transform.SetParent(row.transform, false);
			var irt = (RectTransform)iconGO.transform;
			irt.anchorMin = new Vector2(0f, 0.5f);
			irt.anchorMax = new Vector2(0f, 0.5f);
			irt.pivot = new Vector2(0f, 0.5f);
			irt.anchoredPosition = new Vector2(0f, 0f);
			irt.sizeDelta = new Vector2(IngredientRowHeight, IngredientRowHeight);
			var icon = iconGO.GetComponent<Image>();
			icon.preserveAspect = true;
			icon.raycastTarget = false;
			icon.enabled = false;

			var label = AddText(row.transform, "Label", string.Empty, 14, TextAlignmentOptions.MidlineLeft,
				new Vector2(0f, 0f), new Vector2(1f, 1f));
			var lrt = label.rectTransform;
			lrt.offsetMin = new Vector2(IngredientRowHeight + 6f, 0f);
			lrt.offsetMax = new Vector2(-60f, 0f);
			label.raycastTarget = false;

			var counts = AddText(row.transform, "Counts", string.Empty, 14, TextAlignmentOptions.MidlineRight,
				new Vector2(1f, 0f), new Vector2(1f, 1f));
			var crt = counts.rectTransform;
			crt.pivot = new Vector2(1f, 0.5f);
			crt.anchoredPosition = new Vector2(-4f, 0f);
			crt.sizeDelta = new Vector2(56f, IngredientRowHeight);
			counts.raycastTarget = false;

			row.SetActive(false);
			return new IngredientWidget { Root = row, Icon = icon, Label = label, Counts = counts };
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
