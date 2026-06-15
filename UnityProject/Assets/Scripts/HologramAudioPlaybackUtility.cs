using UnityEngine;

namespace ShadowPrototype
{
    internal static class HologramAudioPlaybackUtility
    {
        public static AudioSource Resolve2DAudioSource(Component owner, AudioSource currentAudioSource)
        {
            if (currentAudioSource == null && owner != null)
            {
                currentAudioSource = owner.GetComponent<AudioSource>();
            }

            if (currentAudioSource == null && owner != null)
            {
                currentAudioSource = owner.gameObject.AddComponent<AudioSource>();
            }

            if (currentAudioSource != null)
            {
                currentAudioSource.enabled = true;
                currentAudioSource.playOnAwake = false;
                currentAudioSource.loop = false;
                currentAudioSource.mute = false;
                currentAudioSource.volume = 1.0f;
                currentAudioSource.spatialBlend = 0.0f;
                currentAudioSource.ignoreListenerPause = true;
            }

            EnsureActiveAudioListener(owner != null ? owner.gameObject : null);
            return currentAudioSource;
        }

        public static void EnsureActiveAudioListener(GameObject contextObject)
        {
            AudioListener.pause = false;
            if (AudioListener.volume <= 0.0f)
            {
                AudioListener.volume = 1.0f;
            }

            AudioListener[] listeners = Object.FindObjectsOfType<AudioListener>();
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null && listeners[i].isActiveAndEnabled)
                {
                    return;
                }
            }

            Camera targetCamera = null;
            if (contextObject != null)
            {
                targetCamera = contextObject.GetComponentInChildren<Camera>(true);
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera == null)
            {
                targetCamera = Object.FindObjectOfType<Camera>();
            }

            GameObject listenerObject = targetCamera != null
                ? targetCamera.gameObject
                : contextObject;

            if (listenerObject == null)
            {
                return;
            }

            AudioListener listener = listenerObject.GetComponent<AudioListener>();
            if (listener == null)
            {
                listener = listenerObject.AddComponent<AudioListener>();
            }

            listener.enabled = true;
        }
    }
}
