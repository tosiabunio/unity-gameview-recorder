using System.Collections;
using UnityEngine;

namespace Airon.GameViewRecorder
{
    /// <summary>
    /// Hidden helper of a <see cref="RecordingSession"/>: gives it a call at the end of every frame,
    /// after everything (UI included) has been drawn, and one per Update.
    /// </summary>
    [AddComponentMenu("")]
    sealed class FrameGrabber : MonoBehaviour
    {
        internal RecordingSession Session;

        IEnumerator Start()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                Session?.OnEndOfFrame();
            }
        }

        void Update() => Session?.OnUpdate();
    }
}
