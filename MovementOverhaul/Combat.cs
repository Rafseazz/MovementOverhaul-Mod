using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buffs;
using StardewValley.Monsters;
using StardewValley.Tools;

namespace MovementOverhaul
{
    public class CombatLogic
    {
        private bool isDashing = false;
        private int dashTimer = 0;
        private Vector2 dashDirection;
        private float dashCooldownTimer = 0f;
        private readonly List<Monster> monstersHitThisDash = new();
        private float dashParticleTimer = 0f;
        private class DashState
        {
            public int Timer;
            public readonly Vector2 Direction;
            public DashState(int timer, Vector2 direction)
            {
                this.Timer = timer;
                this.Direction = direction;
            }
        }

        private readonly Dictionary<long, DashState> _activeRemoteDashes = new();
        private readonly IMultiplayerHelper Multiplayer;
        private readonly IManifest ModManifest;

        public bool IsPerformingDashAttack => this.isDashing;

        public CombatLogic(IModHelper helper, IMonitor monitor, IMultiplayerHelper multiplayer, IManifest modManifest)
        {
            this.Multiplayer = multiplayer;
            this.ModManifest = modManifest;
        }

        // This is the new high-priority input handler.
        public void HandleDashAttackInput(ButtonPressedEventArgs e)
        {
            // Check for the standard attack button.
            if (!e.Button.IsUseToolButton() || !Context.CanPlayerMove || Game1.player.CurrentTool is not MeleeWeapon weapon)
                return;

            ModEntry.Instance.LogDebug($"Attack input detected. Sprinting recently: {ModEntry.SprintLogic.WasSprintingRecently()}. On cooldown: {this.dashCooldownTimer > 0f}.");

            // Called instantly on button press, reliably(?) catching the sprint state.
            if (ModEntry.Instance.Config.EnableDashAttack && ModEntry.SprintLogic.WasSprintingRecently() && !this.isDashing)
            {
                if (Game1.player.stamina < ModEntry.Instance.Config.DashAttackStaminaCost)
                {
                    ModEntry.Instance.LogDebug("-> Dash attack aborted: Not enough stamina.");
                    Game1.playSound("cancel");
                    return;
                }
                ModEntry.Instance.LogDebug($"-> Stamina check passed. Draining {ModEntry.Instance.Config.DashAttackStaminaCost} stamina.");
                Game1.player.stamina -= ModEntry.Instance.Config.DashAttackStaminaCost;

                this.ActivateDash(weapon);
            }
        }

        public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // Cooldown timer for the local player
            if (this.dashCooldownTimer > 0f)
            {
                this.dashCooldownTimer -= (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
            }

            // LOGIC FOR THE LOCAL PLAYER'S DASH
            if (this.isDashing)
            {
                if (!Game1.player.UsingTool || this.dashTimer <= 0)
                {
                    this.isDashing = false;
                    this.SyncDashState(false, Vector2.Zero);
                }
                else
                {
                    this.dashTimer--;
                    Vector2 nextPosition = Game1.player.Position + this.dashDirection * 14f;
                    Rectangle nextBoundingBox = Game1.player.GetBoundingBox();
                    nextBoundingBox.X = (int)nextPosition.X;
                    nextBoundingBox.Y = (int)nextPosition.Y;
                    if (!Game1.currentLocation.isCollidingPosition(nextBoundingBox, Game1.viewport, true, 0, false, Game1.player))
                        Game1.player.Position = nextPosition;
                    else
                        this.isDashing = false;
                }

                this.dashParticleTimer -= (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
                if (this.dashParticleTimer <= 0f)
                {
                    this.dashParticleTimer = 0.05f; // Spawn bursts more frequently than sprint particles
                    string particleType = ModEntry.Instance.Config.DashAttackParticleEffect;
                    if (particleType != "None")
                    {
                        // Tell other players to create a particle burst
                        this.Multiplayer.SendMessage(new SprintParticleMessage(Game1.player.UniqueMultiplayerID, particleType), "CreateDashParticleBurst", modIDs: new[] { this.ModManifest.UniqueID });

                        // Pass the local player's dash direction to the creation method
                        for (int i = 0; i < 3; i++)
                        {
                            this.CreateDashParticle(Game1.player, particleType, this.dashDirection);
                        }
                    }
                }
            }

            // LOGIC FOR REMOTE PLAYERS' DASHES
            if (this._activeRemoteDashes.Any())
            {
                var finishedDashes = new List<long>();
                foreach (var dashPair in this._activeRemoteDashes)
                {
                    long farmerId = dashPair.Key;
                    DashState dash = dashPair.Value;
                    dash.Timer--;

                    if (dash.Timer <= 0)
                    {
                        finishedDashes.Add(farmerId);
                    }
                }

                foreach (long id in finishedDashes)
                {
                    this._activeRemoteDashes.Remove(id);
                    Farmer? farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == id);
                    if (farmer != null)
                    {
                        // Ensure the player stops cleanly when the dash timer expires.
                        farmer.xVelocity = 0;
                        farmer.yVelocity = 0;
                    }
                }
            }

            // DAMAGE LOGIC FOR THE LOCAL PLAYER'S DASH
            if (this.isDashing && Game1.player.CurrentTool is MeleeWeapon weapon)
            {
                Rectangle damageArea = Game1.player.GetBoundingBox();
                int inflationAmount = weapon.type.Value switch
                {
                    0 => 64,
                    3 => 64,
                    1 => 32,
                    2 => 128,
                    _ => 64
                };
                damageArea.Inflate(inflationAmount, inflationAmount);

                foreach (Monster monster in Game1.currentLocation.characters.OfType<Monster>().ToList())
                {
                    if (monster.GetBoundingBox().Intersects(damageArea) && !this.monstersHitThisDash.Contains(monster))
                    {
                        int minDamage = weapon.minDamage.Value;
                        int maxDamage = weapon.maxDamage.Value;
                        Game1.currentLocation.damageMonster(monster.GetBoundingBox(), minDamage, maxDamage, false, Game1.player);
                        this.monstersHitThisDash.Add(monster);
                    }
                }
            }
        }

