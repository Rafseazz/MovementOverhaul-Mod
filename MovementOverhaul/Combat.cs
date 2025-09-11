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
            // We check for the standard attack button (left-click).
            if (!e.Button.IsUseToolButton() || !Context.CanPlayerMove || Game1.player.CurrentTool is not MeleeWeapon weapon)
                return;

            // This is called instantly on button press, reliably catching the sprint state.
            if (ModEntry.Config.EnableDashAttack && ModEntry.SprintLogic.WasSprintingRecently() && !this.isDashing)
            {
                if (Game1.player.stamina < ModEntry.Config.DashAttackStaminaCost)
                {
                    Game1.playSound("cancel");
                    return;
                }
                Game1.player.stamina -= ModEntry.Config.DashAttackStaminaCost;

                this.ActivateDash(weapon);
            }
        }

        public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            if (this.dashCooldownTimer > 0f)
            {
                this.dashCooldownTimer -= (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
            }

            if (this.isDashing)
            {
                // Stop the dash if the player is no longer swinging
                if (!Game1.player.UsingTool)
                    this.isDashing = false;

                this.dashTimer--;

                Vector2 nextPosition = Game1.player.Position + this.dashDirection * 18f;
                Rectangle nextBoundingBox = Game1.player.GetBoundingBox();
                nextBoundingBox.X = (int)nextPosition.X;
                nextBoundingBox.Y = (int)nextPosition.Y;

                if (!Game1.currentLocation.isCollidingPosition(nextBoundingBox, Game1.viewport, true, 0, false, Game1.player))
                    Game1.player.Position = nextPosition;
                else
                    this.isDashing = false;

                if (this.dashTimer <= 0)
                    this.isDashing = false;

                // If the local dash just ended, send a stop message.
                if (!this.isDashing)
                {
                    this.SyncDashState(false, Vector2.Zero);
                }

                // Remote player dash logic
                if (this._activeRemoteDashes.Any())
                {
                    var finishedDashes = new List<long>();
                    foreach (var dashPair in this._activeRemoteDashes)
                    {
                        long farmerId = dashPair.Key;
                        DashState dash = dashPair.Value;

                        Farmer? farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == farmerId);
                        if (farmer is null)
                        {
                            finishedDashes.Add(farmerId);
                            continue;
                        }

                        dash.Timer--;

                        Vector2 nextPositionRemote = farmer.Position + dash.Direction * 18f;
                        farmer.Position = nextPositionRemote;

                        if (dash.Timer <= 0 || !farmer.UsingTool)
                        {
                            finishedDashes.Add(farmerId);
                        }
                    }
                    foreach (long id in finishedDashes)
                    {
                        this._activeRemoteDashes.Remove(id);
                    }
                }
            }

            // Area-of-effect damage logic.
            if (this.isDashing && Game1.player.CurrentTool is MeleeWeapon weapon)
            {
                Rectangle damageArea = Game1.player.GetBoundingBox();

                // Set the damage area based on the weapon type.
                int inflationAmount = weapon.type.Value switch
                {
                    0 => 64, // 0 = Sword
                    3 => 64, // 3 = Defensive Sword
                    1 => 32, // 1 = Dagger (smaller)
                    2 => 128, // 2 = Club/Hammer (larger)
                    _ => 64 // Default fallback
                };

                damageArea.Inflate(inflationAmount, inflationAmount); // A 3x3 tile area around the player.

                foreach (Monster monster in Game1.currentLocation.characters.OfType<Monster>().ToList())
                {
                    if (monster != null && monster.GetBoundingBox().Intersects(damageArea) && !this.monstersHitThisDash.Contains(monster))
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
            if (!e.Button.IsUseToolButton() || !ModEntry.Config.EnableDashAttackCooldown)
                return false;

            // If the cooldown is active and the player is trying to dash, block it.
            if (this.dashCooldownTimer > 0f && ModEntry.SprintLogic.WasSprintingRecently())
            {
                Game1.playSound("cancel");
                return true; // Block this input.
            }

            return false; // Don't block.
        }

        public void ActivateDash(MeleeWeapon weapon)
        {
            if (this.isDashing) return;
            this.isDashing = true;
            this.dashTimer = 10; // Duration of the forward movement
            this.dashDirection = ModEntry.GetDirectionVectorFromFacing(Game1.player.FacingDirection);

            // Clear the list of hit monsters at the start of each dash.
            this.monstersHitThisDash.Clear();

            Game1.playSound("daggerswipe");
            // Send message to other players that our dash has started.
            this.SyncDashState(true, this.dashDirection);

            // Start the cooldown timer.
            if (ModEntry.Config.EnableDashAttackCooldown)
            {
                // Set the cooldown based on the weapon type.
                this.dashCooldownTimer = weapon.type.Value switch
                {
                    0 => ModEntry.Config.SwordDashCooldown,       // 0 = Sword
                    3 => ModEntry.Config.SwordDashCooldown,       // 3 = Defensive Sword
                    1 => ModEntry.Config.DaggerDashCooldown,      // 1 = Dagger
                    2 => ModEntry.Config.ClubDashCooldown,        // 2 = Club/Hammer
                    _ => 1.5f                                     // Default fallback
                };
            }
        }

        // Handles incoming messages from other players.
        public void HandleRemoteDashState(DashAttackMessage msg)
        {
            if (msg.IsStarting)
            {
                this._activeRemoteDashes[msg.PlayerID] = new DashState(10, msg.Direction);
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
                    if (ModEntry.Config.EnableJumpAttack && ModEntry.JumpLogic.IsJumping)
                    {
                        minDamage = (int)(minDamage * ModEntry.Config.JumpAttackDamageMultiplier);
                        maxDamage = (int)(maxDamage * ModEntry.Config.JumpAttackDamageMultiplier);
                    }

                    if (ModEntry.Config.EnableDashAttack && ModEntry.CombatLogic.IsPerformingDashAttack)
                    {
                        minDamage = (int)(minDamage * ModEntry.Config.DashAttackDamageMultiplier);
                        maxDamage = (int)(maxDamage * ModEntry.Config.DashAttackDamageMultiplier);
                    }
                }
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor.Log($"Failed in {nameof(GameLocation_DamageMonster_Patch)}:\n{ex}", LogLevel.Error);
            }
        }
    }

    // This patch handles the activation of the Dash Attack.
    [HarmonyPatch(typeof(Tool), nameof(Tool.beginUsing))]
    public class Tool_BeginUsing_Patch
    {
        public static void Prefix(Tool __instance, Farmer who)
        {
            try
            {
                if (__instance is not MeleeWeapon weapon)
                    return;

                // This now checks for the "intent" flag set by the high-priority input handler.
                if (ModEntry.Config.EnableDashAttack && who.IsLocalPlayer && ModEntry.SprintLogic.WasSprintingRecently())
                {
                    if (ModEntry.CombatLogic.IsPerformingDashAttack)
                        return;

                    if (who.stamina < ModEntry.Config.DashAttackStaminaCost)
                    {
                        Game1.playSound("cancel");
                        return;
                    }
                    who.stamina -= ModEntry.Config.DashAttackStaminaCost;

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