using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using xTile.Dimensions;

namespace MovementOverhaul
{
    public class JumpLogic
    {
        // Helper class to store the state of any jump (local or remote).
        private class JumpArcState
        {
            public float Progress;
            public readonly float Duration;
            public readonly float Height;
            public readonly Vector2 StartPos;
            public readonly Vector2 TargetPos;
            public readonly bool IsHorseJump;
            public int OriginalFrame;

            public JumpArcState(float duration, float height, Vector2 start, Vector2 target, bool isHorse, int originalFrame)
            {
                this.Progress = 0f;
                this.Duration = duration;
                this.Height = height;
                this.StartPos = start;
                this.TargetPos = target;
                this.IsHorseJump = isHorse;
                this.OriginalFrame = originalFrame;
            }
        }

        // A dictionary to track all currently active jumps.
        private readonly Dictionary<long, JumpArcState> _activeJumps = new();

        private readonly IModHelper Helper;
        private readonly IMultiplayerHelper Multiplayer;
        private readonly IManifest ModManifest;

        public bool IsChargingJump { get; private set; } = false;
        private float jumpChargeTimer = 0f;
        private const float MAX_JUMP_CHARGE_SECONDS = 0.75f;

        public JumpLogic(IModHelper helper, IMultiplayerHelper multiplayer, IManifest manifest)
        {
            this.Helper = helper;
            this.Multiplayer = multiplayer;
            this.ModManifest = manifest;
        }

        public bool OnButtonPressed_Instant(ButtonPressedEventArgs e)
        {
            if (!ModEntry.Config.EnableJump || e.Button != ModEntry.Config.JumpKey || !Game1.player.canMove || Game1.eventUp || this._activeJumps.ContainsKey(Game1.player.UniqueMultiplayerID))
                return false;

            Vector2 landingTile = this.CalculateBestLandingTile();
            this.StartJump(Game1.player, landingTile);
            return true;
        }

        public void OnButtonPressed_Charge(object? sender, ButtonPressedEventArgs e)
        {
            if (!ModEntry.Config.EnableJump || e.Button != ModEntry.Config.JumpKey || !Game1.player.canMove || Game1.eventUp || this._activeJumps.ContainsKey(Game1.player.UniqueMultiplayerID))
                return;

            this.IsChargingJump = true;
            this.jumpChargeTimer = 0f;
        }

        public void OnButtonReleased_Jump(object? sender, ButtonReleasedEventArgs e)
        {
            if (!this.IsChargingJump || e.Button != ModEntry.Config.JumpKey)
                return;

            this.IsChargingJump = false;
            Vector2 landingTile = this.CalculateBestLandingTile();
            this.StartJump(Game1.player, landingTile);
        }

        private Vector2 CalculateBestLandingTile()
        {
            Farmer player = Game1.player;
            Character jumper = player.isRidingHorse() ? player.mount : player;
            if (jumper == null) return player.Tile;

            int jumpDistance;
            int maxJumpDistance = 1;

            if (player.movementDirections.Any())
            {
                float baseSpeed = 5f;
                float extraSpeed = player.getMovementSpeed() - baseSpeed;
                float bonusDistance = (extraSpeed > 0) ? extraSpeed * ModEntry.Config.JumpDistanceScaleFactor : 0;
                maxJumpDistance = (int)Math.Round(ModEntry.Config.NormalJumpDistance + bonusDistance);
            }

            if (!ModEntry.Config.InstantJump && ModEntry.Config.ChargeAffectsDistance)
            {
                float chargePercent = Math.Min(1f, this.jumpChargeTimer / MAX_JUMP_CHARGE_SECONDS);
                jumpDistance = (int)Math.Ceiling(1 + (maxJumpDistance - 1) * chargePercent * 1.5f);
            }
            else
            {
                jumpDistance = maxJumpDistance;
            }

            // Enforce a minimum distance for all horse jumps (can't do 2.5 huhu)
            if (player.isRidingHorse())
            {
                jumpDistance = Math.Max(3, jumpDistance);
            }

            Vector2 moveDirection = this.SafeGetDirectionVector(jumper);

            if (ModEntry.Config.HopOverAnything)
            {
                return jumper.Tile + moveDirection * jumpDistance;
            }

            Vector2 bestLandingTile = jumper.Tile;
            for (int i = 1; i <= jumpDistance; i++)
            {
                Vector2 currentTile = jumper.Tile + moveDirection * i;
                if (this.IsSolidWall(currentTile)) break;
                if (this.IsJumpableObjectOnTile(currentTile)) continue;
                if (this.IsTileUnobstructedForLanding(currentTile)) bestLandingTile = currentTile;
                else break;
            }
            return bestLandingTile;
        }

