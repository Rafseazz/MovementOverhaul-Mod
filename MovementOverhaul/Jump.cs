using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using System;
using System.Linq;
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

        // A helper property for other classes to easily check if a jump is happening.
        public bool IsJumping => this._activeJumps.ContainsKey(Game1.player.UniqueMultiplayerID);

        private readonly IModHelper Helper;
        private readonly IMultiplayerHelper Multiplayer;
        private readonly IManifest ModManifest;

        public bool IsChargingJump { get; private set; } = false;
        private float jumpChargeTimer = 0f;
        private const float MAX_JUMP_CHARGE_SECONDS = 0.75f;
        public bool IsPlayerJumping(long farmerId)
        {
            return this._activeJumps.ContainsKey(farmerId);
        }

        public JumpLogic(IModHelper helper, IMultiplayerHelper multiplayer, IManifest manifest)
        {
            this.Helper = helper;
            this.Multiplayer = multiplayer;
            this.ModManifest = manifest;
        }

        public bool OnButtonPressed_Instant(ButtonPressedEventArgs e)
        {
            if (!ModEntry.Instance.Config.EnableJump || e.Button != ModEntry.Instance.Config.JumpKey || !Game1.player.canMove || Game1.player.IsSitting() || Game1.eventUp || this._activeJumps.ContainsKey(Game1.player.UniqueMultiplayerID))
                return false;

            Vector2 landingTile = this.CalculateBestLandingTile();
            this.StartJump(Game1.player, landingTile);
            return true;
        }

        public void OnButtonPressed_Charge(object? sender, ButtonPressedEventArgs e)
        {
            if (!ModEntry.Instance.Config.EnableJump || e.Button != ModEntry.Instance.Config.JumpKey || !Game1.player.canMove || Game1.player.IsSitting() || Game1.eventUp || this._activeJumps.ContainsKey(Game1.player.UniqueMultiplayerID))
                return;

            ModEntry.Instance.LogDebug("Jump key pressed. Starting charge.");
            this.IsChargingJump = true;
            this.jumpChargeTimer = 0f;
        }

        public void OnButtonReleased_Jump(object? sender, ButtonReleasedEventArgs e)
        {
            if (!this.IsChargingJump || e.Button != ModEntry.Instance.Config.JumpKey)
                return;

            ModEntry.Instance.LogDebug($"Jump key released. Charge time: {this.jumpChargeTimer:F2}s. Calculating and starting jump.");
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
                float bonusDistance = (extraSpeed > 0) ? extraSpeed * ModEntry.Instance.Config.JumpDistanceScaleFactor : 0;
                maxJumpDistance = (int)Math.Round(ModEntry.Instance.Config.NormalJumpDistance + bonusDistance);
            }
            ModEntry.Instance.LogDebug($"Calculating jump distance. Charge Timer: {this.jumpChargeTimer:F2}s.");

            if (!ModEntry.Instance.Config.InstantJump && ModEntry.Instance.Config.ChargeAffectsDistance)
            {
                float chargePercent = Math.Min(1f, this.jumpChargeTimer / MAX_JUMP_CHARGE_SECONDS);
                jumpDistance = (int)Math.Ceiling(1 + (maxJumpDistance - 1) * chargePercent * 1.5f);
                ModEntry.Instance.LogDebug($"-> Charge jump calculated. Charge %: {chargePercent:P0}, Max Dist: {maxJumpDistance}, Final Dist: {jumpDistance}");
            }
            else
            {
                jumpDistance = maxJumpDistance;
                ModEntry.Instance.LogDebug($"-> Instant jump calculated. Max Dist: {maxJumpDistance}, Final Dist: {jumpDistance}");
            }

            // Enforce a minimum distance for all horse jumps (can't do 2.5 huhu)
            if (player.isRidingHorse())
            {
                jumpDistance = Math.Max(3, jumpDistance);
            }

            Vector2 moveDirection = this.SafeGetDirectionVector(jumper);

            if (ModEntry.Instance.Config.HopOverAnything)
            {
                return jumper.Tile + moveDirection * jumpDistance;
            }

            Vector2 bestLandingTile = jumper.Tile;
            ModEntry.Instance.LogDebug($"-> Searching for best landing tile up to {jumpDistance} tiles away...");
            for (int i = 1; i <= jumpDistance; i++)
            {
                Vector2 currentTile = jumper.Tile + moveDirection * i;
                if (this.IsSolidWall(currentTile))
                {
                    ModEntry.Instance.LogDebug($"--> Tile {currentTile} is a solid wall. Stopping search.");
                    break;
                }
                if (this.IsJumpableObjectOnTile(currentTile))
                {
                    ModEntry.Instance.LogDebug($"--> Tile {currentTile} has a jumpable object. Skipping.");
                    continue;
                }
                if (this.IsTileUnobstructedForLanding(currentTile)) bestLandingTile = currentTile;
                else
                {
                    ModEntry.Instance.LogDebug($"--> Tile {currentTile} is obstructed. Stopping search.");
                    break;
                }
            }
            ModEntry.Instance.LogDebug($"==> Best landing tile found: {bestLandingTile}");
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
                if (!this.Helper.Input.IsDown(ModEntry.Instance.Config.JumpKey))
                    this.IsChargingJump = false;
            }
        }

        private void StartJump(Farmer player, Vector2 landingTile)
        {
            if (this._activeJumps.ContainsKey(player.UniqueMultiplayerID)) return;

            bool isHorseJump = player.isRidingHorse() && player.mount != null;

            if (ModEntry.Instance.Config.JumpStaminaCost > 0)
            {
                bool freeJump = isHorseJump && ModEntry.Instance.Config.NoStaminaDrainOnHorse;
                if (!freeJump)
                {
                    if (player.stamina < ModEntry.Instance.Config.JumpStaminaCost)
                    {
                        ModEntry.Instance.LogDebug($"Jump aborted: Not enough stamina. Have {player.stamina}, need {ModEntry.Instance.Config.JumpStaminaCost}.");
                        return;
                    }
                    player.stamina -= ModEntry.Instance.Config.JumpStaminaCost;
                }
            }

            if (isHorseJump)
            {
                ModEntry.IsHorseJumping = true;
            }
            ModEntry.Instance.LogDebug($"{player.Name} IS GONNA JUMP YAHOO!");
            ModEntry.Instance.LogDebug($"-> From tile {player.Tile} to {landingTile}. Horse jump: {isHorseJump}.");

            Character jumper = isHorseJump ? player.mount! : player;
            Vector2 startPosition = jumper.Position;
            Vector2 targetPosition = (landingTile == jumper.Tile)
                ? jumper.Position
                : new Vector2(landingTile.X * 64 + 32, landingTile.Y * 64 + 32) - new Vector2(jumper.GetBoundingBox().Width / 2f, jumper.GetBoundingBox().Height / 2f);

            float baseHeight = ModEntry.Instance.Config.JumpHeight;
            if (isHorseJump) baseHeight *= 1.7f;
            float speedHeightBonus = player.movementDirections.Any() ? 1.25f : 1.0f;
            float jumpHeight;

            if (ModEntry.Instance.Config.InstantJump)
            {
                jumpHeight = baseHeight * speedHeightBonus;
            }
            else
            {
                float chargePercent = Math.Min(1f, this.jumpChargeTimer / MAX_JUMP_CHARGE_SECONDS);
                jumpHeight = baseHeight * (0.5f + (1.2f * chargePercent)) * speedHeightBonus;
            }

            if (!string.IsNullOrEmpty(ModEntry.Instance.Config.JumpSound))
            {
                Game1.playSound(ModEntry.Instance.Config.JumpSound);
                if (ModEntry.Instance.Config.AmplifyJumpSound)
                {
                    Game1.playSound(ModEntry.Instance.Config.JumpSound);
                    Game1.playSound(ModEntry.Instance.Config.JumpSound);
                }
            }
            ModEntry.Instance.LogDebug($"-> Jump height calculated: {jumpHeight:F2}. Duration: {ModEntry.Instance.Config.JumpDuration} ticks.");

            var jumpState = new JumpArcState(ModEntry.Instance.Config.JumpDuration, jumpHeight, startPosition, targetPosition, isHorseJump, jumper.Sprite.currentFrame);
            this._activeJumps[player.UniqueMultiplayerID] = jumpState;

            player.canMove = false;

            ModEntry.Instance.LogDebug("-> Sending jump sync message to other players.");
            var message = new FullJumpSyncMessage(player.UniqueMultiplayerID, startPosition, targetPosition, ModEntry.Instance.Config.JumpDuration, jumpHeight, isHorseJump);
            this.Multiplayer.SendMessage(message, "FullJumpSync", modIDs: new[] { this.ModManifest.UniqueID });
        }
        public void StartRemoteJump(FullJumpSyncMessage msg)
        {
            Farmer? farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == msg.PlayerID);
            if (farmer is null) return;

            ModEntry.Instance.LogDebug($"Received remote jump sync for '{farmer.Name}'. Starting their jump arc locally.");

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
                float sinWave = (float)Math.Sin(percentDone * Math.PI);
                int currentArcOffset = (int)(-sinWave * jump.Height);

                // Logic is split for Local vs. Remote players

                if (farmer.IsLocalPlayer)
                {
                    // For the local player, we control everything.
                    Vector2 newPosition = Vector2.Lerp(jump.StartPos, jump.TargetPos, percentDone);
                    Character jumper = jump.IsHorseJump && farmer.mount != null ? farmer.mount : farmer;
                    jumper.Position = newPosition;
                    jumper.yJumpOffset = currentArcOffset;

                    if (jump.IsHorseJump)
                    {
                        ModEntry.CurrentBounceFactor = ModEntry.Instance.Config.HorseJumpPlayerBounce;
                        ModEntry.CurrentHorseJumpPosition = newPosition;
                        ModEntry.CurrentHorseJumpYOffset = currentArcOffset;
                    }
                    else if (jumper is Farmer f)
                    {
                        bool isSitJump = jump.OriginalFrame == 29 || jump.OriginalFrame == 58 || jump.OriginalFrame == 62;

                        if (!isSitJump)
                        {
                            // Only apply the standard jump animation if it's NOT a sit-jump.
                            f.Sprite.currentFrame = (jump.Progress < jump.Duration / 2) ? 12 : 11;
                        }
                    }
                }
                else // For REMOTE players
                {
                    // For remote players, we let the game's netcode handle horizontal position.
                    // We ONLY apply the vertical arc to prevent teleporting.
                    Character jumper = jump.IsHorseJump && farmer.mount != null ? farmer.mount : farmer;
                    jumper.yJumpOffset = currentArcOffset;

                    if (jump.IsHorseJump)
                    {
                        farmer.yJumpOffset = (int)(currentArcOffset * ModEntry.Instance.Config.HorseJumpPlayerBounce);
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
            ModEntry.Instance.LogDebug($"{farmer.Name} finished jumping. Yiy.");
            // Fail safe if ever player gets stuck while jumping on horse
            if (farmer.IsLocalPlayer && jump.IsHorseJump)
            {
                ModEntry.IsHorseJumping = false;
                ModEntry.CurrentHorseJumpYOffset = 0;
            }

            Character jumper = jump.IsHorseJump && farmer.mount != null ? farmer.mount! : farmer;

            // Only forcefully set the final position for the local player's on-foot jumps.
            // For remote players, we let the game's own netcode handle the final position to prevent teleporting.
            // For the local horse jump, the Harmony patch handles the final position.
            if (farmer.IsLocalPlayer && !jump.IsHorseJump)
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

            // Check for the custom "Jumpable" property on the map tile.
            string? jumpableProp = location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Jumpable", "Back")
                                   ?? location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Jumpable", "Buildings");

            if (jumpableProp != null)
            {
                ModEntry.Instance.LogDebug($"--> Tile {tile} has 'Jumpable' property. It can be jumped over.");
                return true;
            }

            if (ModEntry.Instance.Config.JumpOverTrashCans)
            {
                string? tileAction = location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Action", "Buildings")
                                     ?? location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Action", "Back");

                if (tileAction?.StartsWith("Garbage") == true)
                {
                    return true;
                }
            }
            if (!location.isTilePassable(new Location((int)tile.X, (int)tile.Y), Game1.viewport)) return false;
            if (this.GetCharacterAtTile(location, tile) is NPC && !ModEntry.Instance.Config.JumpThroughNPCs)
                return true;
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
                    if (ModEntry.Instance.Config.JumpOverBoulders && (rc.parentSheetIndex.Value == 672)) return true;
                    if (ModEntry.Instance.Config.JumpOverLargeStumps && (rc.parentSheetIndex.Value == 600)) return true;
                    if (ModEntry.Instance.Config.JumpOverLargeLogs && (rc.parentSheetIndex.Value == 602)) return true;
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

            Character? c = this.GetCharacterAtTile(location, tile);
            if (c != null)
            {
                // If it's an NPC and we can jump through them, the tile is NOT obstructed.
                if (c is NPC && ModEntry.Instance.Config.JumpThroughNPCs)
                {
                    // Allow landing.
                }
                // If it's another Farmer and we can jump through them, the tile is NOT obstructed.
                else if (c is Farmer && ModEntry.Instance.Config.JumpThroughPlayers)
                {
                    // Allow landing.
                }
                else
                {
                    // It's a character we can't jump through. The tile IS obstructed.
                    return false;
                }
            }

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

    // Sync held item to jump
    [HarmonyPatch(typeof(StardewValley.Object), nameof(StardewValley.Object.drawWhenHeld))]
    public class Object_DrawWhenHeld_Patch
    {
        // This Prefix patch runs BEFORE the game draws an item being held.
        // It modifies the 'objectPosition' parameter by reference if the holding farmer is jumping.
        public static void Prefix(StardewValley.Object __instance, Farmer f, ref Vector2 objectPosition)
        {
            try
            {
                // Check if the farmer holding the item is currently jumping via our mod.
                if (ModEntry.JumpLogic != null && ModEntry.JumpLogic.IsPlayerJumping(f.UniqueMultiplayerID))
                {
                    // Add the farmer's vertical jump offset to the item's draw position.
                    objectPosition.Y += f.yJumpOffset;
                }
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor.Log($"Failed in {nameof(Object_DrawWhenHeld_Patch)}:\n{ex}", LogLevel.Error);
            }
        }
    }
}