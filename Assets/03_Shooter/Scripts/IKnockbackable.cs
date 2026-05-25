using UnityEngine;

namespace Starter.Shooter
{
	public interface IKnockbackable
	{
		void ApplyKnockback(Vector3 fromPosition, float distance);
	}
}
