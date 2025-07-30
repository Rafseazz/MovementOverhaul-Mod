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
        public bool JumpOverLargeStumps { get; set; } = false;
        public bool JumpOverLargeLogs { get; set; } = false;
        public bool JumpOverBoulders { get; set; } = false;
        public float HorseJumpPlayerBounce { get; set; } = 0.55f;
        public SprintMode SprintActivation { get; set; } = SprintMode.DoubleTap;
        public SButton SprintKey { get; set; } = SButton.LeftAlt;
        public float HorseSprintSpeedMultiplier { get; set; } = 2.0f;
        public float SprintSpeedMultiplier { get; set; } = 1.5f;
        public float SprintDurationSeconds { get; set; } = 1f;
        public float SprintStaminaCostPerSecond { get; set; } = 5f;
        public string SprintParticleEffect { get; set; } = "Smoke";
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

        public override void Entry(IModHelper helper)
        {
            Config = this.Helper.ReadConfig<ModConfig>();

            SprintLogic = new SprintLogic(helper, helper.Multiplayer, this.ModManifest);
            JumpLogic = new JumpLogic(helper, helper.Multiplayer, this.ModManifest);

            var harmony = new Harmony(this.ModManifest.UniqueID);
            harmony.PatchAll();

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

            this.HookUpJumpEvents();

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

            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => "Jump Settings");
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => "Instant Jump on Press", tooltip: () => "If enabled, jump immediately on key press. This disables the hold-to-charge mechanic.", getValue: () => ModEntry.Config.InstantJump, setValue: value => ModEntry.Config.InstantJump = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => "Charge Affects Jump Distance", tooltip: () => "If enabled (and Instant Jump is off), holding the jump button will also increase your jump distance.", getValue: () => Config.ChargeAffectsDistance, setValue: value => Config.ChargeAffectsDistance = value);
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => "Enable Jump", tooltip: () => "Toggles the jump ability on or off.", getValue: () => ModEntry.Config.EnableJump, setValue: value => ModEntry.Config.EnableJump = value);
            configMenu.AddKeybind(mod: this.ModManifest, name: () => "Jump Key", tooltip: () => "The key to press to jump.", getValue: () => ModEntry.Config.JumpKey, setValue: value => ModEntry.Config.JumpKey = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Jump Duration", tooltip: () => "The total time of the jump in frames. Lower is faster.", min: 10f, max: 60f, interval: 1f, getValue: () => ModEntry.Config.JumpDuration, setValue: value => ModEntry.Config.JumpDuration = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Jump Height", tooltip: () => "The height of the jump's arc.", min: 24f, max: 96f, interval: 1f, getValue: () => ModEntry.Config.JumpHeight, setValue: value => ModEntry.Config.JumpHeight = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Base Jump Distance", tooltip: () => "The distance (in tiles) of a running jump with no speed buffs.", min: 1.0f, max: 5.0f, interval: 0.1f, getValue: () => Config.NormalJumpDistance, setValue: value => Config.NormalJumpDistance = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Jump Distance Scale Factor", tooltip: () => "How much extra distance you get per point of speed.", min: 0.1f, max: 1.0f, interval: 0.1f, getValue: () => ModEntry.Config.JumpDistanceScaleFactor, setValue: value => ModEntry.Config.JumpDistanceScaleFactor = value);
            configMenu.AddTextOption(mod: this.ModManifest, name: () => "Jump Sound", tooltip: () => "The sound that plays when you jump.", getValue: () => ModEntry.Config.JumpSound, setValue: value => ModEntry.Config.JumpSound = value,
                allowedValues: new string[] { "dwoop", "jingle1", "stoneStep", "flameSpell", "boop", "coin" },
                formatAllowedValue: value => value switch { "dwoop" => "Classic Dwoop", "jingle1" => "Jingle", "stoneStep" => "Stone Step", "flameSpell" => "Whoosh", "boop" => "Boop", "coin" => "Coin", _ => value });

            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => "Jump Over Settings");
            configMenu.AddBoolOption(mod: this.ModManifest,
                name: () => "Jump Over Large Stumps",
                tooltip: () => "If enabled, allows you to jump over large stumps.",
                getValue: () => ModEntry.Config.JumpOverLargeStumps,
                setValue: value => ModEntry.Config.JumpOverLargeStumps = value
            );
            configMenu.AddBoolOption(mod: this.ModManifest,
                name: () => "Jump Over Large Logs",
                tooltip: () => "If enabled, allows you to jump over large logs.",
                getValue: () => ModEntry.Config.JumpOverLargeLogs,
                setValue: value => ModEntry.Config.JumpOverLargeLogs = value
            );
            configMenu.AddBoolOption(mod: this.ModManifest,
                name: () => "Jump Over Boulders",
                tooltip: () => "If enabled, allows you to jump over boulders.",
                getValue: () => ModEntry.Config.JumpOverBoulders,
                setValue: value => ModEntry.Config.JumpOverBoulders = value
            );

            configMenu.AddNumberOption(mod: this.ModManifest,
                name: () => "Rider Bounce Factor",
                tooltip: () => "How much the rider moves in the saddle during a horse jump. 1.0 = glued to saddle, 0.0 = max bounce.",
                getValue: () => ModEntry.Config.HorseJumpPlayerBounce,
                setValue: value => ModEntry.Config.HorseJumpPlayerBounce = value,
                min: 0.0f, max: 1.0f, interval: 0.05f
            );

            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => "Sprint Settings");
            configMenu.AddTextOption(mod: this.ModManifest,
                name: () => "Sprint Activation",
                tooltip: () => "How sprinting is activated.",
                getValue: () => Config.SprintActivation.ToString(),
                setValue: value => Config.SprintActivation = (SprintMode)Enum.Parse(typeof(SprintMode), value),
                allowedValues: new string[] { "DoubleTap", "Hold", "Toggle" }
            );

            configMenu.AddKeybind(mod: this.ModManifest,
                name: () => "Sprint Key",
                tooltip: () => "The key to hold or toggle for sprinting (only for Hold/Toggle modes).",
                getValue: () => Config.SprintKey,
                setValue: value => Config.SprintKey = value
            );
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Sprint Speed Multiplier", tooltip: () => "The speed multiplier to apply when sprinting. 2.0 means double speed.", min: 1.1f, max: 4.0f, interval: 0.1f, getValue: () => ModEntry.Config.SprintSpeedMultiplier, setValue: value => ModEntry.Config.SprintSpeedMultiplier = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Horse Sprint Speed Multiplier", tooltip: () => "The speed multiplier for sprinting while on your horse.", min: 1.1f, max: 4.0f, interval: 0.1f, getValue: () => Config.HorseSprintSpeedMultiplier, setValue: value => Config.HorseSprintSpeedMultiplier = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Sprint Duration (Seconds)", tooltip: () => "How long the sprint lasts in seconds (only for DoubleTap mode).", min: 1f, max: 10f, interval: 0.5f, getValue: () => ModEntry.Config.SprintDurationSeconds, setValue: value => ModEntry.Config.SprintDurationSeconds = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Stamina Cost Per Second", tooltip: () => "How much stamina is drained per second while sprinting.", min: 0f, max: 10f, interval: 0.5f, getValue: () => ModEntry.Config.SprintStaminaCostPerSecond, setValue: value => ModEntry.Config.SprintStaminaCostPerSecond = value);

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => "Sprint Particle Effect",
                tooltip: () => "Choose the particle effect that appears when you sprint.",
                getValue: () => ModEntry.Config.SprintParticleEffect,
                setValue: value => ModEntry.Config.SprintParticleEffect = value,
                allowedValues: new string[] { "Smoke", "GreenDust", "Circular", "Leaves", "Fire1", "Fire2", "BlueFire", "Stars", "Water Splash", "Poison", "None" },
                formatAllowedValue: value => value switch
                {
                    "Smoke" => "Smoke - Puffy smoke.",
                    "GreenDust" => "GreenDust - Shiny green dust.",
                    "Circular" => "Circular - Circular flat smoke trails.",
                    "Leaves" => "Leaves - Trail of leaves.",
                    "Fire1" => "Fire1 - Small flames.",
                    "Fire2" => "Fire2 - Another small flames.",
                    "BlueFire" => "BlueFire - Fire! But blue.",
                    "Stars" => "Stars - Sparkle with magical stars as you run.",
                    "Water Splash" => "Water Splash - Create a splash effect, great for the beach.",
                    "Poison" => "Poison - Ew.",
                    "None" => "None - No visual effect will be shown.",
                    _ => value
                }
            );
        }
    }
}