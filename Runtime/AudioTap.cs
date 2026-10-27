using UnityEngine;

namespace Airon.GameViewRecorder
{
    /// <summary>
    /// Taps the game's mixed audio. The recorder adds it to the AudioListener's GameObject, where
    /// OnAudioFilterRead sees the final mix; the audio passes through untouched.
    /// </summary>
    [AddComponentMenu("")]
    sealed class AudioTap : MonoBehaviour
    {
        // Set on the main thread, read on the audio thread.
        internal volatile AudioWriter Writer;

        void OnAudioFilterRead(float[] data, int channels) => Writer?.Push(data, channels);
    }
}
