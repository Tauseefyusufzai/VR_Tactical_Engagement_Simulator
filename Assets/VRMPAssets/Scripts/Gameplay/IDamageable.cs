using UnityEngine;

namespace XRMultiplayer
{
    /// <summary>
    /// Interface for any object or entity in the world that can receive damage.
    /// </summary>
    public interface IDamageable
    {
        /// <summary>
        /// Applies damage to this object.
        /// </summary>
        /// <param name="amount">Amount of damage to inflict.</param>
        /// <param name="hitPoint">World position where the hit occurred.</param>
        /// <param name="hitNormal">Normal vector at the hit point.</param>
        /// <param name="attackerId">OwnerClientId of the player who dealt the damage.</param>
        void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal, ulong attackerId);
    }
}
