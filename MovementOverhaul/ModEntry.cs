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
    public class JumpMessage
    {
        public long PlayerID { get; set; }
        public JumpMessage() { }
        public JumpMessage(long playerID) { this.PlayerID = playerID; }
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
    public enum SprintMode { DoubleTap, Hold, Toggle }
    public class ModConfig
    {
        public bool InstantJump { get; set; } = false;
        public bool ChargeAffectsDistance { get; set; } = true;
        public bool EnableJump { get; set; } = true;
        public SButton JumpKey { get; set; } = SButton.Space;
        public float JumpDuration { get; set; } = 30f;
        public float JumpHeight { get; set; } = 36f;
        public float NormalJumpDistance { get; set; } = 1.5f;
        public float JumpDistanceScaleFactor { get; set; } = 0.4f;
        public string JumpSound { get; set; } = "dwoop";
        public bool AmplifyJumpSound { get; set; } = false;
        public float JumpStaminaCost { get; set; } = 1f;
        public bool JumpOverLargeStumps { get; set; } = false;
        public bool JumpOverLargeLogs { get; set; } = false;
        public bool JumpOverBoulders { get; set; } = false;
        public float HorseJumpPlayerBounce { get; set; } = 0.55f;
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
        public float SitGroundRegenPerSecond { get; set; } = 3f;
        public float SitChairRegenPerSecond { get; set; } = 5f;
        public bool SocialSittingFriendship { get; set; } = true;
        public bool FireSittingBuff { get; set; } = true;
        public bool MeditateForBuff { get; set; } = false;
        public bool IdleSitEffects { get; set; } = false;
    }

    public enum JumpState { Idle, Jumping, Falling }

    public class ModEntry : Mod
    {
        public static ModConfig Config { get; private set; } = null!;
        public static bool IsHorseJumping { get; set; } = false;
        public static int CurrentHorseJumpYOffset { get; set; } = 0;
        public static Vector2 CurrentHorseJumpPosition { get; set; }
        public static float CurrentBounceFactor { get; set; } = 0.85f;

        public static SprintLogic SprintLogic { get; private set; } = null!;
        public static JumpLogic JumpLogic { get; private set; } = null!;
        public static SitLogic SitLogic { get; private set; } = null!;

        public override void Entry(IModHelper helper)
        {
            Config = this.Helper.ReadConfig<ModConfig>();

            SprintLogic = new SprintLogic(helper, helper.Multiplayer, this.ModManifest);
            JumpLogic = new JumpLogic(helper, helper.Multiplayer, this.ModManifest);
            SitLogic = new SitLogic(helper, this.Monitor, helper.Multiplayer, this.ModManifest);

            var harmony = new Harmony(this.ModManifest.UniqueID);
            harmony.PatchAll();

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

            this.HookUpJumpEvents();

            helper.Events.Input.ButtonPressed += SitLogic.OnButtonPressed;
            helper.Events.GameLoop.UpdateTicked += SitLogic.OnUpdateTicked;

            helper.Events.GameLoop.UpdateTicked += JumpLogic.OnUpdateTicked;

            helper.Events.Input.ButtonPressed += SprintLogic.OnButtonPressed;
            helper.Events.GameLoop.UpdateTicked += SprintLogic.OnUpdateTicked;

            helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
        }

        private void UnhookJumpEvents()
        {
            this.Helper.Events.Input.ButtonPressed -= this.OnButtonPressed_Instant_Wrapper;
            this.Helper.Events.Input.ButtonPressed -= JumpLogic.OnButtonPressed_Charge;
            this.Helper.Events.Input.ButtonReleased -= JumpLogic.OnButtonReleased_Jump;
        }

        private void HookUpJumpEvents()
        {
            if (ModEntry.Config.InstantJump)
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

            if (e.Type == "PlayerJumped")
            {
                var msg = e.ReadAs<JumpMessage>();
                JumpLogic.TriggerRemoteJump(msg.PlayerID);
            }
            else if (e.Type == "CreateSprintParticle")
            {
                var msg = e.ReadAs<SprintParticleMessage>();
                SprintLogic.CreateRemoteParticle(msg.PlayerID, msg.ParticleType);
            }
            else if (e.Type == "SitStateChanged")
            {
                var msg = e.ReadAs<SitStateMessage>();
                Farmer? farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == msg.PlayerID);
                if (farmer != null)
                {
                    if (msg.IsSitting)
                    {
                        farmer.canMove = false;
                        farmer.completelyStopAnimatingOrDoingAction();
                        farmer.faceDirection(msg.Direction);
                        farmer.FarmerSprite.setCurrentFrame(msg.Frame);
                        farmer.flip = msg.IsFlipped;
                        farmer.yJumpOffset = msg.YOffset;
                    }
                    else
                    {
                        farmer.canMove = true;
                        farmer.FarmerSprite.StopAnimation();
                        farmer.flip = false;
                        farmer.yJumpOffset = 0;
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

            // --- JUMP SETTINGS ---
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
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-distance-scale-factor.name"), tooltip: () => this.Helper.Translation.Get("config.jump-distance-scale-factor.tooltip"), min: 0.1f, max: 1.0f, interval: 0.1f, getValue: () => Config.JumpDistanceScaleFactor, setValue: value => Config.JumpDistanceScaleFactor = value);
            configMenu.AddTextOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-sound.name"), tooltip: () => this.Helper.Translation.Get("config.jump-sound.tooltip"), getValue: () => Config.JumpSound, setValue: value => Config.JumpSound = value,
                allowedValues: new string[] { "dwoop", "jingle1", "stoneStep", "flameSpell", "boop", "coin" },
                formatAllowedValue: value => this.Helper.Translation.Get($"config.jump-sound.value.{value}", new { defaultValue = value })
            );
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.amplify-jump-sound.name"), tooltip: () => this.Helper.Translation.Get("config.amplify-jump-sound.tooltip"), getValue: () => Config.AmplifyJumpSound, setValue: value => Config.AmplifyJumpSound = value);

            // JUMP OVER SETTINGS
            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.jump-over.title"));
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-over-stumps.name"), tooltip: () => this.Helper.Translation.Get("config.jump-over-stumps.tooltip"), getValue: () => Config.JumpOverLargeStumps, setValue: value => Config.JumpOverLargeStumps = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-over-logs.name"), tooltip: () => this.Helper.Translation.Get("config.jump-over-logs.tooltip"), getValue: () => Config.JumpOverLargeLogs, setValue: value => Config.JumpOverLargeLogs = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => this.Helper.Translation.Get("config.jump-over-boulders.name"), tooltip: () => this.Helper.Translation.Get("config.jump-over-boulders.tooltip"), getValue: () => Config.JumpOverBoulders, setValue: value => Config.JumpOverBoulders = value);
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
        }
    }
}