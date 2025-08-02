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

        private struct Pose
        {
            public int Frame;
            public int Direction;
            public bool Flip;
            public int YOffset;

            public Pose(int frame, int direction = -1, bool flip = false, int yOffset = 0)
            {
                this.Frame = frame;
                this.Direction = direction;
                this.Flip = flip;
                this.YOffset = yOffset;
            }
        }

        private readonly List<Pose> _poses = new List<Pose>
        {
            new Pose(29, 2),
            new Pose(4, 2),
            new Pose(5, 2),
            new Pose(54, 2),
            new Pose(55, 2),
            new Pose(62, 0),
            new Pose(70, 2),
            new Pose(69, 2, false, 16)
        };

        private int currentPoseIndex = -1;
        private int bounceAnimationTimer = 0;

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
            if (!ModEntry.Config.EnableSit || !Context.CanPlayerMove || ModEntry.JumpLogic.IsChargingJump)
                return;

            if (e.Button == ModEntry.Config.SitKey)
            {
                if (!this.isSittingOnGround)
                {
                    this.StartSittingOnGround();
                }
                else
                {
                    this.CycleToNextPose();
                }
            }
        }

        public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!ModEntry.Config.EnableSit)
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
                    this.sitRegenDelayTimer = ModEntry.Config.SitRegenDelaySeconds;
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
                        float regenAmount = ModEntry.Config.SitChairRegenPerSecond;
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
                    this.StopSittingOnGround();
                    return;
                }

                Game1.player.completelyStopAnimatingOrDoingAction();
                Game1.player.canMove = false;

                if (this.currentPoseIndex > -1 && this.currentPoseIndex < this._poses.Count)
                {
                    Pose currentPose = this._poses[this.currentPoseIndex];
                    Game1.player.FarmerSprite.setCurrentFrame(currentPose.Frame);

                    int bounceOffset = 0;
                    if (this.bounceAnimationTimer > 0)
                    {
                        this.bounceAnimationTimer--;
                        bounceOffset = this.bounceAnimationTimer > 3 ? -4 : 0;
                    }
                    Game1.player.yJumpOffset = currentPose.YOffset + bounceOffset;
                }

                if (this.bounceAnimationTimer > 0) return;

                if (Game1.player.movementDirections.Any())
                {
                    this.StopSittingOnGround();
                    return;
                }

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
                        float regenAmount = ModEntry.Config.SitGroundRegenPerSecond;
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
            this.isSittingOnGround = true;
            Game1.player.canMove = false;
            Game1.player.completelyStopAnimatingOrDoingAction();

            this.currentPoseIndex = -1;
            this.CycleToNextPose();

            this.sitRegenDelayTimer = ModEntry.Config.SitRegenDelaySeconds;
            this.regenTickTimer = 1f;
            this.socialTimer = 15f;
            this.fireCheckTimer = 5f;
            this.meditateTimer = 20f;
            this.particleIdleTimer = 10f;
        }

        private void CycleToNextPose()
        {
            this.currentPoseIndex++;
            if (this.currentPoseIndex >= this._poses.Count)
            {
                this.currentPoseIndex = 0;
            }

            Pose nextPose = this._poses[this.currentPoseIndex];

            Game1.player.flip = nextPose.Flip;

            if (nextPose.Direction != -1)
            {
                Game1.player.faceDirection(nextPose.Direction);
            }

            Game1.player.FarmerSprite.setCurrentFrame(nextPose.Frame);
            Game1.playSound("dwoop");
            this.bounceAnimationTimer = 6;

            this.SyncSitState();

            this.meditateTimer = 20f;
            this.particleIdleTimer = 10f;
        }

        private void StopSittingOnGround()
        {
            this.isSittingOnGround = false;
            this.currentPoseIndex = -1;
            this.bounceAnimationTimer = 0;
            Game1.player.yJumpOffset = 0;
            Game1.player.flip = false;
            Game1.player.canMove = true;
            Game1.player.FarmerSprite.StopAnimation();

            this.SyncSitState(isStandingUp: true);
        }

        private void SyncSitState(bool isStandingUp = false)
        {
            if (!Context.IsMultiplayer) return;

            SitStateMessage message;
            if (isStandingUp)
            {
                message = new SitStateMessage(Game1.player.UniqueMultiplayerID, false, 0, 0, false, 0);
            }
            else
            {
                Pose currentPose = this._poses[this.currentPoseIndex];
                message = new SitStateMessage(
                    id: Game1.player.UniqueMultiplayerID,
                    isSitting: true,
                    frame: currentPose.Frame,
                    direction: Game1.player.FacingDirection,
                    isFlipped: Game1.player.flip,
                    yOffset: currentPose.YOffset
                );
            }

            this.Multiplayer.SendMessage(message, "SitStateChanged", modIDs: new[] { this.ModManifest.UniqueID });
        }

        private void HandleSocialSitting(float elapsedSeconds)
        {
            if (!ModEntry.Config.SocialSittingFriendship) return;

            this.socialTimer -= elapsedSeconds;
            if (this.socialTimer <= 0)
            {
                this.socialTimer = 15f;

                foreach (var character in Game1.currentLocation.characters)
                {
                    if (Vector2.Distance(Game1.player.Tile, character.Tile) <= 2 && character is NPC npc && !npc.IsMonster && Game1.player.friendshipData.ContainsKey(npc.Name))
                    {
                        Game1.player.changeFriendship(5, npc);
                        npc.doEmote(32);
                        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("hud.social-sitting.npc", new { npcName = npc.displayName }), 4));
                    }
                }

                Pet? pet = Game1.player.getPet();
                if (pet != null && pet.currentLocation == Game1.currentLocation && Vector2.Distance(Game1.player.Tile, pet.Tile) <= 2)
                {
                    pet.friendshipTowardFarmer.Value = Math.Min(1000, pet.friendshipTowardFarmer.Value + 6);
                    pet.doEmote(20);
                    Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("hud.social-sitting.pet", new { petName = pet.displayName }), 4));
                }
            }
        }

        private void HandleFireBuff(float elapsedSeconds)
        {
            if (!ModEntry.Config.FireSittingBuff) return;
            string buffId = $"{this.ModManifest.UniqueID}.WarmedBuff";

            this.fireCheckTimer -= elapsedSeconds;
            if (this.fireCheckTimer <= 0)
            {
                this.fireCheckTimer = 5f;

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
            if (!ModEntry.Config.MeditateForBuff) return;
            string buffId = $"{this.ModManifest.UniqueID}.FocusedBuff";

            this.meditateTimer -= elapsedSeconds;
            if (this.meditateTimer <= 0)
            {
                this.meditateTimer = 9999;
                if (!Game1.player.hasBuff(buffId))
                {
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
            if (!ModEntry.Config.IdleSitEffects) return;

            this.particleIdleTimer -= elapsedSeconds;
            if (this.particleIdleTimer <= 0)
            {
                this.particleIdleTimer = Game1.random.Next(5, 10);

                int emote = Game1.random.Next(3) switch
                {
                    0 => 20,
                    1 => 32,
                    _ => 56, 
                };
                Game1.player.doEmote(emote);

                //Game1.currentLocation.temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(346, 1971, 12, 11), 200f, 5, 0, Game1.player.getStandingPosition() + new Vector2(-16, -112), false, false, -1, 0, Color.White, 4f, 0, 0, 0));
            }
        }
    }
}