        public void OnUpdateTicking(object? sender, UpdateTickingEventArgs e)
        {
            if (this._activeJumps.Any())
            {
                this.UpdateAllJumps();
            }

            if (this.IsChargingJump)
            {
                this.jumpChargeTimer += (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
                if (!this.Helper.Input.IsDown(ModEntry.Config.JumpKey))
                    this.IsChargingJump = false;
            }
        }

        private void StartJump(Farmer player, Vector2 landingTile)
        {
            if (this._activeJumps.ContainsKey(player.UniqueMultiplayerID)) return;

            bool isHorseJump = player.isRidingHorse() && player.mount != null;

            if (ModEntry.Config.JumpStaminaCost > 0)
            {
                bool freeJump = isHorseJump && ModEntry.Config.NoStaminaDrainOnHorse;
                if (!freeJump)
                {
                    if (player.stamina < ModEntry.Config.JumpStaminaCost) return;
                    player.stamina -= ModEntry.Config.JumpStaminaCost;
                }
            }

            if (isHorseJump)
            {
                ModEntry.IsHorseJumping = true;
            }

            Character jumper = isHorseJump ? player.mount! : player;
            Vector2 startPosition = jumper.Position;
            Vector2 targetPosition = (landingTile == jumper.Tile)
                ? jumper.Position
                : new Vector2(landingTile.X * 64 + 32, landingTile.Y * 64 + 32) - new Vector2(jumper.GetBoundingBox().Width / 2f, jumper.GetBoundingBox().Height / 2f);

            float baseHeight = ModEntry.Config.JumpHeight;
            if (isHorseJump) baseHeight *= 1.7f;
            float speedHeightBonus = player.movementDirections.Any() ? 1.25f : 1.0f;
            float jumpHeight;

            if (ModEntry.Config.InstantJump)
            {
                jumpHeight = baseHeight * speedHeightBonus;
            }
            else
            {
                float chargePercent = Math.Min(1f, this.jumpChargeTimer / MAX_JUMP_CHARGE_SECONDS);
                jumpHeight = baseHeight * (0.5f + (1.2f * chargePercent)) * speedHeightBonus;
            }

            if (!string.IsNullOrEmpty(ModEntry.Config.JumpSound))
            {
                Game1.playSound(ModEntry.Config.JumpSound);
                if (ModEntry.Config.AmplifyJumpSound)
                {
                    Game1.playSound(ModEntry.Config.JumpSound);
                }
            }

            var jumpState = new JumpArcState(ModEntry.Config.JumpDuration, jumpHeight, startPosition, targetPosition, isHorseJump, jumper.Sprite.currentFrame);
            this._activeJumps[player.UniqueMultiplayerID] = jumpState;

            player.canMove = false;
            Game1.playSound(ModEntry.Config.JumpSound);

            var message = new FullJumpSyncMessage(player.UniqueMultiplayerID, startPosition, targetPosition, ModEntry.Config.JumpDuration, jumpHeight, isHorseJump);
            this.Multiplayer.SendMessage(message, "FullJumpSync", modIDs: new[] { this.ModManifest.UniqueID });
        }
        public void StartRemoteJump(FullJumpSyncMessage msg)
        {
            Farmer? farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == msg.PlayerID);
            if (farmer is null) return;

            Character jumper = msg.IsHorseJump && farmer.mount != null ? farmer.mount : farmer;

            var jumpState = new JumpArcState(msg.JumpDuration, msg.JumpHeight, msg.StartPosition, msg.TargetPosition, msg.IsHorseJump, jumper.Sprite.currentFrame);
            this._activeJumps[msg.PlayerID] = jumpState;
            farmer.canMove = false;
        }

