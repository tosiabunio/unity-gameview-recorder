namespace Airon.GameViewRecorder
{
    /// <summary>Where a recording's sound comes from.</summary>
    public enum AudioInput
    {
        /// <summary>No sound.</summary>
        Off,

        /// <summary>Wwise when its sound engine is running, else Unity audio when it is enabled.</summary>
        Auto,

        /// <summary>What the active AudioListener hears. Needs Unity audio (Project Settings > Audio).</summary>
        Unity,

        /// <summary>The Wwise sound engine's main output (see <see cref="WwiseCapture"/>).</summary>
        Wwise
    }
}
