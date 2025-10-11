using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Monsters;
using StardewValley.Pathfinding;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using xTile.Dimensions;

namespace MovementOverhaul
{
    public class NpcLogic
    {
        private class AnnoyanceState
        {
            public int WhistleCount { get; set; } = 0;
            public bool HasLostFriendshipToday { get; set; } = false;
        }
        private class PausedNpcState
        {
            public readonly NPC Npc;
            public float PauseTimer;
            public readonly PathFindController? OriginalController;

            public PausedNpcState(NPC npc, float duration)
            {
                this.Npc = npc;
                this.PauseTimer = duration;
                this.OriginalController = npc.controller;
            }
        }

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private readonly IMultiplayerHelper Multiplayer;
        private readonly IManifest ModManifest;
        private int originalPetSpeed = -1;
        private readonly Dictionary<string, AnnoyanceState> npcAnnoyanceTracker = new();
        private readonly List<PausedNpcState> _pausedNpcs = new();

        public NpcLogic(IModHelper helper, IMonitor monitor, IMultiplayerHelper multiplayer, IManifest manifest)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.Multiplayer = multiplayer;
            this.ModManifest = manifest;
        }

        public void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!ModEntry.Instance.Config.EnableWhistle || !Context.IsWorldReady || !Context.CanPlayerMove || e.Button != ModEntry.Instance.Config.WhistleKey)
                return;

            if (Context.IsMultiplayer)
            {
                this.Multiplayer.SendMessage(
                    new WhistleAnimationMessage(Game1.player.UniqueMultiplayerID),
                    "PlayWhistleAnimation",
                    modIDs: new[] { this.ModManifest.UniqueID }
                );
            }

            ModEntry.Instance.LogDebug("Whistle key pressed by local player. Starting animation weeee");
            this.StartWhistleAnimation(Game1.player);
        }

        public void HandleRemoteWhistleAnimation(long playerID)
        {
            Farmer? whistler = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == playerID);
            if (whistler != null)
            {
                this.StartWhistleAnimation(whistler);
            }
        }

        public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            // Paused NPC Countdown & State Restoration
            if (this._pausedNpcs.Any())
            {
                float elapsedSeconds = (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
                for (int i = this._pausedNpcs.Count - 1; i >= 0; i--)
                {
                    var state = this._pausedNpcs[i];
                    state.PauseTimer -= elapsedSeconds;

                    //state.Npc.Halt(); // Keep them frozen (removed fixed animations yay)

                    if (state.PauseTimer <= 0f)
                    {
                        ModEntry.Instance.LogDebug($"You can move now, '{state.Npc.Name}'");
                        state.Npc.controller = state.OriginalController;
                        this._pausedNpcs.RemoveAt(i);
                    }
                }
            }
        }
        public void HandleRemoteWhistle(long playerID)
        {
            Farmer? whistler = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == playerID);
            if (whistler != null)
            {
                ModEntry.Instance.LogDebug($"Remote whistler is '{whistler.Name}' DUN DUN DUUUN");
                if (ModEntry.Instance.Config.HearRemoteWhistles && whistler.currentLocation == Game1.currentLocation)
                {
                    Game1.playSound("whistle");
                }
                this.PerformWhistle(whistler);
            }
        }

        // This is the core logic, now shared by local and remote players.
        private void PerformWhistle(Farmer whistler)
        {
            if (whistler.currentLocation == null) return;
            ModEntry.Instance.LogDebug($"'{whistler.Name}' is whistling at {whistler.currentLocation.Name}. What the helly!");

            // Call Pet (only the owner's pet will respond)
            Pet? pet = whistler.getPet();
            if (pet != null && pet.currentLocation == whistler.currentLocation)
            {
                ModEntry.Instance.LogDebug($"-> Found pet '{pet.displayName}'. Attempting to call forth");

                // Animation reset
                pet.Halt();
                pet.isSleeping.Value = false;
                pet.Sprite.StopAnimation();
                //pet.Sprite.ClearAnimation();
                pet.Sprite.CurrentFrame = 28;

                Point targetTile = this.GetRandomAdjacentOpenTile(whistler.TilePoint, whistler.currentLocation) ?? whistler.TilePoint;
                pet.doEmote(16);

                // Increase the pet's speed.
                this.originalPetSpeed = pet.speed;
                pet.speed = this.originalPetSpeed + 1;
                ModEntry.Instance.LogDebug($"--> Pet speed boosted to {pet.speed}.");

                // Pass the PetPathEndBehavior to reset its speed when it arrives.
                var controller = new PathFindController(pet, whistler.currentLocation, targetTile, -1, this.PetPathEndBehavior);
                if (controller.pathToEndPoint != null)
                {
                    pet.controller = controller;
                }
                else
                {
                    ModEntry.Instance.LogDebug($"--> Could not find a path for the '{pet.displayName}'. Resetting speed");
                    pet.speed = this.originalPetSpeed;
                }
            }

            // Call Farm Animals (only works if in the Farm location)
            if (whistler.currentLocation is Farm farm)
            {
                foreach (FarmAnimal animal in farm.animals.Values)
                {
                    if (animal.friendshipTowardFarmer.Value >= (ModEntry.Instance.Config.WhistleAnimalMinHearts * 200))
                    {
                        ModEntry.Instance.LogDebug($"{whistler.Name} is calling an animal. It is a {animal.type} named {animal.displayName}! What the helly.");
                        Point targetTile = this.GetRandomAdjacentOpenTile(whistler.TilePoint, farm) ?? whistler.TilePoint;
                        animal.doEmote(16);
                        var controller = new PathFindController(animal, farm, targetTile, -1, this.AnimalPathEndBehavior);
                        if (controller.pathToEndPoint != null)
                        {
                            animal.controller = controller;
                        }
                    }
                }
            }

            // NPC Annoyance Logic
            if (ModEntry.Instance.Config.WhistleAnnoysNPCs && whistler.currentLocation is not Farm)
            {
                foreach (NPC npc in whistler.currentLocation.characters.OfType<NPC>())
                {
                    if (npc.IsMonster || !npc.IsVillager || Vector2.Distance(whistler.Tile, npc.Tile) > 10)
                        continue;

                    ModEntry.Instance.LogDebug($"-> NPC '{npc.Name}' is in range of whistle.");

                    if (!this.npcAnnoyanceTracker.TryGetValue(npc.Name, out var annoyanceState))
                    {
                        annoyanceState = new AnnoyanceState();
                        this.npcAnnoyanceTracker[npc.Name] = annoyanceState;
                    }

                    this.PauseNpc(npc, ModEntry.Instance.Config.NPCPauseFromWhistle);

                    if (annoyanceState.HasLostFriendshipToday)
                    {
                        npc.doEmote(40); // "..." emote
                        continue;
                    }

                    annoyanceState.WhistleCount++;
                    ModEntry.Instance.LogDebug($"--> '{npc.Name}' has been whistled at {annoyanceState.WhistleCount} times.");

                    if (annoyanceState.WhistleCount > ModEntry.Instance.Config.WhistleNumberBeforeAnnoying)
                    {
                        ModEntry.Instance.LogDebug($"---> Annoyance threshold reached! '{npc.Name}' is now annoyed! Oh naur");
                        whistler.changeFriendship(-ModEntry.Instance.Config.WhistleFriendshipPenalty, npc);
                        npc.doEmote(12); // Angry emote
                        annoyanceState.HasLostFriendshipToday = true;
                    }
                    else
                    {
                        npc.doEmote(16); // '!' emote
                    }
                }
            }

            // Monster aggro
            if (ModEntry.Instance.Config.WhistleAggrosMonsters && !whistler.currentLocation.IsFarm)
            {
                foreach (Monster monster in whistler.currentLocation.characters.OfType<Monster>())
                {
                    ModEntry.Instance.LogDebug($"-> Monster '{monster.Name}' is being aggroed by whistle.");
                    monster.doEmote(16);
                    // This gives the monster a temporary path to the player who whistled.
                    var controller = new PathFindController(monster, whistler.currentLocation, whistler.TilePoint, -1, this.MonsterPathEndBehavior);
                    if (controller.pathToEndPoint != null)
                    {
                        monster.controller = controller;
                    }
                }
            }
        }
        private void PauseNpc(NPC npc, float durationSeconds)
        {
            var existingState = this._pausedNpcs.FirstOrDefault(p => p.Npc == npc);
            if (existingState != null)
            {
                ModEntry.Instance.LogDebug($"-> NPC '{npc.Name}' was already paused. Resetting timer to {durationSeconds}s.");
                existingState.PauseTimer = durationSeconds; // Reset timer if already paused
            }
            else
            {
                ModEntry.Instance.LogDebug($"-> Pausing NPC '{npc.Name}' for {durationSeconds}s.");
                // Add a new paused state, which saves the NPC's current controller.
                this._pausedNpcs.Add(new PausedNpcState(npc, durationSeconds));
            }
            // Temporarily clear the controller to ensure they stop immediately.
            //npc.Halt();
            npc.controller = null;
        }

        private void StartWhistleAnimation(Farmer who)
        {
            if (!who.CanMove) return;

            who.faceDirection(2);
            who.canMove = false;
            who.synchronizedJump(3);

            if (who.IsLocalPlayer)
            {
                Game1.soundBank.PlayCue("whistle");
            }

            FarmerSprite.AnimationFrame[] frames = new FarmerSprite.AnimationFrame[]
            {
                new FarmerSprite.AnimationFrame(67, 100),
                new FarmerSprite.AnimationFrame(26, 200),
                new FarmerSprite.AnimationFrame(16, 100),
            };

            who.FarmerSprite.animateOnce(frames, (f) => {
                f.canMove = true;
                // The whistle logic now runs after the animation is complete for the local player.
                if (who.IsLocalPlayer)
                {
                    ModEntry.Instance.LogDebug("Local player whistle animation finished. Performing whistle logic.");
                    this.PerformWhistle(who);
                    if (Context.IsMultiplayer)
                    {
                        ModEntry.Instance.LogDebug("Sending whistle command to other players.");
                        this.Multiplayer.SendMessage(new WhistleMessage(who.UniqueMultiplayerID), "WhistleCommand", modIDs: new[] { this.ModManifest.UniqueID });
                    }
                }
            });
        }

        public void ResetState()
        {
            this._pausedNpcs.Clear();
        }

        public void ResetDailyState()
        {
            ModEntry.Instance.LogDebug("New day started. Resetting daily NPC annoyance trackers.");
            foreach (var state in this.npcAnnoyanceTracker.Values)
            {
                state.HasLostFriendshipToday = false;
            }
        }

        private void MonsterPathEndBehavior(Character c, GameLocation location)
        {
            if (c is Monster monster)
            {
                ModEntry.Instance.LogDebug($"Monster '{monster.displayName}' reached whistle target.");
                monster.controller = null;
            }
        }

        private void PetPathEndBehavior(Character c, GameLocation location)
        {
            if (c is Pet pet && this.originalPetSpeed != -1)
            {
                ModEntry.Instance.LogDebug($"Pet '{pet.displayName}' reached whistle target. Resetting speed from {pet.speed} to {this.originalPetSpeed}.");
                pet.speed = this.originalPetSpeed;
                this.originalPetSpeed = -1;
            }
        }

        private void AnimalPathEndBehavior(Character c, GameLocation location)
        {
            if (c is FarmAnimal animal)
            {
                ModEntry.Instance.LogDebug($"Animal '{animal.displayName}' the '{animal.type}' reached whistle target.");
                animal.Halt();
            }
        }

        private Point? GetRandomAdjacentOpenTile(Point origin, GameLocation location)
        {
            List<Point> openTiles = new List<Point>();

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue;

                    Point adjacentTile = new Point(origin.X + x, origin.Y + y);

                    if (location.isTileOnMap(adjacentTile.X, adjacentTile.Y)
                        && location.isTileLocationOpen(new Location(adjacentTile.X, adjacentTile.Y))
                        && location.isTilePassable(new Location(adjacentTile.X, adjacentTile.Y), Game1.viewport))
                    {
                        openTiles.Add(adjacentTile);
                    }
                }
            }

            if (openTiles.Any())
            {
                return openTiles[Game1.random.Next(openTiles.Count)];
            }

            return null;
        }
    }

    [HarmonyPatch(typeof(NPC), nameof(NPC.update), new Type[] { typeof(GameTime), typeof(GameLocation) })]
    public class NPC_Update_Patch
    {
        // This Postfix patch runs AFTER the NPC's normal AI update.
        // It checks if the NPC is "running late" and adjusts their speed accordingly.
        public static void Postfix(NPC __instance, GameTime time, GameLocation location)
        {
            try
            {
                if (!ModEntry.Instance.Config.EnableRunningLate || !__instance.IsVillager || __instance.controller == null)
                    return;

                // Check if the NPC is following a schedule path
                if (__instance.controller.pathToEndPoint != null && __instance.controller.pathToEndPoint.Count > 0)
                {
                    Point finalDestination = __instance.controller.pathToEndPoint.Last();
                    float distance = Vector2.Distance(__instance.Tile, new Vector2(finalDestination.X, finalDestination.Y));

                    // If they are far away (more than specified tiles), make them move faster.
                    if (distance > ModEntry.Instance.Config.DistanceConsideredFar)
                    {
                        //ModEntry.Instance.LogDebug($"[Harmony] NPC '{__instance.Name}' is running late (distance: {distance:F2}). Setting speed to 4.");
                        __instance.speed = 4; // Faster than normal walking speed (2)
                    }
                    else
                    {
                        //ModEntry.Instance.LogDebug($"[Harmony] NPC '{__instance.Name}' is no longer running late. Resetting speed to 2.");
                        __instance.speed = 2; // Reset to normal speed
                    }
                }
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor.Log($"Failed in {nameof(NPC_Update_Patch)}:\n{ex}", LogLevel.Error);
            }
        }
    }

}