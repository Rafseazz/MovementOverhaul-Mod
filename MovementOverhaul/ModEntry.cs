using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using HarmonyLib;

public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
    void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);
    void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    void AddNumberOption(IManifest mod, Func<float> getValue, Action<float> setValue, Func<string> name, Func<string>? tooltip = null, float? min = null, float? max = null, float? interval = null, Func<float, string>? formatValue = null, string? fieldId = null);
    void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string>? tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int, string>? formatValue = null, string? fieldId = null);
    void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string>? tooltip = null, string[]? allowedValues = null, Func<string, string>? formatAllowedValue = null, string? fieldId = null);
    void AddKeybind(IManifest mod, Func<SButton> getValue, Action<SButton> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
}


namespace MovementOverhaul
{
    public class FullJumpSyncMessage
    {
        public long PlayerID { get; set; }
        public Vector2 StartPosition { get; set; }
        public Vector2 TargetPosition { get; set; }
        public float JumpDuration { get; set; }
        public float JumpHeight { get; set; }
        public bool IsHorseJump { get; set; }

        public FullJumpSyncMessage() { } // Parameterless constructor for SMAPI
        public FullJumpSyncMessage(long id, Vector2 start, Vector2 target, float duration, float height, bool isHorse)
        {
            this.PlayerID = id;
            this.StartPosition = start;
            this.TargetPosition = target;
            this.JumpDuration = duration;
            this.JumpHeight = height;
            this.IsHorseJump = isHorse;
        }
    }

    public class SprintParticleMessage
    {
        public long PlayerID { get; set; }
        public string ParticleType { get; set; } = "";
        public SprintParticleMessage() { }
        public SprintParticleMessage(long playerID, string particleType)
        {
            this.PlayerID = playerID;
            this.ParticleType = particleType;
        }
    }

    public class SitStateMessage
    {
        public long PlayerID { get; set; }
        public int Frame { get; set; }
        public int Direction { get; set; }
        public bool IsFlipped { get; set; }
        public int YOffset { get; set; }
        public bool IsSitting { get; set; }

        public SitStateMessage() { }
        public SitStateMessage(long id, bool isSitting, int frame, int direction, bool isFlipped, int yOffset)
        {
            this.PlayerID = id;
            this.IsSitting = isSitting;
            this.Frame = frame;
            this.Direction = direction;
            this.IsFlipped = isFlipped;
            this.YOffset = yOffset;
        }
    }
    public class DashAttackMessage
    {
        public long PlayerID { get; set; }
        public bool IsStarting { get; set; }
        public Vector2 Direction { get; set; }

        public DashAttackMessage() { }
        public DashAttackMessage(long id, bool isStarting, Vector2 direction)
        {
            this.PlayerID = id;
            this.IsStarting = isStarting;
            this.Direction = direction;
        }
    }
    public class WhistleMessage
    {
        public long PlayerID { get; set; } // The ID of the player who whistled

        public WhistleMessage() { }
        public WhistleMessage(long playerID) { this.PlayerID = playerID; }
    }
    public class WhistleAnimationMessage
    {
        public long PlayerID { get; set; }

        public WhistleAnimationMessage() { }
        public WhistleAnimationMessage(long playerID) { this.PlayerID = playerID; }
    }

