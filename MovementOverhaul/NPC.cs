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

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private readonly IMultiplayerHelper Multiplayer;
        private readonly IManifest ModManifest;
        private int originalPetSpeed = -1;
        private readonly Dictionary<string, AnnoyanceState> npcAnnoyanceTracker = new();

        public NpcLogic(IModHelper helper, IMonitor monitor, IMultiplayerHelper multiplayer, IManifest manifest)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.Multiplayer = multiplayer;
            this.ModManifest = manifest;
        }

        public void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!ModEntry.Config.EnableWhistle || !Context.IsWorldReady || !Context.CanPlayerMove || e.Button != ModEntry.Config.WhistleKey)
                return;

            // This logic has been simplified and corrected to avoid duplicate calls.
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

        public void HandleRemoteWhistle(long playerID)
        {
            Farmer? whistler = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == playerID);
            if (whistler != null)
            {
                if (ModEntry.Config.HearRemoteWhistles)
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

            // Call Pet (only the owner's pet will respond)
            Pet? pet = whistler.getPet();
            if (pet != null && pet.currentLocation == whistler.currentLocation)
            {
                Point targetTile = this.GetRandomAdjacentOpenTile(whistler.TilePoint, whistler.currentLocation) ?? whistler.TilePoint;
                pet.doEmote(16);

                // Increase the pet's speed.
                this.originalPetSpeed = pet.speed;
                pet.speed = this.originalPetSpeed + 1;

                // Pass the PetPathEndBehavior to reset its speed when it arrives.
                var controller = new PathFindController(pet, whistler.currentLocation, targetTile, -1, this.PetPathEndBehavior);
                if (controller.pathToEndPoint != null)
                {
                    pet.controller = controller;
                }
                else
                {
                    // If no path could be found, immediately reset the speed.
                    pet.speed = this.originalPetSpeed;
                }
            }

            // Call Farm Animals (only works if in the Farm location)
            if (whistler.currentLocation is Farm farm)
            {
                foreach (FarmAnimal animal in farm.animals.Values)
                {
                    if (animal.friendshipTowardFarmer.Value >= (ModEntry.Config.WhistleAnimalMinHearts * 200))
                    {
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
            if (ModEntry.Config.WhistleAnnoysNPCs && whistler.currentLocation is not Farm)
            {
                foreach (NPC npc in whistler.currentLocation.characters.OfType<NPC>())
                {
                    if (npc.IsMonster || !npc.IsVillager || Vector2.Distance(whistler.Tile, npc.Tile) > 10)
                        continue;

                    if (!this.npcAnnoyanceTracker.TryGetValue(npc.Name, out var annoyanceState))
                    {
                        annoyanceState = new AnnoyanceState();
                        this.npcAnnoyanceTracker[npc.Name] = annoyanceState;
                    }

                    if (annoyanceState.HasLostFriendshipToday)
                    {
                        npc.doEmote(40); // "..." emote
                        continue;
                    }

                    annoyanceState.WhistleCount++;

                    if (annoyanceState.WhistleCount > ModEntry.Config.WhistleNumberBeforeAnnoying)
                    {
                        whistler.changeFriendship(-ModEntry.Config.WhistleFriendshipPenalty, npc);
                        npc.doEmote(12); // Angry emote
                        annoyanceState.HasLostFriendshipToday = true;
                    }
                    else
                    {
                        npc.doEmote(16); // '!' emote in your code, assuming this is correct
                    }
                }
            }

            // Monster aggro
            if (ModEntry.Config.WhistleAggrosMonsters && !whistler.currentLocation.IsFarm)
            {
                foreach (Monster monster in whistler.currentLocation.characters.OfType<Monster>())
                {
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

        private void StartWhistleAnimation(Farmer who)
        {
            if (!who.CanMove) return;

            who.faceDirection(2);
            who.canMove = false;
            Game1.soundBank.PlayCue("whistle");
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
                    this.PerformWhistle(who);
                    if (Context.IsMultiplayer)
                    {
                        this.Multiplayer.SendMessage(new WhistleMessage(who.UniqueMultiplayerID), "WhistleCommand", modIDs: new[] { this.ModManifest.UniqueID });
                    }
                }
            });
        }

        public void ResetDailyState()
        {
            foreach (var state in this.npcAnnoyanceTracker.Values)
            {
                state.HasLostFriendshipToday = false;
            }
        }

        private void MonsterPathEndBehavior(Character c, GameLocation location)
        {
            // This clears the temporary pathfinding controller, allowing the monster's normal AI to take back over.
            if (c is Monster monster)
            {
                monster.controller = null;
            }
        }

        private void PetPathEndBehavior(Character c, GameLocation location)
        {
            if (c is Pet pet && this.originalPetSpeed != -1)
            {
                pet.speed = this.originalPetSpeed;
                this.originalPetSpeed = -1;
            }
        }

        private void AnimalPathEndBehavior(Character c, GameLocation location)
        {
            if (c is FarmAnimal animal)
            {
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
        /// <summary>
        /// This Postfix patch runs AFTER the NPC's normal AI update.
        /// It checks if the NPC is "running late" and adjusts their speed accordingly.
        /// </summary>
        public static void Postfix(NPC __instance, GameTime time, GameLocation location)
        {
            try
            {
                if (!ModEntry.Config.EnableRunningLate || !__instance.IsVillager || __instance.controller == null)
                    return;

                // Check if the NPC is following a schedule path
                if (__instance.controller.pathToEndPoint != null && __instance.controller.pathToEndPoint.Count > 0)
                {
                    Point finalDestination = __instance.controller.pathToEndPoint.Last();
                    float distance = Vector2.Distance(__instance.Tile, new Vector2(finalDestination.X, finalDestination.Y));

                    // If they are far away (more than specified tiles), make them move faster.
                    if (distance > ModEntry.Config.DistanceConsideredFar)
                    {
                        __instance.speed = 4; // Faster than normal walking speed (2)
                    }
                    else
                    {
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