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
        private class PausedNpcState
        {
            public readonly NPC Npc;
            public readonly PathFindController? OriginalController;
            public float PauseTimer;

            public PausedNpcState(NPC npc, float duration)
            {
                this.Npc = npc;
                this.OriginalController = npc.controller;
                this.PauseTimer = duration;
            }
        }
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

        private readonly List<NPC> _pausedNpcs = new();

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

            // Start the local animation and send sync message.
            this.StartWhistleAnimation(Game1.player);
            if (Context.IsMultiplayer)
            {
                this.Multiplayer.SendMessage(new WhistleAnimationMessage(Game1.player.UniqueMultiplayerID), "PlayWhistleAnimation", modIDs: new[] { this.ModManifest.UniqueID });
            }

            // The rest of the whistle logic is now in a delayed action to play after the animation starts.
            Game1.delayedActions.Add(new DelayedAction(250, () =>
            {
                this.PerformWhistle(Game1.player);
                if (Context.IsMultiplayer)
                {
                    this.Multiplayer.SendMessage(
                        new WhistleMessage(Game1.player.UniqueMultiplayerID),
                        "WhistleCommand",
                        modIDs: new[] { this.ModManifest.UniqueID }
                    );
                }
            }));
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
            if (!Context.IsWorldReady) return;

            // --- Paused NPC Countdown ---
            if (this._pausedNpcs.Any())
            {
                // This logic is simple: if an NPC is paused, we just re-apply Halt()
                // The timer is handled in the DelayedAction when they are paused.
                foreach (var npc in this._pausedNpcs)
                {
                    npc.Halt();
                }
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

                    // Get or create the annoyance state for this NPC.
                    if (!this.npcAnnoyanceTracker.TryGetValue(npc.Name, out var annoyanceState))
                    {
                        annoyanceState = new AnnoyanceState();
                        this.npcAnnoyanceTracker[npc.Name] = annoyanceState;
                    }

                    npc.facePlayer(whistler);
                    this.PauseNpc(npc, 1.5f);

                    // If we've already lost friendship today, just show the angry emote.
                    if (annoyanceState.HasLostFriendshipToday)
                    {
                        npc.doEmote(12); // Angry emote
                        continue;
                    }

                    // Otherwise, increment the whistle count.
                    annoyanceState.WhistleCount++;

                    if (annoyanceState.WhistleCount >= 3)
                    {
                        // Third whistle: lose friendship and get angry.
                        whistler.changeFriendship(-ModEntry.Config.WhistleFriendshipPenalty, npc);
                        npc.doEmote(12); // Angry emote
                        annoyanceState.HasLostFriendshipToday = true;
                    }
                    else
                    {
                        npc.doEmote(16); // ! emote
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
        private void PauseNpc(NPC npc, float durationSeconds)
        {
            if (this._pausedNpcs.Contains(npc)) return;

            this._pausedNpcs.Add(npc);
            npc.Halt();

            // Create a delayed action to un-pause the NPC later.
            Game1.delayedActions.Add(new DelayedAction((int)(durationSeconds * 1000), () =>
            {
                if (this._pausedNpcs.Contains(npc))
                {
                    this._pausedNpcs.Remove(npc);
                }
            }));
        }

        private void StartWhistleAnimation(Farmer who)
        {
            // If already performing an action, don't play the animation.
            if (!who.CanMove) return;

            who.faceDirection(2);
            who.jump(4);
            who.canMove = false;
            FarmerSprite.AnimationFrame[] frames = new FarmerSprite.AnimationFrame[]
            {
                new FarmerSprite.AnimationFrame(67, 100),
                new FarmerSprite.AnimationFrame(26, 100),
                new FarmerSprite.AnimationFrame(16, 100),
            };

            // After the animation is done, allow the player to move again.
            who.FarmerSprite.animateOnce(frames, (f) => f.canMove = true);
        }


        public void ResetState()
        {
            this._pausedNpcs.Clear();
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
}