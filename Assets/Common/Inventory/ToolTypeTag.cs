using UnityEngine;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Identity tag for a category of harvesting tool (Axe, Pickaxe, ...). One asset per tool type.
	/// A tool item declares which type it is via <c>ToolCapability.ToolType</c>; a world
	/// <c>ResourceNode</c> declares which type can harvest it via its RequiredTool. Matching is by
	/// asset reference equality, so adding a new tool category is purely an authoring step — create
	/// one asset, wire it on the tool and the node. No enum or code changes.
	/// </summary>
	[CreateAssetMenu(menuName = "Inventory/Tool Type", fileName = "ToolType")]
	public sealed class ToolTypeTag : ScriptableObject
	{
		[Tooltip("Human-readable name for tooltips / 'requires an Axe' prompts.")]
		public string DisplayName = "Tool";
	}
}