        public bool CheckAndBlockCooldown(ButtonPressedEventArgs e)
        {
            // We only care about the standard attack button.
            if (!e.Button.IsUseToolButton() || !ModEntry.Instance.Config.EnableDashAttackCooldown)
                return false;

            // If the cooldown is active and the player is trying to dash, block it.
            if (this.dashCooldownTimer > 0f && ModEntry.SprintLogic.WasSprintingRecently())
            {
                ModEntry.Instance.LogDebug($"Bruh chill. Dash attack blocked by cooldown. Time remaining: {this.dashCooldownTimer:F2}s.");
                Game1.playSound("cancel");
                return true; // Block this input.
            }

            return false; // Don't block.
        }

        public void ActivateDash(MeleeWeapon weapon)
        {
            if (this.isDashing) return;
            ModEntry.Instance.LogDebug("DASH ATTACK ACTIVATE WOOSH WOOSH");

            this.isDashing = true;
            this.dashTimer = 20; // Duration of the forward movement
            this.dashDirection = ModEntry.GetDirectionVectorFromFacing(Game1.player.FacingDirection);

            // Clear the list of hit monsters at the start of each dash.
            this.monstersHitThisDash.Clear();
            ModEntry.Instance.LogDebug("-> Hit monster list cleared.");

            Game1.playSound("daggerswipe");
            // Send message to other players that our dash has started.
            this.SyncDashState(true, this.dashDirection);

            // Start the cooldown timer.
            if (ModEntry.Instance.Config.EnableDashAttackCooldown)
            {
                // Set the cooldown based on the weapon type.
                this.dashCooldownTimer = weapon.type.Value switch
                {
                    0 => ModEntry.Instance.Config.SwordDashCooldown,       // 0 = Sword
                    3 => ModEntry.Instance.Config.SwordDashCooldown,       // 3 = Defensive Sword
                    1 => ModEntry.Instance.Config.DaggerDashCooldown,      // 1 = Dagger
                    2 => ModEntry.Instance.Config.ClubDashCooldown,        // 2 = Club/Hammer
                    _ => 1.5f                                     // Default fallback
                };
                ModEntry.Instance.LogDebug($"-> Cooldown started: {this.dashCooldownTimer:F2}s for weapon type {weapon.type.Value}.");
            }
        }

        // Handles incoming messages from other players.
        public void HandleRemoteDashState(DashAttackMessage msg)
        {
            Farmer? farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == msg.PlayerID);
            if (farmer is null) return;
            ModEntry.Instance.LogDebug($"Received remote dash state for '{farmer?.Name ?? "Unknown"}'. IsStarting: {msg.IsStarting}.");

            if (msg.IsStarting && farmer is not null)
            {
                // Apply a one-time velocity impulse
                // Tweak dashImpulse float to change dash distance (to test)
                const float dashImpulse = 12f;
                farmer.xVelocity = msg.Direction.X * dashImpulse;
                farmer.yVelocity = msg.Direction.Y * dashImpulse;

                this._activeRemoteDashes[msg.PlayerID] = new DashState(20, msg.Direction);
            }
            else
            {
                // If we receive a message that the dash ended, stop it immediately.
                if (this._activeRemoteDashes.ContainsKey(msg.PlayerID) && farmer is not null)
                {
                    this._activeRemoteDashes.Remove(msg.PlayerID);
                    farmer.xVelocity = 0;
                    farmer.yVelocity = 0;
                }
            }
        }

