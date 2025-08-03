using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace MovementOverhaul
{
    public class WalkStandLogic
    {
        private readonly IModHelper Helper;
        private float regenTickTimer = 1f;

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
                return;
            }

            bool isSlowWalking = Game1.player.isMoving() && !Game1.player.running;
            bool isStandingStill = !Game1.player.isMoving();

            float regenRate = 0f;

            if (isSlowWalking && ModEntry.Config.RegenStaminaOnWalk)
            {
                regenRate = ModEntry.Config.WalkRegenPerSecond;
            }
            else if (isStandingStill && ModEntry.Config.RegenStaminaOnStand)
            {
                regenRate = ModEntry.Config.StandRegenPerSecond;
            }

            if (regenRate <= 0f)
            {
                return;
            }

            this.regenTickTimer -= (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
            if (this.regenTickTimer <= 0)
            {
                this.regenTickTimer = 1f;
                Game1.player.stamina = Math.Min(Game1.player.MaxStamina, Game1.player.stamina + regenRate);
            }
        }
    }
}