using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace ShadowPrototype
{
    public class HandLandmarkUdpReceiver : MonoBehaviour
    {
        private const int ValuesPerLandmark = 3;
        private const int LandmarksPerHand = 21;

        [SerializeField] private int port = 5052;
        [SerializeField] private bool startOnEnable = true;
        [SerializeField] private bool printPacketsToConsole;
        [SerializeField] private float staleAfterSeconds = 1.0f;
        [SerializeField] private bool logFirstPacket = true;
        [SerializeField] private bool logStatusToConsole;
        [SerializeField] private float statusLogInterval = 1.0f;

        private readonly object dataLock = new object();

        private Thread receiveThread;
        private UdpClient client;
        private volatile bool isRunning;
        private Vector3[] latestLandmarks = Array.Empty<Vector3>();
        private string latestPacket = string.Empty;
        private DateTime latestPacketUtc = DateTime.MinValue;
        private string pendingError;
        private string pendingInfo;
        private string latestSender = string.Empty;
        private bool hasLoggedFirstPacket;
        private float nextStatusLogTime;

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

                    return (DateTime.UtcNow - latestPacketUtc).TotalSeconds <= staleAfterSeconds;
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

        public double LastPacketAgeSeconds
        {
            get
            {
                lock (dataLock)
                {
                    if (latestPacketUtc == DateTime.MinValue)
                    {
                        return double.PositiveInfinity;
                    }

                    return (DateTime.UtcNow - latestPacketUtc).TotalSeconds;
                }
            }
        }

        public void Configure(int receiverPort)
        {
            port = receiverPort;
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

                if ((DateTime.UtcNow - latestPacketUtc).TotalSeconds > staleAfterSeconds)
                {
                    landmarks = null;
                    return false;
                }

                landmarks = (Vector3[])latestLandmarks.Clone();
                return true;
            }
        }

        private void OnEnable()
        {
            if (startOnEnable)
            {
                StartReceiver();
            }
        }

        private void Update()
        {
            if (!string.IsNullOrEmpty(pendingError))
            {
                Debug.LogWarning(pendingError);
                pendingError = null;
            }

            if (!string.IsNullOrEmpty(pendingInfo))
            {
                Debug.Log(pendingInfo);
                pendingInfo = null;
            }

            if (logStatusToConsole && Time.unscaledTime >= nextStatusLogTime)
            {
                nextStatusLogTime = Time.unscaledTime + Mathf.Max(statusLogInterval, 0.25f);
                Debug.Log(
                    $"HandLandmarkUdpReceiver status: running={isRunning}, hands={HandCount}, " +
                    $"recent={HasRecentData}, age={LastPacketAgeSeconds:0.00}s, sender={latestSender}");
            }
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

            isRunning = true;
            hasLoggedFirstPacket = false;
            receiveThread = new Thread(ReceiveLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();
        }

        public void StopReceiver()
        {
            isRunning = false;

            try
            {
                client?.Close();
            }
            catch (SocketException)
            {
            }

            client = null;

            if (receiveThread != null && receiveThread.IsAlive)
            {
                receiveThread.Join(200);
            }

            receiveThread = null;
        }

        private void ReceiveLoop()
        {
            try
            {
                client = new UdpClient(port);
            }
            catch (Exception exception)
            {
                pendingError = $"HandLandmarkUdpReceiver failed to bind UDP port {port}: {exception.Message}";
                isRunning = false;
                return;
            }

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
                        latestPacket = packet;
                        latestLandmarks = parsedLandmarks;
                        latestPacketUtc = DateTime.UtcNow;
                        latestSender = anyIP.ToString();
                    }

                    if (logFirstPacket && !hasLoggedFirstPacket)
                    {
                        hasLoggedFirstPacket = true;
                        pendingInfo =
                            $"HandLandmarkUdpReceiver received first packet from {anyIP} " +
                            $"({parsedLandmarks.Length} landmarks).";
                    }

                    if (printPacketsToConsole)
                    {
                        Debug.Log($"HandLandmarkUdpReceiver packet: {latestPacket}");
                    }
                }
                catch (SocketException)
                {
                    if (isRunning)
                    {
                        pendingError = $"HandLandmarkUdpReceiver lost UDP connection on port {port}.";
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    pendingError = $"HandLandmarkUdpReceiver parse error: {exception.Message}";
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
