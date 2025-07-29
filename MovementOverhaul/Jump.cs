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
        private JumpState currentState = JumpState.Idle;
        private Vector2 startPosition;
        private Vector2 targetPosition;
        private float jumpProgress;
        private float jumpHeight;
        private int originalFrame;
        private readonly ModConfig Config;
        private readonly IModHelper Helper;
        private readonly IMultiplayerHelper Multiplayer;
        private readonly IManifest ModManifest;
        private bool isHorseJump = false;
        private bool isChargingJump = false;
        private float jumpChargeTimer = 0f;
        private const float MAX_JUMP_CHARGE_SECONDS = 0.75f;

        public JumpLogic(ModConfig config, IModHelper helper, IMultiplayerHelper multiplayer, IManifest manifest)
        {
            this.Config = config;
            this.Helper = helper;
            this.Multiplayer = multiplayer;
            this.ModManifest = manifest;
        }

        // --- METHOD FOR INSTANT JUMP MODE ---
        public bool OnButtonPressed_Instant(ButtonPressedEventArgs e)
        {
            if (!this.Config.EnableJump || e.Button != this.Config.JumpKey || !Context.CanPlayerMove || this.currentState != JumpState.Idle)
                return false;

            // MODIFIED: We no longer calculate the landing spot here. We just start the jump.
            this.StartJump(Game1.player);
            return true; // A jump has been successfully started.
        }

        // --- METHODS FOR HOLD-AND-RELEASE MODE ---
        public void OnButtonPressed_Charge(object? sender, ButtonPressedEventArgs e)
        {
            if (!this.Config.EnableJump || e.Button != this.Config.JumpKey || !Context.CanPlayerMove || this.currentState != JumpState.Idle)
                return;

            this.isChargingJump = true;
            this.jumpChargeTimer = 0f;
        }

        public void OnButtonReleased_Jump(object? sender, ButtonReleasedEventArgs e)
        {
            if (!this.isChargingJump || e.Button != this.Config.JumpKey)
                return;

            this.isChargingJump = false;

            // Contains its own, separate pathfinding logic
            Farmer player = Game1.player;
            Character jumper = player.isRidingHorse() ? player.mount : player;
            if (jumper == null) return;

            int jumpDistance = 1;
            if (player.isMoving())
            {
                float baseSpeed = 5f;
                float extraSpeed = player.getMovementSpeed() - baseSpeed;
                float bonusDistance = (extraSpeed > 0) ? extraSpeed * this.Config.JumpDistanceScaleFactor : 0;
                jumpDistance = (int)Math.Round(this.Config.NormalJumpDistance + bonusDistance);
            }

            Vector2 moveDirection = this.SafeGetDirectionVector(jumper);
            Vector2 bestLandingTile = jumper.Tile;

            for (int i = 1; i <= jumpDistance; i++)
            {
                Vector2 currentTile = jumper.Tile + moveDirection * i;
                if (this.IsSolidWall(currentTile)) break;
                if (this.IsJumpableObjectOnTile(currentTile)) continue;
                if (this.IsTileUnobstructedForLanding(currentTile)) bestLandingTile = currentTile;
                else break;
            }

            this.StartJump(player, bestLandingTile);
        }

        public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (this.currentState != JumpState.Idle)
            {
                this.UpdateJump();
                return;
            }

            if (this.isChargingJump)
            {
                this.jumpChargeTimer += (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
                if (!this.Helper.Input.IsDown(this.Config.JumpKey))
                {
                    this.isChargingJump = false;
                }
            }
        }

        private void StartJump(Farmer player, Vector2? landingTile = null)
        {
            this.isHorseJump = player.isRidingHorse() && player.mount != null;
            if (this.isHorseJump)
            {
                ModEntry.IsHorseJumping = true;
            }

            Character jumper = this.isHorseJump ? player.mount! : player;

            // The jump starts INSTANTLY.
            this.currentState = JumpState.Jumping;
            this.startPosition = jumper.Position;
            this.jumpProgress = 0;
            this.originalFrame = jumper.Sprite.currentFrame;
            player.canMove = false;
            Game1.playSound(this.Config.JumpSound);

            // Now we calculate where to land.
            if (landingTile.HasValue)
            {
                // If a landing tile was provided (by the charge jump), use it.
                this.targetPosition = (landingTile.Value == jumper.Tile)
                    ? jumper.Position
                    : new Vector2(landingTile.Value.X * 64 + 32, landingTile.Value.Y * 64 + 32) - new Vector2(jumper.GetBoundingBox().Width / 2f, jumper.GetBoundingBox().Height / 2f);
            }
            else
            {
                // If no landing tile was provided (by the instant jump), calculate it now.
                // This is the "think later" part of the logic.
                int jumpDistance = 1;
                if (player.isMoving())
                {
                    float baseSpeed = 5f;
                    float extraSpeed = player.getMovementSpeed() - baseSpeed;
                    float bonusDistance = (extraSpeed > 0) ? extraSpeed * this.Config.JumpDistanceScaleFactor : 0;
                    jumpDistance = (int)Math.Round(this.Config.NormalJumpDistance + bonusDistance);
                }
                Vector2 moveDirection = this.SafeGetDirectionVector(jumper);
                Vector2 bestLandingTile = jumper.Tile;

                for (int i = 1; i <= jumpDistance; i++)
                {
                    Vector2 currentTile = jumper.Tile + moveDirection * i;
                    if (this.IsSolidWall(currentTile))
                        break;
                    if (this.IsJumpableObjectOnTile(currentTile))
                        continue;
                    if (this.IsTileUnobstructedForLanding(currentTile))
                        bestLandingTile = currentTile;
                    else
                        break;
                }
                this.targetPosition = (bestLandingTile == jumper.Tile)
                    ? jumper.Position
                    : new Vector2(bestLandingTile.X * 64 + 32, bestLandingTile.Y * 64 + 32) - new Vector2(jumper.GetBoundingBox().Width / 2f, jumper.GetBoundingBox().Height / 2f);
            }

            // Calculate height based on mode.
            float baseHeight = this.Config.JumpHeight;
            if (this.isHorseJump) baseHeight *= 1.7f;
            float speedHeightBonus = player.isMoving() ? 1.25f : 1.0f;

            if (this.Config.InstantJump)
            {
                this.jumpHeight = baseHeight * speedHeightBonus;
            }
            else
            {
                // Charge height calculation
                float chargePercent = Math.Min(1f, this.jumpChargeTimer / MAX_JUMP_CHARGE_SECONDS);
                this.jumpHeight = baseHeight * (0.3f + (0.7f * chargePercent)) * speedHeightBonus;
            }

            this.Multiplayer.SendMessage(new JumpMessage(player.UniqueMultiplayerID), "PlayerJumped", modIDs: new[] { this.ModManifest.UniqueID });
        }

        private void UpdateJump()
        {
            this.jumpProgress += 1f;
            float percentDone = this.jumpProgress / this.Config.JumpDuration;

            Vector2 newPosition = Vector2.Lerp(this.startPosition, this.targetPosition, percentDone);
            float sinWave = (float)Math.Sin(percentDone * Math.PI);
            int currentArcOffset = (int)(-sinWave * this.jumpHeight);

            if (this.isHorseJump)
            {
                ModEntry.CurrentBounceFactor = this.Config.HorseJumpPlayerBounce;
                ModEntry.CurrentHorseJumpPosition = newPosition;
                ModEntry.CurrentHorseJumpYOffset = currentArcOffset;
            }
            else
            {
                Game1.player.Position = newPosition;
                Game1.player.yJumpOffset = currentArcOffset;
                Game1.player.Sprite.currentFrame = (this.jumpProgress < this.Config.JumpDuration / 2) ? 12 : 11;
            }

            this.currentState = (this.jumpProgress < this.Config.JumpDuration / 2) ? JumpState.Jumping : JumpState.Falling;
            if (this.jumpProgress >= this.Config.JumpDuration) this.EndJump();
        }

        private void EndJump()
        {
            if (this.isHorseJump)
            {
                ModEntry.IsHorseJumping = false;
                ModEntry.CurrentHorseJumpYOffset = 0;
            }

            Character jumper = this.isHorseJump && Game1.player.mount != null ? Game1.player.mount : Game1.player;

            jumper.Position = this.targetPosition;
            jumper.yJumpOffset = 0;
            Game1.player.Position = this.targetPosition;
            Game1.player.yJumpOffset = 0;
            Game1.player.canMove = true;
            this.currentState = JumpState.Idle;
            jumper.Sprite.currentFrame = this.originalFrame;
        }

        public void TriggerRemoteJump(long playerID)
        {
            Farmer? farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == playerID);
            if (farmer != null && !farmer.IsLocalPlayer)
            {
                farmer.jump();
            }
        }

        private Vector2 SafeGetDirectionVector(Character who)
        {
            return who.FacingDirection switch { 0 => new Vector2(0, -1), 1 => new Vector2(1, 0), 2 => new Vector2(0, 1), 3 => new Vector2(-1, 0), _ => Vector2.Zero, };
        }

        private Character? GetCharacterAtTile(GameLocation location, Vector2 tile)
        {
            // Check for any farmer (except yourself) at the tile.
            foreach (Farmer farmer in location.farmers)
            {
                if (farmer.IsLocalPlayer) continue;
                if (farmer.Tile == tile) return farmer;
            }
            // Check for any NPC at the tile.
            foreach (NPC npc in location.characters)
            {
                if (npc.Tile == tile) return npc;
            }
            return null;
        }

        private bool IsJumpableObjectOnTile(Vector2 tile)
        {
            GameLocation location = Game1.currentLocation;

            if (!location.isTilePassable(new Location((int)tile.X, (int)tile.Y), Game1.viewport)) return false;

            // Check for NPCs
            if (this.GetCharacterAtTile(location, tile) is NPC) return true;

            // Check for various object types
            if (location.objects.TryGetValue(tile, out StardewValley.Object obj))
            {
                // NEW: Items that can be picked up (forage, monster drops, etc.)
                if (obj.CanBeGrabbed) return true;

                // NEW: Craftables (scarecrows, sprinklers, chests, machines, etc.)
                if (obj.bigCraftable.Value) return true;

                // NEW: Breakable resources (stones, weeds, twigs)
                if (obj.Fragility == 0) return true;

                // Existing check for Fences
                if (obj is Fence) return true;
            }

            // Existing check for terrain features (bushes, saplings)
            if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature feature))
            {
                if (feature is Bush || feature.GetType().Name == "Weed") return true;
                if (feature is Tree tree && tree.growthStage.Value < 5) return true;
                if (feature is FruitTree fTree && fTree.growthStage.Value < 4) return true;
            }

            // Existing check for config-enabled resource clumps
            foreach (ResourceClump rc in location.resourceClumps)
            {
                if (rc.getBoundingBox().Contains(tile.X * 64 + 32, tile.Y * 64 + 32))
                {
                    if (this.Config.JumpOverBoulders && (rc.parentSheetIndex.Value == 672)) return true;
                    if (this.Config.JumpOverLargeStumps && (rc.parentSheetIndex.Value == 600)) return true;
                    if (this.Config.JumpOverLargeLogs && (rc.parentSheetIndex.Value == 602)) return true;
                }
            }

            return false;
        }

        private bool IsSolidWall(Vector2 tile)
        {
            GameLocation location = Game1.currentLocation;

            if (!location.isTileOnMap(tile)) return true;
            if (location.getTileIndexAt((int)tile.X, (int)tile.Y, "Buildings") != -1)
            {
                // If it's a building tile, check if it has the "Passable" property. If so, it's NOT a wall.
                if (location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Passable", "Buildings") != null)
                {
                    return false;
                }
                return true; // It's a building tile without a "Passable" property, so it's a wall.
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

            // Check for fundamental blockers like solid walls, buildings, and water
            if (this.IsSolidWall(tile) || location.isWaterTile((int)tile.X, (int)tile.Y))
                return false;

            // Check if the tile has a "jumpable" obstacle on it (cannot land on these)
            if (this.IsJumpableObjectOnTile(tile))
                return false;

            // Check for objects on the tile (chests, furnaces, etc.)
            if (location.Objects.ContainsKey(tile))
                return false;

            // Check for characters (NPCs, other farmers)
            if (this.GetCharacterAtTile(location, tile) != null)
                return false;

            // Check for large terrain features (boulders that are part of the map)
            if (location.largeTerrainFeatures.Any(tf => tf.getBoundingBox().Intersects(new Microsoft.Xna.Framework.Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64))))
                return false;

            // Check for resource clumps that are NOT configured to be jumpable
            if (!this.IsJumpableObjectOnTile(tile) && location.resourceClumps.Any(rc => rc.getBoundingBox().Intersects(new Microsoft.Xna.Framework.Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64))))
            {
                return false;
            }

            // If we passed all checks, the tile is clear for landing.
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