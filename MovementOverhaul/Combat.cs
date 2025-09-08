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

        public bool IsPerformingDashAttack => this.isDashing;

        public CombatLogic(IModHelper helper, IMonitor monitor) { }

        // This is the new high-priority input handler.
        public void HandleDashAttackInput(ButtonPressedEventArgs e)
        {
            // We check for the standard attack button (left-click).
            if (!e.Button.IsUseToolButton() || !Context.CanPlayerMove || Game1.player.CurrentTool is not MeleeWeapon)
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

                this.ActivateDash();
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
                Game1.player.doEmote(36); // 'X' emote
                Game1.playSound("cancel");
                return true; // Block this input.
            }

            return false; // Don't block.
        }

        public void ActivateDash()
        {
            if (this.isDashing) return;
            this.isDashing = true;
            this.dashTimer = 10; // Duration of the forward movement
            this.dashDirection = ModEntry.GetDirectionVectorFromFacing(Game1.player.FacingDirection);

            Game1.playSound("daggerswipe");

            // Start the cooldown timer.
            if (ModEntry.Config.EnableDashAttackCooldown)
            {
                this.dashCooldownTimer = ModEntry.Config.DashAttackCooldownSeconds;
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
                if (__instance is not MeleeWeapon)
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

                    ModEntry.CombatLogic.ActivateDash();
                }
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor.Log($"Failed in {nameof(Tool_BeginUsing_Patch)}:\n{ex}", LogLevel.Error);
            }
        }
    }
}