    public enum SprintMode { DoubleTap, Hold, Toggle }
    public class ModConfig
    {
        public bool DebugMode { get; set; } = false;
        public bool InstantJump { get; set; } = false;
        public bool ChargeAffectsDistance { get; set; } = true;
        public bool EnableJump { get; set; } = true;
        public SButton JumpKey { get; set; } = SButton.Space;
        public float JumpDuration { get; set; } = 30f;
        public float JumpHeight { get; set; } = 36f;
        public float NormalJumpDistance { get; set; } = 1.5f;
        public float JumpDistanceScaleFactor { get; set; } = 0.5f;
        public string JumpSound { get; set; } = "pickUpItem";
        public bool AmplifyJumpSound { get; set; } = false;
        public float JumpStaminaCost { get; set; } = 0f;
        public bool JumpOverLargeStumps { get; set; } = false;
        public bool JumpOverLargeLogs { get; set; } = false;
        public bool JumpOverBoulders { get; set; } = false;
        public bool JumpOverTrashCans { get; set; } = true;
        public bool JumpThroughNPCs { get; set; } = true;
        public bool JumpThroughPlayers { get; set; } = true;
        public float HorseJumpPlayerBounce { get; set; } = 0.55f;
        public bool NoStaminaDrainOnHorse { get; set; } = false;
        public bool HopOverAnything { get; set; } = false;
        public bool EnableSprint { get; set; } = true;
        public SprintMode SprintActivation { get; set; } = SprintMode.DoubleTap;
        public SButton SprintKey { get; set; } = SButton.LeftAlt;
        public float HorseSprintSpeedMultiplier { get; set; } = 2.0f;
        public float SprintSpeedMultiplier { get; set; } = 1.5f;
        public float SprintDurationSeconds { get; set; } = 1f;
        public float SprintStaminaCostPerSecond { get; set; } = 5f;
        public string SprintParticleEffect { get; set; } = "Smoke";
        public bool PathSpeedBonus { get; set; } = true;
        public float PathSpeedBonusMultiplier { get; set; } = 1.15f;
        public bool EnableSit { get; set; } = true;
        public SButton SitKey { get; set; } = SButton.OemPeriod;
        public float SitRegenDelaySeconds { get; set; } = 1.5f;
        public float SitGroundRegenPerSecond { get; set; } = 5f;
        public float SitChairRegenPerSecond { get; set; } = 8f;
        public bool SocialSittingFriendship { get; set; } = true;
        public bool FireSittingBuff { get; set; } = true;
        public bool MeditateForBuff { get; set; } = false;
        public bool IdleSitEffects { get; set; } = true;
        public bool RegenStaminaOnWalk { get; set; } = true;
        public float WalkRegenPerSecond { get; set; } = 1f;
        public bool RegenStaminaOnStand { get; set; } = true;
        public float StandRegenPerSecond { get; set; } = 2f;
        public float WalkStandRegenDelaySeconds { get; set; } = 1.5f;
        public bool SmootherTurningAnimation { get; set; } = false;
        public bool AdaptiveAnimationSpeed { get; set; } = true;
        public float AnimationExaggerationFactor { get; set; } = 1f;
        public bool EnableDashAttack { get; set; } = true;
        public float DashAttackDamageMultiplier { get; set; } = 1.25f; // 25% damage bonus
        public float DashAttackStaminaCost { get; set; } = 5f;
        public bool EnableDashAttackCooldown { get; set; } = true;
        public float SwordDashCooldown { get; set; } = 1.5f;
        public float DaggerDashCooldown { get; set; } = 0.5f;
        public float ClubDashCooldown { get; set; } = 3f;
        public bool EnableJumpAttack { get; set; } = true;
        public float JumpAttackDamageMultiplier { get; set; } = 1.5f; // 50% damage bonus
        public float SprintAttackGracePeriod { get; set; } = 0.25f; // Default to 250ms
        public string DashAttackParticleEffect { get; set; } = "Smoke";
        public bool LaggingDashParticles { get; set; } = true;
        public bool EnableWhistle { get; set; } = true;
        public SButton WhistleKey { get; set; } = SButton.OemComma;
        public int WhistleAnimalMinHearts { get; set; } = 3;
        public bool WhistleAggrosMonsters { get; set; } = false;
        public bool HearRemoteWhistles { get; set; } = true;
        public float NPCPauseFromWhistle { get; set; } = 1.5f;
        public bool WhistleAnnoysNPCs { get; set; } = true;
        public int WhistleNumberBeforeAnnoying { get; set; } = 5;
        public int WhistleFriendshipPenalty { get; set; } = 10;
        public bool EnableRunningLate { get; set; } = true;
        public int DistanceConsideredFar { get; set; } = 100;
    }

    public enum JumpState { Idle, Jumping, Falling }

    public class ModEntry : Mod
    {
        // A dictionary to track the sitting state of all remote players.
        private static readonly Dictionary<long, SitStateMessage> _remoteSitters = new();
        public static IMonitor SMonitor { get; private set; } = null!;
        public static ModEntry Instance { get; private set; } = null!;
        public ModConfig Config { get; private set; } = null!;
        public static bool IsHorseJumping { get; set; } = false;
        public static int CurrentHorseJumpYOffset { get; set; } = 0;
        public static Vector2 CurrentHorseJumpPosition { get; set; }
        public static float CurrentBounceFactor { get; set; } = 0.85f;

        public static SprintLogic SprintLogic { get; private set; } = null!;
        public static JumpLogic JumpLogic { get; private set; } = null!;
        public static SitLogic SitLogic { get; private set; } = null!;
        public static WalkStandLogic WalkStandLogic { get; private set; } = null!;
        public static AnimationLogic AnimationLogic { get; private set; } = null!;
        public static CombatLogic CombatLogic { get; private set; } = null!;
        public static NpcLogic NpcLogic { get; private set; } = null!;
        
