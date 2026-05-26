namespace Starter.Common.Crafting
{
	/// <summary>
	/// Implemented by per-mode networked recipe books (e.g. <c>Starter.Shooter.RecipeBook</c>).
	/// Lets shared code in <c>Starter.Common.Crafting</c> (e.g. recipe-unlock consumables)
	/// grant recipes without taking a hard dependency on a per-mode namespace.
	/// </summary>
	public interface IRecipeBook
	{
		bool IsKnown(int recipeId);

		/// <summary>State authority only on the implementing component. Idempotent.</summary>
		void Unlock(int recipeId);
	}
}
