using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace MovementOverhaul
{
    public class SprintLogic
    {
        private readonly ModConfig Config;
        private SButton lastMoveKeyPressed = SButton.None;
        private uint lastKeyPressTime = 0;
        private int tapCount = 0;
        private bool isSprinting = false;
        private float sprintTimer = 0f;
        private float staminaDrainTimer = 0f;
        private float particleEffectTimer = 0f;
        private const uint TapTimeThreshold = 300;
        private readonly IMultiplayerHelper Multiplayer;
        private readonly IManifest ModManifest;

        public SprintLogic(ModConfig config, IMultiplayerHelper multiplayer, IManifest manifest)
        {
            this.Config = config;
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
            if (!this.IsMovementKey(e.Button) || !Context.CanPlayerMove || Game1.player.stamina <= 1)
                return;

            uint currentTime = (uint)Game1.currentGameTime.TotalGameTime.TotalMilliseconds;

            if (e.Button == this.lastMoveKeyPressed && currentTime - this.lastKeyPressTime < TapTimeThreshold)
                this.tapCount++;
            else
                this.tapCount = 1;

            this.lastMoveKeyPressed = e.Button;
            this.lastKeyPressTime = currentTime;

            if (this.tapCount == 2 && !this.isSprinting)
                this.ActivateSprint();
        }

        private void ActivateSprint()
        {
            this.isSprinting = true;
            this.sprintTimer = this.Config.SprintDurationSeconds;
            this.staminaDrainTimer = 1f;
            Game1.playSound("woodyStep");
        }


        public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!this.isSprinting)
                return;

            float elapsedSeconds = (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;

            this.sprintTimer -= elapsedSeconds;
            if (this.sprintTimer <= 0)
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
                    float cost = this.Config.SprintStaminaCostPerSecond;
                    if (Game1.player.stamina >= cost)
                        Game1.player.stamina -= cost;
                    else
                        this.StopSprint();
                }

                this.particleEffectTimer -= elapsedSeconds;
                if (this.particleEffectTimer <= 0)
                {
                    this.particleEffectTimer = 0.1f;
                    if (this.Config.SprintParticleEffect == "None")
                        return;

                    this.Multiplayer.SendMessage(
                         new SprintParticleMessage(Game1.player.UniqueMultiplayerID, this.Config.SprintParticleEffect),
                         "CreateSprintParticle",
                         modIDs: new[] { this.ModManifest.UniqueID }
                    );

                    this.CreateParticle(Game1.player, this.Config.SprintParticleEffect);
                }
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
                Game1.currentLocation.temporarySprites.Add(spriteToAdd);
            }
        }

        public void CreateRemoteParticle(long playerID, string particleType)
        {
            Farmer? farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == playerID);
            if (farmer != null && !farmer.IsLocalPlayer)
            {
                this.CreateParticle(farmer, particleType);
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

        private void StopSprint()
        {
            this.isSprinting = false;
            this.sprintTimer = 0;
            this.tapCount = 0;
        }

        public float GetSpeedMultiplier()
        {
            return this.isSprinting ? this.Config.SprintSpeedMultiplier : 1f;
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