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
        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private readonly IMultiplayerHelper Multiplayer;
        private readonly IManifest ModManifest;
        private int originalPetSpeed = -1;

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

            // This method only plays the sound for the local player.
            this.PlayLocalWhistleSound();

            // Execute the whistle logic for the local player.
            this.PerformWhistle(Game1.player);

            // Send a message to other players to also execute the logic.
            if (Context.IsMultiplayer)
            {
                this.Multiplayer.SendMessage(
                    new WhistleMessage(Game1.player.UniqueMultiplayerID),
                    "WhistleCommand",
                    modIDs: new[] { this.ModManifest.UniqueID }
                );
            }
        }

        // This method is called by remote clients when they receive a whistle message.
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
        // A dedicated method for handling the local player's whistle sound.
        private void PlayLocalWhistleSound()
        {
            Game1.soundBank.PlayCue("whistle");
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
                pet.speed = this.originalPetSpeed + 2;

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
}