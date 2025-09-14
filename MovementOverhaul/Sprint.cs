using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace MovementOverhaul
{
    public class SprintLogic
    {
        private readonly IModHelper Helper;
        private readonly IMultiplayerHelper Multiplayer;
        private readonly IManifest ModManifest;

        public bool IsSprinting { get; private set; } = false;

        // A timer to track the grace period after a sprint ends.
        private float timeSinceSprintStopped = 999f;

        // State for Keyboard DoubleTap
        private SButton lastMoveKeyPressed = SButton.None;
        private uint lastKeyPressTime = 0;
        private int tapCount = 0;
        private float sprintTimer = 0f;
        private const uint TapTimeThreshold = 300;

        // State for Controller DoubleTap
        private int lastControllerDirection = -1;
        private uint lastControllerFlickTime = 0;

        // State for Toggle mode
        private bool isToggleSprintOn = false;

        private float staminaDrainTimer = 0f;
        private float particleEffectTimer = 0f;
        private float horseSoundTimer = 0f;

        public SprintLogic(IModHelper helper, IMultiplayerHelper multiplayer, IManifest manifest)
        {
            this.Helper = helper;
            this.Multiplayer = multiplayer;
            this.ModManifest = manifest;
        }

        private bool IsMovementKey(SButton button)
        {
            return button is SButton.W or SButton.A or SButton.S or SButton.D
                or SButton.Up or SButton.Down or SButton.Left or SButton.Right;
        }

        public void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!ModEntry.Instance.Config.EnableSprint) return;

            //ModEntry.Instance.LogDebug($"Button pressed: {e.Button}. Current sprint mode: {ModEntry.Instance.Config.SprintActivation}."); //debug

            switch (ModEntry.Instance.Config.SprintActivation)
            {
                case SprintMode.DoubleTap:
                    this.HandleKeyboardDoubleTap(e.Button);
                    break;
                case SprintMode.Toggle:
                    if (e.Button == ModEntry.Instance.Config.SprintKey)
                    {
                        this.isToggleSprintOn = !this.isToggleSprintOn;
                        ModEntry.Instance.LogDebug($"Sprint toggle key pressed. New toggle state: {this.isToggleSprintOn}."); //debug
                        if (this.isToggleSprintOn) this.ActivateSprint();
                        else this.StopSprint();
                    }
                    break;
            }
        }

        private void HandleKeyboardDoubleTap(SButton button)
        {
            if (!this.IsMovementKey(button) || !Context.CanPlayerMove || Game1.player.stamina <= 1)
                return;

            uint currentTime = (uint)Game1.currentGameTime.TotalGameTime.TotalMilliseconds;
            uint timeSinceLastTap = currentTime - this.lastKeyPressTime;

            if (button == this.lastMoveKeyPressed && currentTime - this.lastKeyPressTime < TapTimeThreshold)
            {
                this.tapCount++;
                ModEntry.Instance.LogDebug($"Tap registered for '{button}'. Time since last: {timeSinceLastTap}ms. Tap count: {this.tapCount}."); //debug
            }
            else
            {
                this.tapCount = 1;
                //ModEntry.Instance.LogDebug($"First tap registered for '{button}'. Resetting tap count."); //debug
            }

            this.lastMoveKeyPressed = button;
            this.lastKeyPressTime = currentTime;

            if (this.tapCount == 2 && !this.IsSprinting)
            {
                ModEntry.Instance.LogDebug("Double-tap confirmed. Activating sprint."); //debug
                this.ActivateSprint();
            }
        }

        private void HandleControllerDoubleTap()
        {
            if (!Game1.options.gamepadControls)
                return;

            if (!Context.IsPlayerFree || !Game1.player.movementDirections.Any())
            {
                if ((uint)Game1.currentGameTime.TotalGameTime.TotalMilliseconds - this.lastControllerFlickTime > TapTimeThreshold)
                {
                    this.lastControllerDirection = -1;
                }
                return;
            }

            int currentDirection = Game1.player.movementDirections[0];
            uint currentTime = (uint)Game1.currentGameTime.TotalGameTime.TotalMilliseconds;

            if (currentDirection != -1 && currentDirection == this.lastControllerDirection && currentTime - this.lastControllerFlickTime < TapTimeThreshold)
            {
                if (!this.IsSprinting) this.ActivateSprint();
                this.lastControllerDirection = -1;
            }

            if (currentDirection != -1)
                this.lastControllerFlickTime = currentTime;

            this.lastControllerDirection = currentDirection;
        }

        public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            // If not sprinting, update the grace period timer.
            if (!this.IsSprinting)
            {
                this.timeSinceSprintStopped += (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
            }

            if (!ModEntry.Instance.Config.EnableSprint)
            {
                if (this.IsSprinting) this.StopSprint();
                return;
            }

            if (ModEntry.Instance.Config.SprintActivation == SprintMode.DoubleTap)
            {
                this.HandleControllerDoubleTap();
            }

            if (!Context.CanPlayerMove)
            {
                if (this.IsSprinting) this.StopSprint();
                return;
            }

            if (ModEntry.Instance.Config.SprintActivation == SprintMode.DoubleTap)
            {
                if (this.IsSprinting)
                {
                    this.HandleActiveSprint((float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds);
                }
                return;
            }

            bool shouldBeSprinting = false;
            if (ModEntry.Instance.Config.SprintActivation == SprintMode.Hold)
            {
                shouldBeSprinting = this.Helper.Input.IsDown(ModEntry.Instance.Config.SprintKey);
            }
            else if (ModEntry.Instance.Config.SprintActivation == SprintMode.Toggle)
            {
                shouldBeSprinting = this.isToggleSprintOn;
            }

            if (shouldBeSprinting && !this.IsSprinting)
            {
                ModEntry.Instance.LogDebug($"'{ModEntry.Instance.Config.SprintActivation}' mode condition met. Activating sprint.");
                this.ActivateSprint();
            }
            else if (!shouldBeSprinting && this.IsSprinting)
            {
                ModEntry.Instance.LogDebug($"'{ModEntry.Instance.Config.SprintActivation}' mode condition no longer met. Stopping sprint.");
                this.StopSprint();
            }
            if (this.IsSprinting)
            {
                this.HandleActiveSprint((float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds);
            }
        }

        private void HandleActiveSprint(float elapsedSeconds)
        {
            if (ModEntry.Instance.Config.SprintActivation == SprintMode.DoubleTap)
            {
                this.sprintTimer -= elapsedSeconds;
                if (this.sprintTimer <= 0)
                {
                    ModEntry.Instance.LogDebug("Double-tap sprint timer expired. Stopping sprint.");
                    this.StopSprint();
                    return;
                }
            }

            if (Game1.player.stamina <= 1)
            {
                ModEntry.Instance.LogDebug($"Player out of stamina ({Game1.player.stamina}). Stopping sprint.");
                this.StopSprint();
                return;
            }

            if (Game1.player.isMoving())
            {
                this.staminaDrainTimer -= elapsedSeconds;
                if (this.staminaDrainTimer <= 0)
                {
                    this.staminaDrainTimer = 1f;
                    bool freeSprint = Game1.player.isRidingHorse() && ModEntry.Instance.Config.NoStaminaDrainOnHorse;
                    if (!freeSprint)
                    {
                        float cost = ModEntry.Instance.Config.SprintStaminaCostPerSecond;
                        ModEntry.Instance.LogDebug($"Draining {cost} stamina. Current: {Game1.player.stamina}.");
                        Game1.player.stamina = Math.Max(0, Game1.player.stamina - cost);
                    }
                }

                this.particleEffectTimer -= elapsedSeconds;
                if (this.particleEffectTimer <= 0)
                {
                    this.particleEffectTimer = 0.1f;
                    if (ModEntry.Instance.Config.SprintParticleEffect != "None")
                    {
                        this.Multiplayer.SendMessage(new SprintParticleMessage(Game1.player.UniqueMultiplayerID, ModEntry.Instance.Config.SprintParticleEffect), "CreateSprintParticle", modIDs: new[] { this.ModManifest.UniqueID });
                        this.CreateParticle(Game1.player, ModEntry.Instance.Config.SprintParticleEffect);
                    }
                }

                if (Game1.player.isRidingHorse())
                {
                    this.horseSoundTimer -= elapsedSeconds;
                    if (this.horseSoundTimer <= 0f)
                    {
                        this.horseSoundTimer = 0.45f;
                        Game1.playSound("sandyStep");
                    }
                }
            }
        }

        private void ActivateSprint()
        {
            if (Game1.player.stamina <= 1)
            {
                ModEntry.Instance.LogDebug($"Sprint activation failed: Not enough stamina ({Game1.player.stamina}).");
                if (ModEntry.Instance.Config.SprintActivation == SprintMode.Toggle)
                    this.isToggleSprintOn = false;
                return;
            }

            ModEntry.Instance.LogDebug("Sprint activated! WEEEEEE");
            this.IsSprinting = true;
            this.staminaDrainTimer = 0.5f;

            if (ModEntry.Instance.Config.SprintActivation == SprintMode.DoubleTap)
            {
                this.sprintTimer = ModEntry.Instance.Config.SprintDurationSeconds;
            }

            Game1.playSound("hoeHit");
        }

        private void StopSprint()
        {
            if (!this.IsSprinting) return;

            ModEntry.Instance.LogDebug("Stopped spriting Aww");
            this.IsSprinting = false;
            this.timeSinceSprintStopped = 0f;
            this.tapCount = 0;
            this.lastControllerDirection = -1;

            if (ModEntry.Instance.Config.SprintActivation == SprintMode.Toggle)
            {
                this.isToggleSprintOn = false;
                Game1.playSound("woodyStep");
            }
            else
            {
                Game1.playSound("woodyStep");
            }
        }

        // A public method for the combat logic to check the grace period.
        public bool WasSprintingRecently()
        {
            // Sprint is active if currently sprinting OR if the grace period (0.2s) hasn't passed.
            return this.IsSprinting || this.timeSinceSprintStopped < ModEntry.Instance.Config.SprintAttackGracePeriod; 
        }


        public float GetSpeedMultiplier()
        {
            if (!ModEntry.Instance.Config.EnableSprint || !this.IsSprinting)
                return 1f;

            float multiplier = Game1.player.isRidingHorse()
                ? ModEntry.Instance.Config.HorseSprintSpeedMultiplier
                : ModEntry.Instance.Config.SprintSpeedMultiplier;

            if (ModEntry.Instance.Config.PathSpeedBonus && Game1.player.isMoving())
            {
                string tileType = Game1.currentLocation.doesTileHaveProperty((int)Game1.player.Tile.X, (int)Game1.player.Tile.Y, "Type", "Back");
                if (tileType is "Wood" or "Stone")
                {
                    multiplier *= ModEntry.Instance.Config.PathSpeedBonusMultiplier;
                }
            }

            return multiplier;
        }

        public void CreateRemoteParticle(long playerID, string particleType)
        {
            Farmer? farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == playerID);
            if (farmer != null && !farmer.IsLocalPlayer)
            {
                this.CreateParticle(farmer, particleType);
            }
        }

        private void CreateParticle(Farmer who, string particleType)
        {
            Vector2 basePos = who.getStandingPosition() + new Vector2(0, -16);
            Vector2 offset = who.FacingDirection switch
            {
                0 => new Vector2(-32, 32),
                1 => new Vector2(-80, -16),
                2 => new Vector2(-32, -32),
                3 => new Vector2(16, -32),
                _ => Vector2.Zero
            };
            Vector2 particlePos = basePos + offset + new Vector2(Game1.random.Next(-8, 8), Game1.random.Next(-8, 8));

            TemporaryAnimatedSprite? spriteToAdd = GetSpriteForType(particleType, particlePos);
            if (spriteToAdd != null)
            {
                who.currentLocation.temporarySprites.Add(spriteToAdd);
            }
        }

        public static TemporaryAnimatedSprite? GetSpriteForType(string particleType, Vector2 position)
        {
            switch (particleType)
            {
                case "Smoke": return new TemporaryAnimatedSprite("TileSheets\\animations", new Rectangle(0, 320, 64, 64), 30f, 3, 0, position, false, false) { alphaFade = 0.05f };
                case "GreenDust": return new TemporaryAnimatedSprite("TileSheets\\animations", new Rectangle(0, 256, 64, 64), 30f, 4, 0, position, false, false);
                case "Circular": return new TemporaryAnimatedSprite("TileSheets\\animations", new Rectangle(0, 0, 64, 64), 30f, 4, 0, position, false, false);
                case "Leaves": return new TemporaryAnimatedSprite("TileSheets\\animations", new Rectangle(0, 1088, 64, 64), 30f, 4, 0, position, false, false);
                case "Fire1": return new TemporaryAnimatedSprite("TileSheets\\animations", new Rectangle(0, 1920, 64, 64), 50f, 4, 0, position, false, false);
                case "Fire2": return new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(276, 1965, 8, 8), 50f, 7, 0, position, false, false) { scale = 4f };
                case "BlueFire": return new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(536, 1945, 8, 8), 50f, 7, 0, position, false, false) { scale = 4f };
                case "Stars": return new TemporaryAnimatedSprite("TileSheets\\animations", new Rectangle(0, 192, 64, 64), 50f, 8, 0, position, false, false);
                case "Water Splash": return new TemporaryAnimatedSprite("TileSheets\\animations", new Rectangle(0, 832, 64, 64), 80f, 5, 0, position, false, false);
                case "Poison": return new TemporaryAnimatedSprite("TileSheets\\animations", new Rectangle(256, 1920, 64, 64), 30f, 4, 0, position, false, false);
                default: return null;
            }
        }
    }

    [HarmonyPatch(typeof(Farmer), "getMovementSpeed")]
    public class Farmer_GetMovementSpeed_Patch
    {
        public static void Postfix(Farmer __instance, ref float __result)
        {
            if (__instance.IsLocalPlayer && ModEntry.SprintLogic != null)
            {
                float originalSpeed = __result;
                float multiplier = ModEntry.SprintLogic.GetSpeedMultiplier();
                if (multiplier > 1f)
                {
                    __result *= multiplier;
                    //ModEntry.Instance.LogDebug($"[Harmony] Patched speed for {__instance.Name}. Original: {originalSpeed:F2}, Multiplier: {multiplier:F2}, Final: {__result:F2}");
                }
            }
        }
    }
}