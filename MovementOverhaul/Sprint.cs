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
            if (!ModEntry.Config.EnableSprint) return;

            switch (ModEntry.Config.SprintActivation)
            {
                case SprintMode.DoubleTap:
                    this.HandleKeyboardDoubleTap(e.Button);
                    break;
                case SprintMode.Toggle:
                    if (e.Button == ModEntry.Config.SprintKey)
                    {
                        this.isToggleSprintOn = !this.isToggleSprintOn;
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

            if (button == this.lastMoveKeyPressed && currentTime - this.lastKeyPressTime < TapTimeThreshold)
                this.tapCount++;
            else
                this.tapCount = 1;

            this.lastMoveKeyPressed = button;
            this.lastKeyPressTime = currentTime;

            if (this.tapCount == 2 && !this.IsSprinting)
                this.ActivateSprint();
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
            if (!ModEntry.Config.EnableSprint)
            {
                if (this.IsSprinting) this.StopSprint();
                return;
            }

            if (ModEntry.Config.SprintActivation == SprintMode.DoubleTap)
            {
                this.HandleControllerDoubleTap();
            }

            if (!Context.CanPlayerMove)
            {
                if (this.IsSprinting) this.StopSprint();
                return;
            }

            if (ModEntry.Config.SprintActivation == SprintMode.DoubleTap)
            {
                if (this.IsSprinting)
                {
                    this.HandleActiveSprint((float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds);
                }
                return;
            }

            bool shouldBeSprinting = false;
            if (ModEntry.Config.SprintActivation == SprintMode.Hold)
            {
                shouldBeSprinting = this.Helper.Input.IsDown(ModEntry.Config.SprintKey);
            }
            else if (ModEntry.Config.SprintActivation == SprintMode.Toggle)
            {
                shouldBeSprinting = this.isToggleSprintOn;
            }

            if (shouldBeSprinting && !this.IsSprinting) this.ActivateSprint();
            else if (!shouldBeSprinting && this.IsSprinting) this.StopSprint();

            if (this.IsSprinting)
            {
                this.HandleActiveSprint((float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds);
            }
        }

        private void HandleActiveSprint(float elapsedSeconds)
        {
            if (ModEntry.Config.SprintActivation == SprintMode.DoubleTap)
            {
                this.sprintTimer -= elapsedSeconds;
                if (this.sprintTimer <= 0)
                {
                    this.StopSprint();
                    return;
                }
            }

            if (Game1.player.stamina <= 1)
            {
                this.StopSprint();
                return;
            }

            if (Game1.player.isMoving())
            {
                this.staminaDrainTimer -= elapsedSeconds;
                if (this.staminaDrainTimer <= 0)
                {
                    this.staminaDrainTimer = 1f;
                    float cost = ModEntry.Config.SprintStaminaCostPerSecond;
                    Game1.player.stamina = Math.Max(0, Game1.player.stamina - cost);
                }

                this.particleEffectTimer -= elapsedSeconds;
                if (this.particleEffectTimer <= 0)
                {
                    this.particleEffectTimer = 0.1f;
                    if (ModEntry.Config.SprintParticleEffect != "None")
                    {
                        this.Multiplayer.SendMessage(new SprintParticleMessage(Game1.player.UniqueMultiplayerID, ModEntry.Config.SprintParticleEffect), "CreateSprintParticle", modIDs: new[] { this.ModManifest.UniqueID });
                        this.CreateParticle(Game1.player, ModEntry.Config.SprintParticleEffect);
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
                if (ModEntry.Config.SprintActivation == SprintMode.Toggle)
                    this.isToggleSprintOn = false;
                return;
            }

            this.IsSprinting = true;
            this.staminaDrainTimer = 0.5f;

            if (ModEntry.Config.SprintActivation == SprintMode.DoubleTap)
            {
                this.sprintTimer = ModEntry.Config.SprintDurationSeconds;
            }

            Game1.playSound("hoeHit");
        }

        private void StopSprint()
        {
            this.IsSprinting = false;
            this.tapCount = 0;
            this.lastControllerDirection = -1;

            if (ModEntry.Config.SprintActivation == SprintMode.Toggle)
            {
                this.isToggleSprintOn = false;
                Game1.playSound("woodyStep");
            }
            else
            {
                Game1.playSound("woodyStep");
            }
        }

        public float GetSpeedMultiplier()
        {
            if (!ModEntry.Config.EnableSprint || !this.IsSprinting)
                return 1f;

            float multiplier = Game1.player.isRidingHorse()
                ? ModEntry.Config.HorseSprintSpeedMultiplier
                : ModEntry.Config.SprintSpeedMultiplier;

            if (ModEntry.Config.PathSpeedBonus && Game1.player.isMoving())
            {
                string tileType = Game1.currentLocation.doesTileHaveProperty((int)Game1.player.Tile.X, (int)Game1.player.Tile.Y, "Type", "Back");
                if (tileType is "Wood" or "Stone")
                {
                    multiplier *= ModEntry.Config.PathSpeedBonusMultiplier;
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
                __result *= ModEntry.SprintLogic.GetSpeedMultiplier();
            }
        }
    }
}