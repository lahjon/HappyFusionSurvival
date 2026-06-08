using System;
using Starter.Common.Inventory;

namespace Starter.Shooter
{
	/// <summary>
	/// Item facet that tags an item as a harvesting tool of a given <see cref="ToolTypeTag"/>
	/// (Axe, Pickaxe, ...). Composes with <see cref="WeaponCapability"/>: the tool is still swung as a
	/// weapon (same fire path, swing feel), and this facet is what lets a <c>ResourceNode</c> recognize
	/// the swing as the right tool. Stateless authoring data, like every other capability.
	///
	/// Resolved at fire time by Inventory (<c>ActiveToolTag</c>) and carried to the resource node via
	/// <c>ActorContext.ToolTag</c>; non-tool items leave the tag null, so they harvest nothing.
	/// </summary>
	[Serializable]
	public sealed class ToolCapability : ItemCapability
	{
		public ToolTypeTag ToolType;
	}
}
