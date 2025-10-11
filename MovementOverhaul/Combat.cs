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
        private class DashState
        {
            public int Timer;
            public readonly int Duration;
            public readonly Vector2 StartPosition;
            public readonly Vector2 TargetPosition;
            public DashState(int duration, Vector2 direction, Vector2 startPosition)
            {
                this.Timer = duration;
                this.Duration = duration;
                this.StartPosition = startPosition;
                // Pre-calculate the final destination of the dash
                this.TargetPosition = startPosition + direction * 18f * duration;
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
                    Vector2 nextPosition = Game1.player.Position + this.dashDirection * 18f;
                    Rectangle nextBoundingBox = Game1.player.GetBoundingBox();
                    nextBoundingBox.X = (int)nextPosition.X;
                    nextBoundingBox.Y = (int)nextPosition.Y;
                    if (!Game1.currentLocation.isCollidingPosition(nextBoundingBox, Game1.viewport, true, 0, false, Game1.player))
                        Game1.player.Position = nextPosition;
                    else
                        this.isDashing = false;
                }
            }

            // SMOOTHING LOGIC FOR REMOTE PLAYERS' DASHES
            if (this._activeRemoteDashes.Any())
            {
                var finishedDashes = new List<long>();
                foreach (var dashPair in this._activeRemoteDashes)
                {
                    long farmerId = dashPair.Key;
                    DashState dash = dashPair.Value;
                    Farmer? farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == farmerId);

                    if (farmer is null || dash.Timer <= 0 || !farmer.UsingTool)
                    {
                        finishedDashes.Add(farmerId);
                        continue;
                    }

                    dash.Timer--;

                    // Calculate how far along the dash arc the player should be (a value from 0.0 to 1.0)
                    float progress = 1f - ((float)dash.Timer / dash.Duration);

                    // Determine the ideal, exact position for this point in the animation
                    Vector2 idealPosition = Vector2.Lerp(dash.StartPosition, dash.TargetPosition, progress);

                    // Instead of teleporting, smoothly move the farmer's current position
                    // towards the ideal position. This works with the game's netcode instead of fighting it.
                    farmer.Position = Vector2.Lerp(farmer.Position, idealPosition, 0.4f);
                }

                foreach (long id in finishedDashes)
                {
                    this._activeRemoteDashes.Remove(id);
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
            this.dashTimer = 10; // Duration of the forward movement
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
                // Create the new DashState, capturing the remote player's current position as the start point.
                this._activeRemoteDashes[msg.PlayerID] = new DashState(10, msg.Direction, farmer.Position);
            }
            else
            {
                if (this._activeRemoteDashes.ContainsKey(msg.PlayerID))
                    this._activeRemoteDashes.Remove(msg.PlayerID);
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