using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buffs;
using StardewValley.Characters;
using StardewValley.Objects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MovementOverhaul
{
    public class SitLogic
    {
        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private readonly IMultiplayerHelper Multiplayer;
        private readonly IManifest ModManifest;

        private bool isSittingOnGround = false;
        private bool wasSittingInChairLastTick = false;
        private float sitRegenDelayTimer = 0f;
        private float regenTickTimer = 0f;

        public bool IsSittingOnGround => this.isSittingOnGround;

        private int sittingFrame = -1;
        private bool sittingIsFlipped = false;

        private float socialTimer = 15f;
        private float fireCheckTimer = 5f;
        private float meditateTimer = 60f;
        private float particleIdleTimer = 10f;

        public SitLogic(IModHelper helper, IMonitor monitor, IMultiplayerHelper multiplayer, IManifest manifest)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.Multiplayer = multiplayer;
            this.ModManifest = manifest;
        }

        public void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (Game1.player.isRidingHorse())
                return;

            //ModEntry.Instance.LogDebug($"Button pressed: {e.Button}. Is sitting on ground: {this.isSittingOnGround}.");

            if (this.isSittingOnGround && e.Button.IsActionButton())
            {
                ModEntry.Instance.LogDebug("Action button pressed while sitting. Standing up.");
                this.StopSittingOnGround();
            }

            if (!ModEntry.Instance.Config.EnableSit || !Context.CanPlayerMove || ModEntry.JumpLogic.IsChargingJump)
                return;

            if (e.Button == ModEntry.Instance.Config.SitKey)
            {
                ModEntry.Instance.LogDebug("Sit key pressed.");
                if (this.isSittingOnGround)
                    this.StopSittingOnGround();
                else if (Context.CanPlayerMove)
                    this.StartSittingOnGround();
            }
        }

        public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!ModEntry.Instance.Config.EnableSit)
            {
                if (this.isSittingOnGround) this.StopSittingOnGround();
                return;
            }

            if (!Context.IsWorldReady) return;

            bool isSittingInChairThisTick = Game1.player.isSitting.Value;
            if (isSittingInChairThisTick)
            {
                float elapsedSeconds = (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
                if (!this.wasSittingInChairLastTick)
                {
                    ModEntry.Instance.LogDebug("Player just sat in a chair. Starting regen delay timer.");
                    this.sitRegenDelayTimer = ModEntry.Instance.Config.SitRegenDelaySeconds;
                }

                if (this.sitRegenDelayTimer > 0)
                {
                    this.sitRegenDelayTimer -= elapsedSeconds;
                }
                else
                {
                    this.regenTickTimer -= elapsedSeconds;
                    if (this.regenTickTimer <= 0)
                    {
                        this.regenTickTimer = 1f;
                        float regenAmount = ModEntry.Instance.Config.SitChairRegenPerSecond;
                        ModEntry.Instance.LogDebug($"Regenerating {regenAmount} stamina from sitting in chair. Current: {Game1.player.stamina}");
                        Game1.player.stamina = Math.Min(Game1.player.MaxStamina, Game1.player.stamina + regenAmount);
                    }
                }
                this.wasSittingInChairLastTick = true;
                return;
            }

            if (this.isSittingOnGround)
            {
                if (Game1.player.movementDirections.Any())
                {
                    ModEntry.Instance.LogDebug("Player moved while sitting. Standing up.");
                    this.StopSittingOnGround();
                    return;
                }

                Game1.player.canMove = false;
                Game1.player.completelyStopAnimatingOrDoingAction();
                Game1.player.showFrame(this.sittingFrame, this.sittingIsFlipped);

                float elapsedSeconds = (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
                if (this.sitRegenDelayTimer > 0)
                {
                    this.sitRegenDelayTimer -= elapsedSeconds;
                }
                else
                {
                    this.regenTickTimer -= elapsedSeconds;
                    if (this.regenTickTimer <= 0)
                    {
                        this.regenTickTimer = 1f;
                        float regenAmount = ModEntry.Instance.Config.SitGroundRegenPerSecond;
                        ModEntry.Instance.LogDebug($"Regenerating {regenAmount} stamina from sitting on ground. Current: {Game1.player.stamina}");
                        Game1.player.stamina = Math.Min(Game1.player.MaxStamina, Game1.player.stamina + regenAmount);
                    }
                }

                this.HandleSocialSitting(elapsedSeconds);
                this.HandleFireBuff(elapsedSeconds);
                this.HandleMeditateBuff(elapsedSeconds);
                this.HandleIdleEffects(elapsedSeconds);
            }

            this.wasSittingInChairLastTick = isSittingInChairThisTick;
        }

        private void StartSittingOnGround()
        {
            if (this.IsSittingOnGround) return;
            ModEntry.Instance.LogDebug("NOW GROUND SITTING! YEAH DIRTY PANTS!");

            this.isSittingOnGround = true;
            Game1.player.canMove = false;
            Game1.player.completelyStopAnimatingOrDoingAction();

            switch (Game1.player.FacingDirection)
            {
                case 0:
                    this.sittingFrame = 62;
                    this.sittingIsFlipped = false;
                    break;
                case 1:
                    this.sittingFrame = 58;
                    this.sittingIsFlipped = false;
                    break;
                case 3:
                    this.sittingFrame = 58;
                    this.sittingIsFlipped = true;
                    break;
                case 2:
                default:
                    this.sittingFrame = 29;
                    this.sittingIsFlipped = false;
                    break;
            }
            ModEntry.Instance.LogDebug($"Sit frame set to {this.sittingFrame} based on direction {Game1.player.FacingDirection}.");

            Game1.playSound("pickUpItem");

            this.SyncSitState();

            this.sitRegenDelayTimer = ModEntry.Instance.Config.SitRegenDelaySeconds;
            this.regenTickTimer = 1f;
            this.socialTimer = 15f;
            this.fireCheckTimer = 5f;
            this.meditateTimer = 20f;
            this.particleIdleTimer = 10f;
        }

        private void StopSittingOnGround()
        {
            if (!this.isSittingOnGround) return;
            ModEntry.Instance.LogDebug("STOOD UP FROM SITTING ON THE GROUND NAUR");

            this.isSittingOnGround = false;
            this.sittingFrame = -1;
            this.sittingIsFlipped = false;
            Game1.player.yJumpOffset = 0;
            Game1.player.flip = false;
            Game1.player.canMove = true;
            Game1.player.FarmerSprite.StopAnimation();

            switch (Game1.player.FacingDirection)
            {
                case 0:
                    Game1.player.flip = false;
                    Game1.player.FarmerSprite.setCurrentFrame(12);
                    break;
                case 1:
                    Game1.player.flip = false;
                    Game1.player.FarmerSprite.setCurrentFrame(6);
                    break;
                case 2:
                    Game1.player.flip = false;
                    Game1.player.FarmerSprite.setCurrentFrame(0);
                    break;
                case 3:
                    Game1.player.flip = true;
                    Game1.player.FarmerSprite.setCurrentFrame(6);
                    break;
            }

            this.SyncSitState(isStandingUp: true);
        }

        private void SyncSitState(bool isStandingUp = false)
        {
            if (!Context.IsMultiplayer) return;
            ModEntry.Instance.LogDebug($"Sending multiplayer sit state sync message. Standing up: {isStandingUp}.");

            SitStateMessage message;
            if (isStandingUp)
            {
                message = new SitStateMessage(Game1.player.UniqueMultiplayerID, false, 0, 0, false, 0);
            }
            else
            {
                message = new SitStateMessage(
                    id: Game1.player.UniqueMultiplayerID,
                    isSitting: true,
                    frame: this.sittingFrame,
                    direction: Game1.player.FacingDirection,
                    isFlipped: this.sittingIsFlipped,
                    yOffset: 0
                );
            }

            this.Multiplayer.SendMessage(message, "SitStateChanged", modIDs: new[] { this.ModManifest.UniqueID });
        }

        private void HandleSocialSitting(float elapsedSeconds)
        {
            if (!ModEntry.Instance.Config.SocialSittingFriendship) return;

            this.socialTimer -= elapsedSeconds;
            if (this.socialTimer <= 0)
            {
                this.socialTimer = 15f;
                ModEntry.Instance.LogDebug("Social timer expired. Checking for nearby NPCs and pets.");

                foreach (var character in Game1.currentLocation.characters)
                {
                    if (Vector2.Distance(Game1.player.Tile, character.Tile) <= 2 && character is NPC npc && !npc.IsMonster && Game1.player.friendshipData.ContainsKey(npc.Name))
                    {
                        ModEntry.Instance.LogDebug($"-> Found nearby NPC: {npc.Name}. Granting 5 friendship points.");
                        Game1.player.changeFriendship(5, npc);
                        npc.doEmote(32);
                        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("hud.social-sitting.npc", new { npcName = npc.displayName }), 4));
                    }
                }

                Pet? pet = Game1.player.getPet();
                if (pet != null && pet.currentLocation == Game1.currentLocation && Vector2.Distance(Game1.player.Tile, pet.Tile) <= 2)
                {
                    ModEntry.Instance.LogDebug($"-> Found nearby pet: {pet.displayName}. Granting 6 friendship points.");
                    pet.friendshipTowardFarmer.Value = Math.Min(1000, pet.friendshipTowardFarmer.Value + 6);
                    pet.doEmote(20);
                    Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("hud.social-sitting.pet", new { petName = pet.displayName }), 4));
                }
            }
        }

        private void HandleFireBuff(float elapsedSeconds)
        {
            if (!ModEntry.Instance.Config.FireSittingBuff) return;
            string buffId = $"{this.ModManifest.UniqueID}.WarmedBuff";

            this.fireCheckTimer -= elapsedSeconds;
            if (this.fireCheckTimer <= 0)
            {
                this.fireCheckTimer = 5f;
                ModEntry.Instance.LogDebug("Checking for nearby fire sources for 'Warmed' buff.");

                bool nearFire = false;
                foreach (var furniture in Game1.currentLocation.furniture)
                {
                    if ((furniture.ParentSheetIndex == 500 || furniture.ParentSheetIndex == 524) && furniture.IsOn)
                        if (Vector2.Distance(Game1.player.Tile, furniture.TileLocation) <= 3)
                            nearFire = true;
                }
                foreach (var obj in Game1.currentLocation.objects.Values)
                {
                    if (obj.ParentSheetIndex == 146 && obj.IsOn)
                        if (Vector2.Distance(Game1.player.Tile, obj.TileLocation) <= 3)
                            nearFire = true;
                }

                if (nearFire && !Game1.player.hasBuff(buffId))
                {
                    ModEntry.Instance.LogDebug("Near a fire source and doesn't have buff. Applying 'Warmed' buff.");
                    Buff warmedBuff = new Buff(
                        id: buffId,
                        displayName: this.Helper.Translation.Get("buff.warmed.name"),
                        description: this.Helper.Translation.Get("buff.warmed.description"),
                        duration: 3 * 60 * 1000,
                        effects: new BuffEffects()
                        {
                            MaxStamina = { Value = 20 },
                            Speed = { Value = 1 }
                        }
                    );
                    Game1.player.buffs.Apply(warmedBuff);
                    Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("hud.warmed-buff"), 4));
                }
            }
        }

        private void HandleMeditateBuff(float elapsedSeconds)
        {
            if (!ModEntry.Instance.Config.MeditateForBuff) return;
            string buffId = $"{this.ModManifest.UniqueID}.FocusedBuff";

            this.meditateTimer -= elapsedSeconds;
            if (this.meditateTimer <= 0)
            {
                this.meditateTimer = 9999;
                if (!Game1.player.hasBuff(buffId))
                {
                    ModEntry.Instance.LogDebug("Meditation timer complete. Applying 'Focused' buff.");
                    Buff focusedBuff = new Buff(
                        id: buffId,
                        displayName: this.Helper.Translation.Get("buff.focused.name"),
                        description: this.Helper.Translation.Get("buff.focused.description"),
                        duration: 6 * 60 * 1000,
                        effects: new BuffEffects() { LuckLevel = { Value = 2 } }
                    );
                    Game1.player.buffs.Apply(focusedBuff);
                    Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("hud.meditate-buff"), 4));
                }
            }
        }

        private void HandleIdleEffects(float elapsedSeconds)
        {
            if (!ModEntry.Instance.Config.IdleSitEffects) return;

            this.particleIdleTimer -= elapsedSeconds;
            if (this.particleIdleTimer <= 0)
            {
                this.particleIdleTimer = Game1.random.Next(5, 10);
                ModEntry.Instance.LogDebug($"Idle effects timer expired. Resetting for {this.particleIdleTimer}s.");


                int emote = Game1.random.Next(3) switch
                {
                    0 => 20,
                    1 => 32,
                    _ => 56, 
                };
                ModEntry.Instance.LogDebug($"-> Triggering random emote: {emote}.");
                Game1.player.doEmote(emote);
            }
        }
    }
}