        private void UpdateAllJumps()
        {
            var finishedJumps = new List<long>();

            foreach (var jumpPair in this._activeJumps)
            {
                long farmerId = jumpPair.Key;
                JumpArcState jump = jumpPair.Value;

                Farmer? farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == farmerId);
                if (farmer is null)
                {
                    finishedJumps.Add(farmerId);
                    continue;
                }

                jump.Progress += 1f;
                float percentDone = jump.Progress / jump.Duration;

                Vector2 newPosition = Vector2.Lerp(jump.StartPos, jump.TargetPos, percentDone);
                float sinWave = (float)Math.Sin(percentDone * Math.PI);
                int currentArcOffset = (int)(-sinWave * jump.Height);

                if (farmer.IsLocalPlayer && jump.IsHorseJump)
                {
                    // For the local player's horse jump, we don't move the horse directly.
                    // Instead, we feed the data to the Harmony patch, which will move both
                    // the horse and the player to ensure they are perfectly synced.
                    ModEntry.CurrentBounceFactor = ModEntry.Config.HorseJumpPlayerBounce;
                    ModEntry.CurrentHorseJumpPosition = newPosition;
                    ModEntry.CurrentHorseJumpYOffset = currentArcOffset;
                }
                else
                {
                    // For remote players or on-foot jumps, we move the character directly.
                    Character jumper = jump.IsHorseJump && farmer.mount != null ? farmer.mount : farmer;
                    jumper.Position = newPosition;
                    jumper.yJumpOffset = currentArcOffset;

                    if (jumper is Farmer f && !jump.IsHorseJump)
                    {
                        f.Sprite.currentFrame = (jump.Progress < jump.Duration / 2) ? 12 : 11;
                    }
                }

                if (jump.Progress >= jump.Duration)
                {
                    finishedJumps.Add(farmerId);
                    this.EndJump(farmer, jump);
                }
            }

            foreach (long id in finishedJumps)
            {
                this._activeJumps.Remove(id);
            }
        }

        private void EndJump(Farmer farmer, JumpArcState jump)
        {   
            //Fail safe if ever player gets stuck while jumping on horse
            if (farmer.IsLocalPlayer && jump.IsHorseJump)
            {
                ModEntry.IsHorseJumping = false;
                ModEntry.CurrentHorseJumpYOffset = 0;
            }

            Character jumper = jump.IsHorseJump && farmer.mount != null ? farmer.mount! : farmer;

            // For local horse jumps, the patch has the final position. For others, we set it here.
            if (!farmer.IsLocalPlayer || !jump.IsHorseJump)
            {
                jumper.Position = jump.TargetPos;
            }

            jumper.yJumpOffset = 0;
            farmer.yJumpOffset = 0;
            farmer.canMove = true;
            jumper.Sprite.currentFrame = jump.OriginalFrame;
        }

        private Vector2 SafeGetDirectionVector(Character who)
        {
            return who.FacingDirection switch { 0 => new Vector2(0, -1), 1 => new Vector2(1, 0), 2 => new Vector2(0, 1), 3 => new Vector2(-1, 0), _ => Vector2.Zero, };
        }

        private Character? GetCharacterAtTile(GameLocation location, Vector2 tile)
        {
            foreach (Farmer farmer in location.farmers)
            {
                if (farmer.IsLocalPlayer) continue;
                if (farmer.Tile == tile) return farmer;
            }
            foreach (NPC npc in location.characters)
            {
                if (npc.Tile == tile) return npc;
            }
            return null;
        }