        // Debugger
        public void LogDebug(string message)
        {
            if (!this.Config.DebugMode)
            {
                return;
            }
            this.Monitor.Log(message, LogLevel.Debug);
        }

        public override void Entry(IModHelper helper)
        {
            SMonitor = this.Monitor;
            Instance = this;
            Config = this.Helper.ReadConfig<ModConfig>();

            SprintLogic = new SprintLogic(helper, helper.Multiplayer, this.ModManifest);
            JumpLogic = new JumpLogic(helper, helper.Multiplayer, this.ModManifest);
            SitLogic = new SitLogic(helper, this.Monitor, helper.Multiplayer, this.ModManifest);
            WalkStandLogic = new WalkStandLogic(helper);
            AnimationLogic = new AnimationLogic(helper);
            CombatLogic = new CombatLogic(helper, this.Monitor, helper.Multiplayer, this.ModManifest);
            NpcLogic = new NpcLogic(helper, this.Monitor, helper.Multiplayer, this.ModManifest);

            var harmony = new Harmony(this.ModManifest.UniqueID);
            harmony.PatchAll();

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

            this.HookUpJumpEvents();

            helper.Events.Input.ButtonPressed += this.OnButtonPressed_Combat_CooldownCheck;

            helper.Events.Input.ButtonPressed += SitLogic.OnButtonPressed;
            helper.Events.GameLoop.UpdateTicked += SitLogic.OnUpdateTicked;
            helper.Events.GameLoop.UpdateTicking += JumpLogic.OnUpdateTicking;
            helper.Events.GameLoop.UpdateTicked += WalkStandLogic.OnUpdateTicked;
            helper.Events.Input.ButtonPressed += SprintLogic.OnButtonPressed;
            helper.Events.GameLoop.UpdateTicked += SprintLogic.OnUpdateTicked;
            helper.Events.GameLoop.UpdateTicked += AnimationLogic.OnUpdateTicked;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked_SyncRemoteStates;
            helper.Events.GameLoop.UpdateTicked += CombatLogic.OnUpdateTicked;
            helper.Events.Input.ButtonPressed += NpcLogic.OnButtonPressed;
            helper.Events.GameLoop.UpdateTicked += NpcLogic.OnUpdateTicked;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;

            helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;

        }
        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            NpcLogic.ResetState();
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            NpcLogic.ResetState();
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            NpcLogic.ResetDailyState();
        }

        // A high-priority wrapper to check for cooldowns before any other action.
        [EventPriority(EventPriority.High)]
        private void OnButtonPressed_Combat_CooldownCheck(object? sender, ButtonPressedEventArgs e)
        {
            if (CombatLogic.CheckAndBlockCooldown(e))
            {
                this.Helper.Input.Suppress(e.Button);
            }
            else
            {
                ModEntry.CombatLogic.HandleDashAttackInput(e);
            }
        }

        public static Vector2 GetDirectionVectorFromFacing(int facingDirection)
        {
            return facingDirection switch
            {
                0 => new Vector2(0, -1), // Up
                1 => new Vector2(1, 0),  // Right
                2 => new Vector2(0, 1),  // Down
                3 => new Vector2(-1, 0), // Left
                _ => Vector2.Zero,
            };
        }

        private void UnhookJumpEvents()
        {
            this.Helper.Events.Input.ButtonPressed -= this.OnButtonPressed_Instant_Wrapper;
            this.Helper.Events.Input.ButtonPressed -= JumpLogic.OnButtonPressed_Charge;
            this.Helper.Events.Input.ButtonReleased -= JumpLogic.OnButtonReleased_Jump;
        }

        private void HookUpJumpEvents()
        {

            if (ModEntry.Instance.Config.InstantJump)
            {
                this.Helper.Events.Input.ButtonPressed += this.OnButtonPressed_Instant_Wrapper;
            }
            else
            {
                this.Helper.Events.Input.ButtonPressed += JumpLogic.OnButtonPressed_Charge;
                this.Helper.Events.Input.ButtonReleased += JumpLogic.OnButtonReleased_Jump;
            }
        }

        [EventPriority(EventPriority.High)]
        private void OnButtonPressed_Instant_Wrapper(object? sender, ButtonPressedEventArgs e)
        {
            if (JumpLogic.OnButtonPressed_Instant(e))
            {
                this.Helper.Input.Suppress(e.Button);
            }
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != this.ModManifest.UniqueID || e.FromPlayerID == Game1.player.UniqueMultiplayerID)
                return;

