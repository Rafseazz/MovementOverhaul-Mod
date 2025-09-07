using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace MovementOverhaul
{
    public class AnimationLogic
    {
        private readonly IModHelper Helper;

        // State for smoother turning
        private int lastFacingDirection = 2; // Default to down
        private int turnAnimationTimer = 0;

        // State for adaptive animation speed
        private float defaultAnimationInterval = 0f;

        public AnimationLogic(IModHelper helper)
        {
            this.Helper = helper;
        }

        public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            // Safely initialize the default animation interval once a save is loaded.
            // This prevents a crash on game startup.
            if (this.defaultAnimationInterval == 0f && Context.IsWorldReady)
            {
                this.defaultAnimationInterval = Game1.player.FarmerSprite.interval;
            }

            if (!Context.IsWorldReady || !Game1.player.CanMove)
                return;

            // Smoother turning is a client-side-only cosmetic effect for the local player.
            this.HandleSmootherTurning(Game1.player);

            // Adaptive speed is synced by having all clients run the logic for all farmers.
            this.HandleAdaptiveAnimationSpeed();

            this.lastFacingDirection = Game1.player.FacingDirection;
        }

        private void HandleSmootherTurning(Farmer who)
        {
            if (!ModEntry.Config.SmootherTurningAnimation || !who.IsLocalPlayer)
            {
                // Ensure timer is off if feature is disabled.
                if (this.turnAnimationTimer > 0) this.turnAnimationTimer = 0;
                return;
            }

            int currentDirection = who.FacingDirection;
            bool isSharpTurn = (this.lastFacingDirection == 1 && currentDirection == 3) || (this.lastFacingDirection == 3 && currentDirection == 1);

            // Start the timer only if a turn is detected and we're not already in the animation.
            if (this.turnAnimationTimer == 0 && who.isMoving() && isSharpTurn)
            {
                this.turnAnimationTimer = 3; // A 3-frame transition
            }

            // If the transition animation is active...
            if (this.turnAnimationTimer > 0)
            {
                this.turnAnimationTimer--;

                // For the first part of the transition, show the neutral frame.
                if (this.turnAnimationTimer > 0)
                {
                    who.showFrame(0); // Face down
                }
                else // This is the final frame of the transition (timer just hit 0).
                {
                    // Release the animation lock.
                    who.FarmerSprite.StopAnimation();

                    // And immediately force the correct final frame for the new direction.
                    switch (currentDirection)
                    {
                        case 1: // Right
                            who.flip = false;
                            who.FarmerSprite.setCurrentFrame(6);
                            break;
                        case 3: // Left
                            who.flip = true;
                            who.FarmerSprite.setCurrentFrame(6);
                            break;
                    }
                }
            }
        }

        private void HandleAdaptiveAnimationSpeed()
        {
            // Don't run this logic until the default interval has been safely stored.
            if (this.defaultAnimationInterval == 0f) return;

            if (!ModEntry.Config.AdaptiveAnimationSpeed)
            {
                // If disabled, ensure all farmers in the location are reset to the default speed.
                foreach (var farmer in Game1.currentLocation.farmers)
                {
                    farmer.FarmerSprite.interval = this.defaultAnimationInterval;
                }
                return;
            }

            // This loops through all farmers in the current location to sync the animation for everyone.
            foreach (var farmer in Game1.currentLocation.farmers)
            {
                if (farmer.isMoving())
                {
                    float currentSpeed = farmer.getMovementSpeed();
                    const float baseSpeed = 5f; // Farmer's default running speed.

                    if (currentSpeed > 0)
                    {
                        float speedRatio = baseSpeed / currentSpeed;
                        farmer.FarmerSprite.interval = this.defaultAnimationInterval * speedRatio;
                    }
                }
                else
                {
                    // When a farmer is standing still, reset their animation interval to the default.
                    farmer.FarmerSprite.interval = this.defaultAnimationInterval;
                }
            }
        }
    }
}