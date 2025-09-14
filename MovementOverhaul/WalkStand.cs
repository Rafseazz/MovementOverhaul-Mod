using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace MovementOverhaul
{
    public class WalkStandLogic
    {
        private readonly IModHelper Helper;

        // Timers for regeneration
        private float delayTimer = 0f;
        private float regenTickTimer = 1f;

        // State tracking to know when to start the delay
        private bool wasInRegenStateLastTick = false;

        public WalkStandLogic(IModHelper helper)
        {
            this.Helper = helper;
        }

        public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady
                || Game1.player.stamina >= Game1.player.MaxStamina
                || ModEntry.SprintLogic.IsSprinting
                || ModEntry.SitLogic.IsSittingOnGround)
            {
                // Only log if we were just in a regen state, to indicate why it stopped.
                if (this.wasInRegenStateLastTick)
                {
                    ModEntry.Instance.LogDebug("Player is no longer in a valid regen state (sprinting, sitting, full stamina, etc). Stopping regen.");
                }
                this.wasInRegenStateLastTick = false;
                return;
            }

            // Determine player's current state.
            bool isSlowWalking = Game1.player.isMoving() && !Game1.player.running;
            bool isStandingStill = !Game1.player.isMoving();

            // Determine if the player is in any valid state for passive regeneration.
            bool isInRegenStateThisTick = (isSlowWalking && ModEntry.Instance.Config.RegenStaminaOnWalk) || (isStandingStill && ModEntry.Instance.Config.RegenStaminaOnStand);

            // If the player just entered a regen state, start the delay timer.
            if (isInRegenStateThisTick && !this.wasInRegenStateLastTick)
            {
                string state = isSlowWalking ? "Walking" : "Standing";
                ModEntry.Instance.LogDebug($"Player entered '{state}' regen state. Starting {ModEntry.Instance.Config.WalkStandRegenDelaySeconds}s delay timer.");
                this.delayTimer = ModEntry.Instance.Config.WalkStandRegenDelaySeconds;
            }

            this.wasInRegenStateLastTick = isInRegenStateThisTick;

            // If the player is not in a regen state, do nothing further.
            if (!isInRegenStateThisTick)
            {
                return;
            }

            // Tick down the initial delay.
            if (this.delayTimer > 0f)
            {
                this.delayTimer -= (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
                return;
            }

            // If the delay is over, determine the rate and apply regeneration.
            float regenRate = 0f;
            if (isSlowWalking)
                regenRate = ModEntry.Instance.Config.WalkRegenPerSecond;
            else if (isStandingStill)
                regenRate = ModEntry.Instance.Config.StandRegenPerSecond;

            if (regenRate > 0f)
            {
                this.regenTickTimer -= (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
                if (this.regenTickTimer <= 0)
                {
                    this.regenTickTimer = 1f; // Reset for the next second
                    ModEntry.Instance.LogDebug($"Regen tick: Adding {regenRate} stamina. Current: {Game1.player.stamina:F1}");
                    Game1.player.stamina = Math.Min(Game1.player.MaxStamina, Game1.player.stamina + regenRate);
                }
            }
        }
    }
}