            if (e.Type == "FullJumpSync") // For on-foot jumps
            {
                var msg = e.ReadAs<FullJumpSyncMessage>();
                JumpLogic.StartRemoteJump(msg);
            }
            else if (e.Type == "CreateSprintParticle")
            {
                var msg = e.ReadAs<SprintParticleMessage>();
                SprintLogic.CreateRemoteParticle(msg.PlayerID, msg.ParticleType);
            }
            else if (e.Type == "SitStateChanged")
            {
                var msg = e.ReadAs<SitStateMessage>();
                if (msg.IsSitting)
                {
                    // A remote player is sitting. Store their state.
                    _remoteSitters[msg.PlayerID] = msg;
                }
                else
                {
                    // A remote player stood up. Remove their state and fix their animation.
                    if (_remoteSitters.ContainsKey(msg.PlayerID))
                    {
                        _remoteSitters.Remove(msg.PlayerID);
                    }

                    Farmer? farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == msg.PlayerID);
                    if (farmer != null)
                    {
                        farmer.canMove = true;
                        farmer.FarmerSprite.StopAnimation();
                        farmer.flip = false;
                        farmer.yJumpOffset = 0;
                        farmer.FarmerSprite.setCurrentFrame(0);
                    }
                }
            }
            else if (e.Type == "DashAttackStateChanged")
            {
                var msg = e.ReadAs<DashAttackMessage>();
                CombatLogic.HandleRemoteDashState(msg);
            }
            else if (e.Type == "CreateDashParticleBurst")
            {
                var msg = e.ReadAs<SprintParticleMessage>();
                CombatLogic.CreateRemoteDashParticleBurst(msg.PlayerID, msg.ParticleType);
            }
            else if (e.Type == "WhistleCommand")
            {
                var msg = e.ReadAs<WhistleMessage>();
                NpcLogic.HandleRemoteWhistle(msg.PlayerID);
            }
            else if (e.Type == "PlayWhistleAnimation")
            {
                var msg = e.ReadAs<WhistleAnimationMessage>();
                NpcLogic.HandleRemoteWhistleAnimation(msg.PlayerID);
            }
        }

        // A dedicated update loop to enforce remote player animations.
        private void OnUpdateTicked_SyncRemoteStates(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsMultiplayer) return;

            foreach (var sitter in _remoteSitters)
            {
                Farmer? farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == sitter.Key);
                if (farmer != null)
                {
                    var state = sitter.Value;
                    farmer.canMove = false;
                    farmer.completelyStopAnimatingOrDoingAction();
                    farmer.faceDirection(state.Direction);
                    farmer.showFrame(state.Frame, state.IsFlipped);

                    // If the jump logic is NOT handling this farmer's jump, then apply the y-offset from the sit state.
                    // Otherwise, let JumpLogic control the y-offset to prevent conflict.
                    if (!JumpLogic.IsPlayerJumping(farmer.UniqueMultiplayerID))
                    {
                        farmer.yJumpOffset = state.YOffset;
                    }
                }
            }
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null) return;

            configMenu.Register(
                mod: this.ModManifest,
                reset: () => Config = new ModConfig(),
                save: () =>
                {
                    this.Helper.WriteConfig(Config);
                    this.UnhookJumpEvents();
                    this.HookUpJumpEvents();
                }
            );
            // Debug Mode
            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.debug.title"));
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.enable-debug.name"), tooltip: () => this.Helper.Translation.Get("config.enable-debug.tooltip"), getValue: () => Config.DebugMode, setValue: value => Config.DebugMode = value);

            // JUMP SETTINGS
            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.jump.title"));

            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.instant-jump.name"), tooltip: () => this.Helper.Translation.Get("config.instant-jump.tooltip"), getValue: () => Config.InstantJump, setValue: value => Config.InstantJump = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.charge-affects-distance.name"), tooltip: () => this.Helper.Translation.Get("config.charge-affects-distance.tooltip"), getValue: () => Config.ChargeAffectsDistance, setValue: value => Config.ChargeAffectsDistance = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.enable-jump.name"), tooltip: () => this.Helper.Translation.Get("config.enable-jump.tooltip"), getValue: () => Config.EnableJump, setValue: value => Config.EnableJump = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.hop-over-anything.name"), tooltip: () => this.Helper.Translation.Get("config.hop-over-anything.tooltip"), getValue: () => Config.HopOverAnything, setValue: value => Config.HopOverAnything = value);
            configMenu.AddKeybind(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-key.name"), tooltip: () => this.Helper.Translation.Get("config.jump-key.tooltip"), getValue: () => Config.JumpKey, setValue: value => Config.JumpKey = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-stamina-cost.name"), tooltip: () => this.Helper.Translation.Get("config.jump-stamina-cost.tooltip"), getValue: () => Config.JumpStaminaCost, setValue: value => Config.JumpStaminaCost = value, min: 0f, max: 10f, interval: 0.5f);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-duration.name"), tooltip: () => this.Helper.Translation.Get("config.jump-duration.tooltip"), min: 10f, max: 60f, interval: 1f, getValue: () => Config.JumpDuration, setValue: value => Config.JumpDuration = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-height.name"), tooltip: () => this.Helper.Translation.Get("config.jump-height.tooltip"), min: 24f, max: 96f, interval: 1f, getValue: () => Config.JumpHeight, setValue: value => Config.JumpHeight = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.base-jump-distance.name"), tooltip: () => this.Helper.Translation.Get("config.base-jump-distance.tooltip"), min: 1.0f, max: 5.0f, interval: 0.1f, getValue: () => Config.NormalJumpDistance, setValue: value => Config.NormalJumpDistance = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-distance-scale-factor.name"), tooltip: () => this.Helper.Translation.Get("config.jump-distance-scale-factor.tooltip"), min: 0.1f, max: 2.0f, interval: 0.1f, getValue: () => Config.JumpDistanceScaleFactor, setValue: value => Config.JumpDistanceScaleFactor = value);
            configMenu.AddTextOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-sound.name"), tooltip: () => this.Helper.Translation.Get("config.jump-sound.tooltip"), getValue: () => Config.JumpSound, setValue: value => Config.JumpSound = value,
                allowedValues: new string[] { "pickUpItem", "dwoop", "jingle1", "stoneStep", "flameSpell", "boop", "coin"},
                formatAllowedValue: value => this.Helper.Translation.Get($"config.jump-sound.value.{value}", new { defaultValue = value })
            );
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.amplify-jump-sound.name"), tooltip: () => this.Helper.Translation.Get("config.amplify-jump-sound.tooltip"), getValue: () => Config.AmplifyJumpSound, setValue: value => Config.AmplifyJumpSound = value);

            // JUMP OVER SETTINGS
            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.jump-over.title"));
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-over-stumps.name"), tooltip: () => this.Helper.Translation.Get("config.jump-over-stumps.tooltip"), getValue: () => Config.JumpOverLargeStumps, setValue: value => Config.JumpOverLargeStumps = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-over-logs.name"), tooltip: () => this.Helper.Translation.Get("config.jump-over-logs.tooltip"), getValue: () => Config.JumpOverLargeLogs, setValue: value => Config.JumpOverLargeLogs = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-over-boulders.name"), tooltip: () => this.Helper.Translation.Get("config.jump-over-boulders.tooltip"), getValue: () => Config.JumpOverBoulders, setValue: value => Config.JumpOverBoulders = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-over-trashcans.name"), tooltip: () => this.Helper.Translation.Get("config.jump-over-trashcans.tooltip"), getValue: () => Config.JumpOverTrashCans, setValue: value => Config.JumpOverTrashCans = value);
            configMenu.AddBoolOption(mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.jump-thru-NPCs.name"),
                tooltip: () => this.Helper.Translation.Get("config.jump-thru-NPCs.tooltip"),
                getValue: () => Config.JumpThroughNPCs,
                setValue: value => Config.JumpThroughNPCs = value);
            configMenu.AddBoolOption(mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.jump-thru-Farmers.name"),
                tooltip: () => this.Helper.Translation.Get("config.jump-thru-Farmers.tooltip"),
                getValue: () => Config.JumpThroughPlayers,
                setValue: value => Config.JumpThroughPlayers = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.rider-bounce.name"), tooltip: () => this.Helper.Translation.Get("config.rider-bounce.tooltip"), getValue: () => Config.HorseJumpPlayerBounce, setValue: value => Config.HorseJumpPlayerBounce = value, min: 0.0f, max: 1.0f, interval: 0.05f);

            // SPRINT SETTINGS
            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.sprint.title"));
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.enable-sprint.name"), tooltip: () => this.Helper.Translation.Get("config.enable-sprint.tooltip"), getValue: () => Config.EnableSprint, setValue: value => Config.EnableSprint = value);
            configMenu.AddTextOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.sprint-activation.name"), tooltip: () => this.Helper.Translation.Get("config.sprint-activation.tooltip"), getValue: () => Config.SprintActivation.ToString(), setValue: value => Config.SprintActivation = (SprintMode)Enum.Parse(typeof(SprintMode), value), allowedValues: new string[] { "DoubleTap", "Hold", "Toggle" });
            configMenu.AddKeybind(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.sprint-key.name"), tooltip: () => this.Helper.Translation.Get("config.sprint-key.tooltip"), getValue: () => Config.SprintKey, setValue: value => Config.SprintKey = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.sprint-duration.name"), tooltip: () => this.Helper.Translation.Get("config.sprint-duration.tooltip"), min: 1f, max: 10f, interval: 0.5f, getValue: () => Config.SprintDurationSeconds, setValue: value => Config.SprintDurationSeconds = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.sprint-speed.name"), tooltip: () => this.Helper.Translation.Get("config.sprint-speed.tooltip"), min: 1.1f, max: 4.0f, interval: 0.1f, getValue: () => Config.SprintSpeedMultiplier, setValue: value => Config.SprintSpeedMultiplier = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.horse-sprint-speed.name"), tooltip: () => this.Helper.Translation.Get("config.horse-sprint-speed.tooltip"), min: 1.1f, max: 4.0f, interval: 0.1f, getValue: () => Config.HorseSprintSpeedMultiplier, setValue: value => Config.HorseSprintSpeedMultiplier = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.sprint-stamina-cost.name"), tooltip: () => this.Helper.Translation.Get("config.sprint-stamina-cost.tooltip"), min: 0f, max: 10f, interval: 0.5f, getValue: () => Config.SprintStaminaCostPerSecond, setValue: value => Config.SprintStaminaCostPerSecond = value);
            configMenu.AddTextOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.sprint-particles.name"), tooltip: () => this.Helper.Translation.Get("config.sprint-particles.tooltip"), getValue: () => Config.SprintParticleEffect, setValue: value => Config.SprintParticleEffect = value,
                allowedValues: new string[] { "Smoke", "GreenDust", "Circular", "Leaves", "Fire1", "Fire2", "BlueFire", "Stars", "Water Splash", "Poison", "None" },
                formatAllowedValue: value => this.Helper.Translation.Get($"config.sprint-particles.value.{value}", new { defaultValue = value })
            );
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.horse-sprint-stamina.name"), tooltip: () => this.Helper.Translation.Get("config.horse-sprint-stamina.tooltip"), getValue: () => Config.NoStaminaDrainOnHorse, setValue: value => Config.NoStaminaDrainOnHorse = value
            );

            // PATH SPEED BONUS
            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.path-speed.title"));
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.path-speed-enable.name"), tooltip: () => this.Helper.Translation.Get("config.path-speed-enable.tooltip"), getValue: () => Config.PathSpeedBonus, setValue: value => Config.PathSpeedBonus = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.path-speed-multiplier.name"), tooltip: () => this.Helper.Translation.Get("config.path-speed-multiplier.tooltip"), getValue: () => Config.PathSpeedBonusMultiplier, setValue: value => Config.PathSpeedBonusMultiplier = value, min: 1.0f, max: 2.0f, interval: 0.05f);

            // SIT SETTINGS
            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.sit.title"));
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.enable-sit.name"), tooltip: () => this.Helper.Translation.Get("config.enable-sit.tooltip"), getValue: () => Config.EnableSit, setValue: value => Config.EnableSit = value);
            configMenu.AddKeybind(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.sit-key.name"), tooltip: () => this.Helper.Translation.Get("config.sit-key.tooltip"), getValue: () => Config.SitKey, setValue: value => Config.SitKey = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.sit-regen-delay.name"), tooltip: () => this.Helper.Translation.Get("config.sit-regen-delay.tooltip"), min: 0f, max: 5f, interval: 0.5f, getValue: () => Config.SitRegenDelaySeconds, setValue: value => Config.SitRegenDelaySeconds = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.ground-regen-rate.name"), tooltip: () => this.Helper.Translation.Get("config.ground-regen-rate.tooltip"), min: 1f, max: 10f, interval: 0.5f, getValue: () => Config.SitGroundRegenPerSecond, setValue: value => Config.SitGroundRegenPerSecond = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.chair-regen-rate.name"), tooltip: () => this.Helper.Translation.Get("config.chair-regen-rate.tooltip"), min: 1f, max: 15f, interval: 0.5f, getValue: () => Config.SitChairRegenPerSecond, setValue: value => Config.SitChairRegenPerSecond = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.social-sitting.name"), tooltip: () => this.Helper.Translation.Get("config.social-sitting.tooltip"), getValue: () => Config.SocialSittingFriendship, setValue: value => Config.SocialSittingFriendship = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.warming-by-fire.name"), tooltip: () => this.Helper.Translation.Get("config.warming-by-fire.tooltip"), getValue: () => Config.FireSittingBuff, setValue: value => Config.FireSittingBuff = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.meditate-for-buff.name"), tooltip: () => this.Helper.Translation.Get("config.meditate-for-buff.tooltip"), getValue: () => Config.MeditateForBuff, setValue: value => Config.MeditateForBuff = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.idle-sit-effects.name"), tooltip: () => this.Helper.Translation.Get("config.idle-sit-effects.tooltip"), getValue: () => Config.IdleSitEffects, setValue: value => Config.IdleSitEffects = value);

            // WALK & STAND SETTINGS
            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.walkstand.title"));
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.enable-walk.name"), tooltip: () => this.Helper.Translation.Get("config.enable-walk.tooltip"), getValue: () => Config.RegenStaminaOnWalk, setValue: value => Config.RegenStaminaOnWalk = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.walking-regen.name"), tooltip: () => this.Helper.Translation.Get("config.walking-regen.tooltip"), min: 0.1f, max: 5.0f, interval: 0.1f, getValue: () => Config.WalkRegenPerSecond, setValue: value => Config.WalkRegenPerSecond = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.enable-stand.name"), tooltip: () => this.Helper.Translation.Get("config.enable-stand.tooltip"), getValue: () => Config.RegenStaminaOnStand, setValue: value => Config.RegenStaminaOnStand = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.standing-regen.name"), tooltip: () => this.Helper.Translation.Get("config.standing-regen.tooltip"), min: 0.1f, max: 5.0f, interval: 0.1f, getValue: () => Config.StandRegenPerSecond, setValue: value => Config.StandRegenPerSecond = value);
            configMenu.AddNumberOption(mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.walkstand-regen-delay.name"),
                tooltip: () => this.Helper.Translation.Get("config.walkstand-regen-delay.tooltip"),
                min: 0f, max: 5f, interval: 0.5f,
                getValue: () => Config.WalkStandRegenDelaySeconds,
                setValue: value => Config.WalkStandRegenDelaySeconds = value);

            // ANIMATION SMOOTHING SETTINGS
            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.animation.title"));
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.enable-turning.name"), tooltip: () => this.Helper.Translation.Get("config.enable-turning.tooltip"), getValue: () => Config.SmootherTurningAnimation, setValue: value => Config.SmootherTurningAnimation = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.enable-animspeed.name"), tooltip: () => this.Helper.Translation.Get("config.enable-animspeed.tooltip"), getValue: () => Config.AdaptiveAnimationSpeed, setValue: value => Config.AdaptiveAnimationSpeed = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.animation-exag-factor.name"), tooltip: () => this.Helper.Translation.Get("config.animation-exag-factor.tooltip"), min: 0.1f, max: 5.0f, interval: 0.1f, getValue: () => Config.AnimationExaggerationFactor, setValue: value => Config.AnimationExaggerationFactor = value);

            // COMBAT SETTINGS
            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.combat-section.title"));
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.enable-dash.name"), tooltip: () => this.Helper.Translation.Get("config.enable-dash.tooltip"), getValue: () => Config.EnableDashAttack, setValue: value => Config.EnableDashAttack = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.dash-stamina.name"), tooltip: () => this.Helper.Translation.Get("config.dash-stamina.tooltip"), min: 0f, max: 20f, interval: 1f, getValue: () => Config.DashAttackStaminaCost, setValue: value => Config.DashAttackStaminaCost = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.dash-damage.name"), tooltip: () => this.Helper.Translation.Get("config.dash-damage.tooltip"), min: 1.0f, max: 3.0f, interval: 0.05f, getValue: () => Config.DashAttackDamageMultiplier, setValue: value => Config.DashAttackDamageMultiplier = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.dash-grace-period.name"), tooltip: () => this.Helper.Translation.Get("config.dash-grace-period.tooltip"), min: 0.1f, max: 0.5f, interval: 0.05f, getValue: () => Config.SprintAttackGracePeriod, setValue: value => Config.SprintAttackGracePeriod = value);
            configMenu.AddTextOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.dash-particles.name"), tooltip: () => this.Helper.Translation.Get("config.dash-particles.tooltip"), getValue: () => Config.DashAttackParticleEffect, setValue: value => Config.DashAttackParticleEffect = value, allowedValues: new string[] { "Smoke", "GreenDust", "Circular", "Leaves", "Fire1", "Fire2", "BlueFire", "Stars", "Water Splash", "Poison", "None" }, formatAllowedValue: value => this.Helper.Translation.Get($"config.sprint-particles.value.{value}", new { defaultValue = value }));
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.lagging-dash-particles.name"),
                tooltip: () => this.Helper.Translation.Get("config.lagging-dash-particles.tooltip"),
                getValue: () => Config.LaggingDashParticles,
                setValue: value => Config.LaggingDashParticles = value
            );
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.enable-dash-cooldown.name"), tooltip: () => this.Helper.Translation.Get("config.enable-dash-cooldown.tooltip"), getValue: () => Config.EnableDashAttackCooldown, setValue: value => Config.EnableDashAttackCooldown = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.sword-cooldown.name"), tooltip: () => this.Helper.Translation.Get("config.sword-cooldown.tooltip"), min: 0.5f, max: 10f, interval: 0.25f, getValue: () => Config.SwordDashCooldown, setValue: value => Config.SwordDashCooldown = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.dagger-cooldown.name"), tooltip: () => this.Helper.Translation.Get("config.dagger-cooldown.tooltip"), min: 0.25f, max: 10f, interval: 0.25f, getValue: () => Config.DaggerDashCooldown, setValue: value => Config.DaggerDashCooldown = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.club-hammer-cooldown.name"), tooltip: () => this.Helper.Translation.Get("config.club-hammer-cooldown.tooltip"), min: 1.0f, max: 10f, interval: 0.25f, getValue: () => Config.ClubDashCooldown, setValue: value => Config.ClubDashCooldown = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.enable-jump-attack.name"), tooltip: () => this.Helper.Translation.Get("config.enable-jump-attack.tooltip"), getValue: () => Config.EnableJumpAttack, setValue: value => Config.EnableJumpAttack = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-attack-damage.name"), tooltip: () => this.Helper.Translation.Get("config.jump-attack-damage.tooltip"), min: 1, max: 10, interval: 1, getValue: () => Config.JumpAttackDamageMultiplier, setValue: value => Config.JumpAttackDamageMultiplier = value);

            // WHISTLE
            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.whistle-section.title"));
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.enable-whistle.name"), tooltip: () => this.Helper.Translation.Get("config.enable-whistle.tooltip"), getValue: () => Config.EnableWhistle, setValue: value => Config.EnableWhistle = value);
            configMenu.AddKeybind(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.whistle-key.name"), tooltip: () => this.Helper.Translation.Get("config.whistle-key.tooltip"), getValue: () => Config.WhistleKey, setValue: value => Config.WhistleKey = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.animal-min-hearts.name"), tooltip: () => this.Helper.Translation.Get("config.animal-min-hearts.tooltip"), min: 0, max: 5, interval: 1, getValue: () => Config.WhistleAnimalMinHearts, setValue: value => Config.WhistleAnimalMinHearts = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.whistle-attracts-monsters.name"), tooltip: () => this.Helper.Translation.Get("config.whistle-attracts-monsters.tooltip"), getValue: () => Config.WhistleAggrosMonsters, setValue: value => Config.WhistleAggrosMonsters = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.hear-others-whistles.name"), tooltip: () => this.Helper.Translation.Get("config.hear-others-whistles.tooltip"), getValue: () => Config.HearRemoteWhistles, setValue: value => Config.HearRemoteWhistles = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.npc-pause-duration.name"), tooltip: () => this.Helper.Translation.Get("config.npc-pause-duration.tooltip"), min: 0f, max: 10f, interval: 0.5f, getValue: () => Config.NPCPauseFromWhistle, setValue: value => Config.NPCPauseFromWhistle = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.whistle-annoys-npcs.name"), tooltip: () => this.Helper.Translation.Get("config.whistle-annoys-npcs.tooltip"), getValue: () => Config.WhistleAnnoysNPCs, setValue: value => Config.WhistleAnnoysNPCs = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.whistle-tolerance.name"), tooltip: () => this.Helper.Translation.Get("config.whistle-tolerance.tooltip"), min: 0, max: 20, interval: 1, getValue: () => Config.WhistleNumberBeforeAnnoying, setValue: value => Config.WhistleNumberBeforeAnnoying = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.annoyance-penalty.name"), tooltip: () => this.Helper.Translation.Get("config.annoyance-penalty.tooltip"), min: 0, max: 50, interval: 5, getValue: () => Config.WhistleFriendshipPenalty, setValue: value => Config.WhistleFriendshipPenalty = value);

            // NPC MOVEMENT
            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.npc-movement-section.title"));
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.running-late.name"), tooltip: () => this.Helper.Translation.Get("config.running-late.tooltip"), getValue: () => Config.EnableRunningLate, setValue: value => Config.EnableRunningLate = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.far-distance.name"), tooltip: () => this.Helper.Translation.Get("config.far-distance.tooltip"), min: 0, max: 500, interval: 10, getValue: () => Config.DistanceConsideredFar, setValue: value => Config.DistanceConsideredFar = value);
        }
    }
}