// MovementOverhaul/Sprint.cs

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

        private bool isSprinting = false;

        // State for DoubleTap mode
        private SButton lastMoveKeyPressed = SButton.None;
        private uint lastKeyPressTime = 0;
        private int tapCount = 0;
        private float sprintTimer = 0f;
        private const uint TapTimeThreshold = 300;

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
                    this.HandleDoubleTap(e.Button);
                    break;
                case SprintMode.Toggle:
                    if (e.Button == ModEntry.Config.SprintKey)
                    {
                        this.isToggleSprintOn = !this.isToggleSprintOn;
                        if (this.isToggleSprintOn) Game1.playSound("hoeHit");
                        else Game1.playSound("woodyStep");
                    }
                    break;
                    // NOTE: Hold mode is handled entirely in OnUpdateTicked, so this method does nothing for it.
            }
        }

        private void HandleDoubleTap(SButton button)
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

            if (this.tapCount == 2 && !this.isSprinting)
                this.ActivateSprint();
        }

        public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!ModEntry.Config.EnableSprint)
            {
                // Failsafe??
                if (this.isSprinting) this.StopSprint();
                return;
            }

            if (!Context.CanPlayerMove)
            {
                if (this.isSprinting) this.StopSprint();
                return;
            }

            bool shouldBeSprinting = false;
            switch (ModEntry.Config.SprintActivation)
            {
                case SprintMode.Hold:
                    shouldBeSprinting = this.Helper.Input.IsDown(ModEntry.Config.SprintKey);
                    break;
                case SprintMode.Toggle:
                    shouldBeSprinting = this.isToggleSprintOn;
                    break;
                case SprintMode.DoubleTap:
                    shouldBeSprinting = this.isSprinting;
                    break;
            }

            if (shouldBeSprinting && !this.isSprinting) this.ActivateSprint();
            else if (!shouldBeSprinting && this.isSprinting) this.StopSprint();

            if (this.isSprinting)
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
                // Stamina Drain
                this.staminaDrainTimer -= elapsedSeconds;
                if (this.staminaDrainTimer <= 0)
                {
                    this.staminaDrainTimer = 1f;
                    float cost = ModEntry.Config.SprintStaminaCostPerSecond;
                    Game1.player.stamina = Math.Max(0, Game1.player.stamina - cost);
                }

                // Particle Effects
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

                // Horse Sound Effects
                if (Game1.player.isRidingHorse())
                {
                    this.horseSoundTimer -= elapsedSeconds;
                    if (this.horseSoundTimer <= 0f)
                    {
                        this.horseSoundTimer = 0.45f;
                        Game1.playSound("horse_fluffy_step");
                    }
                }
            }
        }

        private void ActivateSprint()
        {
            if (Game1.player.stamina <= 1) return;

            this.isSprinting = true;
            this.staminaDrainTimer = 0.5f;

            if (ModEntry.Config.SprintActivation == SprintMode.DoubleTap)
            {
                this.sprintTimer = ModEntry.Config.SprintDurationSeconds;
            }

            Game1.playSound("hoeHit");
        }

        private void StopSprint()
        {
            this.isSprinting = false;
            this.tapCount = 0;
            if (ModEntry.Config.SprintActivation == SprintMode.Toggle)
                this.isToggleSprintOn = false;
        }

        public float GetSpeedMultiplier()
        {
            if (!ModEntry.Config.EnableSprint) return 1f;

            if (!this.isSprinting) return 1f;
            return Game1.player.isRidingHorse()
                ? ModEntry.Config.HorseSprintSpeedMultiplier
                : ModEntry.Config.SprintSpeedMultiplier;
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

        private TemporaryAnimatedSprite? GetSpriteForType(string particleType, Vector2 position)
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