        // Helper method to send messages.
        private void SyncDashState(bool isStarting, Vector2 direction)
        {
            if (!Context.IsMultiplayer) return;
            ModEntry.Instance.LogDebug($"Sending dash state sync message. IsStarting: {isStarting}.");
            var message = new DashAttackMessage(Game1.player.UniqueMultiplayerID, isStarting, direction);
            this.Multiplayer.SendMessage(message, "DashAttackStateChanged", modIDs: new[] { this.ModManifest.UniqueID });
        }
        private void CreateDashParticle(Farmer who, string particleType, Vector2 dashDirection)
        {
            Vector2 particlePos;

            // Check the config to see which particle style to use
            if (ModEntry.Instance.Config.LaggingDashParticles)
            {
                // Lagging effect: Spawn particles BEHIND the player.
                Vector2 oppositeDirection = -dashDirection; // The direction opposite to the dash
                float lagDistance = 48f; // How many pixels behind the player

                particlePos = who.getStandingPosition() + new Vector2(-32, -48); // Start at player's center
                particlePos += oppositeDirection * lagDistance; // Move backwards
                particlePos += new Vector2(Game1.random.Next(-24, 24), Game1.random.Next(-24, 24)); // Add some random jitter
            }
            else
            {
                // Original centered effect
                particlePos = who.getStandingPosition() + new Vector2(-32, -48);
                particlePos += new Vector2(Game1.random.Next(-32, 32), Game1.random.Next(-32, 32));
            }

            // Reuse static method from SprintLogic
            TemporaryAnimatedSprite? spriteToAdd = SprintLogic.GetSpriteForType(particleType, particlePos);
            if (spriteToAdd != null)
            {
                // Make the particles a bit bigger and fade faster for a more impactful "poof"
                spriteToAdd.scale *= 1.2f;
                spriteToAdd.alphaFade = 0.02f;
                who.currentLocation.temporarySprites.Add(spriteToAdd);
            }
        }

        public void CreateRemoteDashParticleBurst(long playerID, string particleType)
        {
            Farmer? farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == playerID);
            // Try to find the active dash state for this remote player to get their dash direction
            if (farmer != null && !farmer.IsLocalPlayer && this._activeRemoteDashes.TryGetValue(playerID, out var dashState))
            {
                Vector2 direction = dashState.Direction;
                for (int i = 0; i < 3; i++)
                {
                    this.CreateDashParticle(farmer, particleType, direction);
                }
            }
        }
    }

    // This patch handles all damage modifications for both Jump and Dash attacks.
    [HarmonyPatch(typeof(GameLocation), nameof(GameLocation.damageMonster), new Type[] { typeof(Rectangle), typeof(int), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(float), typeof(float), typeof(bool), typeof(Farmer), typeof(bool) })]
    public class GameLocation_DamageMonster_Patch
    {
        public static void Prefix(Farmer who, ref int minDamage, ref int maxDamage)
        {
            try
            {
                if (who != null && who.IsLocalPlayer)
                {
                    if (ModEntry.Instance.Config.EnableJumpAttack && ModEntry.JumpLogic.IsJumping)
                    {
                        ModEntry.Instance.LogDebug($"[Harmony] Applying jump attack damage multiplier ({ModEntry.Instance.Config.JumpAttackDamageMultiplier}x). Original: {minDamage}-{maxDamage}.");
                        minDamage = (int)(minDamage * ModEntry.Instance.Config.JumpAttackDamageMultiplier);
                        maxDamage = (int)(maxDamage * ModEntry.Instance.Config.JumpAttackDamageMultiplier);
                    }

                    if (ModEntry.Instance.Config.EnableDashAttack && ModEntry.CombatLogic.IsPerformingDashAttack)
                    {
                        ModEntry.Instance.LogDebug($"[Harmony] Applying dash attack damage multiplier ({ModEntry.Instance.Config.DashAttackDamageMultiplier}x). Original: {minDamage}-{maxDamage}.");
                        minDamage = (int)(minDamage * ModEntry.Instance.Config.DashAttackDamageMultiplier);
                        maxDamage = (int)(maxDamage * ModEntry.Instance.Config.DashAttackDamageMultiplier);
                    }
                }
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor.Log($"Failed in {nameof(GameLocation_DamageMonster_Patch)}:\n{ex}", LogLevel.Error);
            }
        }
    }

    // This patch handles the activation of the Dash Attack. It seems redundant but if I remove it some weird bugs occur huhu
    [HarmonyPatch(typeof(Tool), nameof(Tool.beginUsing))]
    public class Tool_BeginUsing_Patch
    {
        public static void Prefix(Tool __instance, Farmer who)
        {
            try
            {
                if (__instance is not MeleeWeapon weapon)
                    return;

                // This checks for the "intent" flag set by the high-priority input handler.
                if (ModEntry.Instance.Config.EnableDashAttack && who.IsLocalPlayer && ModEntry.SprintLogic.WasSprintingRecently())
                {
                    if (ModEntry.CombatLogic.IsPerformingDashAttack)
                        return;

                    if (who.stamina < ModEntry.Instance.Config.DashAttackStaminaCost)
                    {
                        Game1.playSound("cancel");
                        return;
                    }
                    who.stamina -= ModEntry.Instance.Config.DashAttackStaminaCost;

                    ModEntry.CombatLogic.ActivateDash(weapon);
                }
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor.Log($"Failed in {nameof(Tool_BeginUsing_Patch)}:\n{ex}", LogLevel.Error);
            }
        }
    }
}