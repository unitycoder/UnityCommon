using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace UnityCommon
{
    [RequireComponent(typeof(Text))]
    public class FpsDisplay : MonoBehaviour
    {
        [SerializeField] private float updateFrequency = 1f;

        private Text text;

        private void Awake ()
        {
            text = GetComponent<Text>();
        }

        private void Start ()
        {
            StartCoroutine(UpdateCounter());
        }

        private IEnumerator UpdateCounter ()
        {
            var waitForDelay = new WaitForSeconds(updateFrequency);

            while (Application.isPlaying)
            {
                var lastFrameCount = Time.frameCount;
                var lastTime = Time.realtimeSinceStartup;

                yield return waitForDelay;

                var timeDelta = Time.realtimeSinceStartup - lastTime;
                var frameDelta = Time.frameCount - lastFrameCount;

                text.text = $"{frameDelta / timeDelta:0.} FPS";
            }
        }
    }
}
