using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace ShadowPrototype
{
    public class MediaPipeUdpReceiver : MonoBehaviour
    {
        private const int ValuesPerLandmark = 3;
        private const int LandmarksPerHand = 21;
        private const int Port = 5053;
        private const float StaleAfterSeconds = 1.0f;

        private readonly object dataLock = new object();

        private Thread receiveThread;
        private UdpClient client;
        private volatile bool isRunning;
        private Vector3[] latestLandmarks = Array.Empty<Vector3>();
        private DateTime latestPacketUtc = DateTime.MinValue;
        private string pendingError;

        public bool HasRecentData
        {
            get
            {
                lock (dataLock)
                {
                    if (latestLandmarks.Length < LandmarksPerHand)
                    {
                        return false;
                    }

                    return (DateTime.UtcNow - latestPacketUtc).TotalSeconds <= StaleAfterSeconds;
                }
            }
        }

        public int HandCount
        {
            get
            {
                lock (dataLock)
                {
                    return latestLandmarks.Length / LandmarksPerHand;
                }
            }
        }

        public bool TryGetLatestLandmarks(out Vector3[] landmarks)
        {
            lock (dataLock)
            {
                if (latestLandmarks.Length < LandmarksPerHand)
                {
                    landmarks = null;
                    return false;
                }

                if ((DateTime.UtcNow - latestPacketUtc).TotalSeconds > StaleAfterSeconds)
                {
                    landmarks = null;
                    return false;
                }

                landmarks = (Vector3[])latestLandmarks.Clone();
                return true;
            }
        }

        private void Update()
        {
            if (string.IsNullOrEmpty(pendingError))
            {
                return;
            }

            Debug.LogWarning(pendingError);
            pendingError = null;
        }

        private void OnDisable()
        {
            StopReceiver();
        }

        private void OnDestroy()
        {
            StopReceiver();
        }

        public void StartReceiver()
        {
            if (isRunning)
            {
                return;
            }

            try
            {
                client = new UdpClient(Port);
            }
            catch (SocketException exception)
            {
                pendingError = $"MediaPipeUdpReceiver: failed to bind UDP port {Port}: {exception.Message}";
                isRunning = false;
                return;
            }

            isRunning = true;

            receiveThread = new Thread(ReceiveLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();
        }

        public void StopReceiver()
        {
            isRunning = false;

            client?.Close();
            client = null;

            if (receiveThread != null && receiveThread.IsAlive)
            {
                receiveThread.Join(200);
            }

            receiveThread = null;
        }

        private void ReceiveLoop()
        {
            while (isRunning)
            {
                try
                {
                    IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] dataBytes = client.Receive(ref anyIP);
                    string packet = System.Text.Encoding.UTF8.GetString(dataBytes);

                    if (!TryParseLandmarks(packet, out Vector3[] parsedLandmarks))
                    {
                        continue;
                    }

                    lock (dataLock)
                    {
                        latestLandmarks = parsedLandmarks;
                        latestPacketUtc = DateTime.UtcNow;
                    }
                }
                catch (SocketException)
                {
                    if (isRunning)
                    {
                        pendingError = $"MediaPipeUdpReceiver: lost UDP connection on port {Port}.";
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private static bool TryParseLandmarks(string packet, out Vector3[] landmarks)
        {
            landmarks = null;
            if (string.IsNullOrWhiteSpace(packet))
            {
                return false;
            }

            string trimmed = packet.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(1);
            }

            if (trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            string[] values = trimmed.Split(new[] { ',', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length < LandmarksPerHand * ValuesPerLandmark || values.Length % ValuesPerLandmark != 0)
            {
                return false;
            }

            int landmarkCount = values.Length / ValuesPerLandmark;
            landmarks = new Vector3[landmarkCount];

            for (int i = 0; i < landmarkCount; i++)
            {
                int valueIndex = i * ValuesPerLandmark;
                if (!float.TryParse(values[valueIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                    !float.TryParse(values[valueIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                    !float.TryParse(values[valueIndex + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    landmarks = null;
                    return false;
                }

                landmarks[i] = new Vector3(x, y, z);
            }

            return true;
        }
    }
}