        private bool IsJumpableObjectOnTile(Vector2 tile)
        {
            GameLocation location = Game1.currentLocation;
            if (ModEntry.Config.JumpOverTrashCans)
            {
                string? tileAction = location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Action", "Buildings")
                                     ?? location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Action", "Back");

                if (tileAction?.StartsWith("Garbage") == true)
                {
                    return true;
                }
            }
            if (!location.isTilePassable(new Location((int)tile.X, (int)tile.Y), Game1.viewport)) return false;
            if (this.GetCharacterAtTile(location, tile) is NPC) return true;
            if (location.objects.TryGetValue(tile, out StardewValley.Object obj))
            {
                if (obj.CanBeGrabbed) return true;
                if (obj.bigCraftable.Value) return true;
                if (obj.Fragility == 0) return true;
                if (obj is Fence) return true;
            }
            if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature feature))
            {
                if (feature is Bush || feature.GetType().Name == "Weed") return true;
                if (feature is Tree tree && tree.growthStage.Value < 5) return true;
                if (feature is FruitTree fTree && fTree.growthStage.Value < 4) return true;
            }
            foreach (ResourceClump rc in location.resourceClumps)
            {
                if (rc.getBoundingBox().Contains(tile.X * 64 + 32, tile.Y * 64 + 32))
                {
                    if (ModEntry.Config.JumpOverBoulders && (rc.parentSheetIndex.Value == 672)) return true;
                    if (ModEntry.Config.JumpOverLargeStumps && (rc.parentSheetIndex.Value == 600)) return true;
                    if (ModEntry.Config.JumpOverLargeLogs && (rc.parentSheetIndex.Value == 602)) return true;
                }
            }
            return false;
        }

        private bool IsSolidWall(Vector2 tile)
        {
            GameLocation location = Game1.currentLocation;
            if (!location.isTileOnMap(tile)) return true;

            if (this.IsJumpableObjectOnTile(tile))
                return false;

            if (location.getTileIndexAt((int)tile.X, (int)tile.Y, "Buildings") != -1)
            {
                if (location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Passable", "Buildings") != null)
                {
                    return false;
                }
                return true;
            }
            foreach (var building in location.buildings)
            {
                if (building.occupiesTile(tile)) return true;
            }
            if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature feature))
            {
                if (feature is Tree tree && tree.growthStage.Value >= 5) return true;
                if (feature is FruitTree fTree && fTree.growthStage.Value >= 4) return true;
            }
            return false;
        }

        private bool IsTileUnobstructedForLanding(Vector2 tile)
        {
            GameLocation location = Game1.currentLocation;

            if (this.IsSolidWall(tile) || location.isWaterTile((int)tile.X, (int)tile.Y))
                return false;

            if (this.IsJumpableObjectOnTile(tile))
                return false;

            if (location.Objects.ContainsKey(tile))
                return false;

            if (this.GetCharacterAtTile(location, tile) != null)
                return false;

            if (location.largeTerrainFeatures.Any(tf => tf.getBoundingBox().Intersects(new Microsoft.Xna.Framework.Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64))))
                return false;

            if (!this.IsJumpableObjectOnTile(tile) && location.resourceClumps.Any(rc => rc.getBoundingBox().Intersects(new Microsoft.Xna.Framework.Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64))))
            {
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Horse), nameof(Horse.update))]
    public class Horse_Update_Postfix_Patch
    {
        public static void Postfix(Horse __instance)
        {
            if (ModEntry.IsHorseJumping && __instance == Game1.player.mount)
            {
                __instance.Position = ModEntry.CurrentHorseJumpPosition;
                __instance.yJumpOffset = ModEntry.CurrentHorseJumpYOffset;

                Game1.player.Position = __instance.Position;
                Game1.player.yJumpOffset = (int)(__instance.yJumpOffset * ModEntry.CurrentBounceFactor);
            }
        }
    }
}