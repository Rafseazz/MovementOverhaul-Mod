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
    public class ModConfig
    {
        public bool InstantJump { get; set; } = false;
        public bool EnableJump { get; set; } = true;
        public SButton JumpKey { get; set; } = SButton.Space;
        public float JumpDuration { get; set; } = 30f;
        public float JumpHeight { get; set; } = 48f;
        public int NormalJumpDistance { get; set; } = 1;
        public float JumpDistanceScaleFactor { get; set; } = 0.4f;
        public string JumpSound { get; set; } = "dwoop";
        public bool JumpOverLargeStumps { get; set; } = false;
        public bool JumpOverLargeLogs { get; set; } = false;
        public bool JumpOverBoulders { get; set; } = false;
        public float HorseJumpPlayerBounce { get; set; } = 0.55f;
        public float SprintSpeedMultiplier { get; set; } = 1.3f;
        public float SprintDurationSeconds { get; set; } = 1.5f;
        public float SprintStaminaCostPerSecond { get; set; } = 2f;
        public string SprintParticleEffect { get; set; } = "Smoke";
    }

    public enum JumpState { Idle, Jumping, Falling }

    public class ModEntry : Mod
    {
        public static SprintLogic SprintLogic { get; private set; } = null!;
        public static JumpLogic JumpLogic { get; private set; } = null!;
        private ModConfig Config = null!;

        public static bool IsHorseJumping { get; set; } = false;
        public static int CurrentHorseJumpYOffset { get; set; } = 0;
        public static Vector2 CurrentHorseJumpPosition { get; set; }
        public static float CurrentBounceFactor { get; set; } = 0.85f;

        public override void Entry(IModHelper helper)
        {
            this.Config = this.Helper.ReadConfig<ModConfig>();
            SprintLogic = new SprintLogic(this.Config, helper.Multiplayer, this.ModManifest);
            JumpLogic = new JumpLogic(this.Config, helper, helper.Multiplayer, this.ModManifest);

            var harmony = new Harmony(this.ModManifest.UniqueID);
            harmony.PatchAll();

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

            this.HookUpJumpEvents();

            // UpdateTicked is needed for both jump logic and charging.
            helper.Events.GameLoop.UpdateTicked += JumpLogic.OnUpdateTicked;

            // Sprint events remain the same.
            helper.Events.Input.ButtonPressed += SprintLogic.OnButtonPressed;
            helper.Events.GameLoop.UpdateTicked += SprintLogic.OnUpdateTicked;

            helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
        }

        // NEW: Method to remove all possible jump event handlers.
        private void UnhookJumpEvents()
        {
            this.Helper.Events.Input.ButtonPressed -= this.OnButtonPressed_Instant_Wrapper;
            this.Helper.Events.Input.ButtonPressed -= JumpLogic.OnButtonPressed_Charge;
            this.Helper.Events.Input.ButtonReleased -= JumpLogic.OnButtonReleased_Jump;
        }

        // NEW: Method to subscribe to the correct jump event handlers based on config.
        private void HookUpJumpEvents()
        {
            if (this.Config.InstantJump)
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
                reset: () => this.Config = new ModConfig(),
                save: () =>
                {
                    this.Helper.WriteConfig(this.Config);
                    this.UnhookJumpEvents(); // Remove old events
                    this.HookUpJumpEvents();   // Add new events based on the new config
                }
            );

            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => "Jump Settings");
            configMenu.AddBoolOption(mod: this.ModManifest,
                name: () => "Instant Jump on Press",
                tooltip: () => "If enabled, jump immediately on key press. This disables the hold-to-charge mechanic.",
                getValue: () => this.Config.InstantJump,
                setValue: value => this.Config.InstantJump = value
            );
            configMenu.AddBoolOption(mod: this.ModManifest, name: () => "Enable Jump", tooltip: () => "Toggles the jump ability on or off.", getValue: () => this.Config.EnableJump, setValue: value => this.Config.EnableJump = value);
            configMenu.AddKeybind(mod: this.ModManifest, name: () => "Jump Key", tooltip: () => "The key to press to jump.", getValue: () => this.Config.JumpKey, setValue: value => this.Config.JumpKey = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Jump Duration", tooltip: () => "The total time of the jump in frames. Lower is faster.", min: 10f, max: 60f, interval: 1f, getValue: () => this.Config.JumpDuration, setValue: value => this.Config.JumpDuration = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Jump Height", tooltip: () => "The height of the jump's arc.", min: 24f, max: 96f, interval: 1f, getValue: () => this.Config.JumpHeight, setValue: value => this.Config.JumpHeight = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Base Jump Distance", tooltip: () => "The distance (in tiles) of a running jump with no speed buffs.", min: 1, max: 5, interval: 1, getValue: () => this.Config.NormalJumpDistance, setValue: value => this.Config.NormalJumpDistance = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Jump Distance Scale Factor", tooltip: () => "How much extra distance you get per point of speed.", min: 0.1f, max: 1.0f, interval: 0.1f, getValue: () => this.Config.JumpDistanceScaleFactor, setValue: value => this.Config.JumpDistanceScaleFactor = value);
            configMenu.AddTextOption(mod: this.ModManifest, name: () => "Jump Sound", tooltip: () => "The sound that plays when you jump.", getValue: () => this.Config.JumpSound, setValue: value => this.Config.JumpSound = value,
                allowedValues: new string[] { "dwoop", "jingle1", "stoneStep", "flameSpell", "boop", "coin" },
                formatAllowedValue: value => value switch { "dwoop" => "Classic Dwoop", "jingle1" => "Jingle", "stoneStep" => "Stone Step", "flameSpell" => "Whoosh", "boop" => "Boop", "coin" => "Coin", _ => value });

            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => "Jump Over Settings");
            configMenu.AddBoolOption(mod: this.ModManifest,
                name: () => "Jump Over Large Stumps",
                tooltip: () => "If enabled, allows you to jump over large stumps.",
                getValue: () => this.Config.JumpOverLargeStumps,
                setValue: value => this.Config.JumpOverLargeStumps = value
            );
            configMenu.AddBoolOption(mod: this.ModManifest,
                name: () => "Jump Over Large Logs",
                tooltip: () => "If enabled, allows you to jump over large logs.",
                getValue: () => this.Config.JumpOverLargeLogs,
                setValue: value => this.Config.JumpOverLargeLogs = value
            );
            configMenu.AddBoolOption(mod: this.ModManifest,
                name: () => "Jump Over Boulders",
                tooltip: () => "If enabled, allows you to jump over boulders.",
                getValue: () => this.Config.JumpOverBoulders,
                setValue: value => this.Config.JumpOverBoulders = value
            );

            configMenu.AddNumberOption(mod: this.ModManifest,
                name: () => "Rider Bounce Factor",
                tooltip: () => "How much the rider moves in the saddle during a horse jump. 1.0 = glued to saddle, 0.0 = max bounce.",
                getValue: () => this.Config.HorseJumpPlayerBounce,
                setValue: value => this.Config.HorseJumpPlayerBounce = value,
                min: 0.0f, max: 1.0f, interval: 0.05f
            );

            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => "Sprint Settings");
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Sprint Speed Multiplier", tooltip: () => "The speed multiplier to apply when sprinting. 2.0 means double speed.", min: 1.1f, max: 4.0f, interval: 0.1f, getValue: () => this.Config.SprintSpeedMultiplier, setValue: value => this.Config.SprintSpeedMultiplier = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Sprint Duration (Seconds)", tooltip: () => "How long the sprint lasts in seconds.", min: 1f, max: 10f, interval: 0.5f, getValue: () => this.Config.SprintDurationSeconds, setValue: value => this.Config.SprintDurationSeconds = value);
            configMenu.AddNumberOption(mod: this.ModManifest, name: () => "Stamina Cost Per Second", tooltip: () => "How much stamina is drained per second while sprinting.", min: 0f, max: 10f, interval: 0.5f, getValue: () => this.Config.SprintStaminaCostPerSecond, setValue: value => this.Config.SprintStaminaCostPerSecond = value);

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => "Sprint Particle Effect",
                tooltip: () => "Choose the particle effect that appears when you sprint.",
                getValue: () => this.Config.SprintParticleEffect,
                setValue: value => this.Config.SprintParticleEffect = value,
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