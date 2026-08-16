using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TidalNexus.StandaloneServer
{

    public sealed class ServerBootstrap : MonoBehaviour
    {
        public static ServerBootstrap Instance { get; private set; }

        [Header("Editor / fallback settings")]
        [Tooltip("Photon Fusion App ID. Used when no -appid argument and no " +
                 "TN_APPID variable is present - which is always the case when " +
                 "pressing Play in the editor. Must allow anonymous clients.")]
        public string appId = string.Empty;

        [Tooltip("Session name. Clients must use the same one.")]
        public string sessionName = "tidalnexus-local";

        [Tooltip("UDP port to bind.")]
        public ushort port = 27015;

        [Tooltip("Connection cap.")]
        public int maxPlayers = 32;

        public NetworkRunner Runner { get; private set; }
        public ServerConfig Config { get; private set; }
        public bool IsUp { get; private set; }

        private bool _saidGoodbye;
        private bool _watchdogStarted;

        private int _exitCode;

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Application.targetFrameRate = 60;
        }

        private async void Start()
        {
            Config = ServerConfig.FromEnvironment(new ServerConfig
            {
                AppId = appId,
                SessionName = sessionName,
                Port = port,
                MaxPlayers = maxPlayers,
            });

            ServerHub.Config = Config;

            ServerLog.Info($"starting dedicated server: {Config}");

            ApplyAppId(Config.AppId);

            Runner = gameObject.AddComponent<NetworkRunner>();

            Runner.ProvideInput = false;
            Runner.AddCallbacks(new ServerCallbacks());

            var sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

            var sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(
                SceneRef.FromIndex(Config.SceneIndex), LoadSceneMode.Additive);

            StartGameResult result = await Runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Server,
                SessionName = Config.SessionName,
                PlayerCount = Config.MaxPlayers,
                Scene = sceneInfo,
                SceneManager = sceneManager,
                Address = NetAddress.Any(Config.Port),
            });

            if (result.Ok)
            {
                IsUp = true;

                ServerHub.Boot(Runner);

                ServerLog.Info(
                    $"server up on port {Config.Port}, session '{Config.SessionName}'");
            }
            else
            {
                ServerLog.Error(
                    $"StartGame failed: {result.ShutdownReason} - {result.ErrorMessage}");
                Quit(1);
            }
        }

        private void FixedUpdate()
        {
            if (!IsUp)
            {
                return;
            }

            ServerHub.Tick(Time.fixedDeltaTime);
            PollStopFile();
        }

        /// <summary>
        /// The only stop that can say goodbye.
        ///
        /// A WM_CLOSE ignores what Application.wantsToQuit returns: the app tears
        /// down in the same frame, so the runner stops before Fusion flushes
        /// anything queued and the goodbye never leaves. Nothing in the quit
        /// hooks can widen that window.
        ///
        /// So the stop is asked for out of band instead. Dropping a file named
        /// StopFileName beside the executable is noticed on an ordinary tick,
        /// while the server is still running normally, which is what lets the
        /// farewell flush before anything is torn down.
        /// </summary>
        private const string StopFileName = "stop";

        private float _nextStopPoll;

        private void PollStopFile()
        {
            if (_saidGoodbye || Time.realtimeSinceStartup < _nextStopPoll)
            {
                return;
            }

            _nextStopPoll = Time.realtimeSinceStartup + 0.5f;

            string path = System.IO.Path.Combine(
                System.AppDomain.CurrentDomain.BaseDirectory, StopFileName);

            try
            {
                if (!System.IO.File.Exists(path))
                {
                    return;
                }

                System.IO.File.Delete(path);
            }
            catch (System.Exception e)
            {
                ServerLog.Warn($"could not read the stop file: {e.Message}");
                return;
            }

            ServerLog.Info("stop file seen - shutting down gracefully");
            _saidGoodbye = true;
            StartWatchdog();
            StartCoroutine(GoodbyeThenQuit());
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                ServerHub.Shutdown();
            }
        }

        private static void ApplyAppId(string appId)
        {
            try
            {
                if (!PhotonAppSettings.TryGetGlobal(out PhotonAppSettings settings) ||
                    settings == null)
                {
                    ServerLog.Error("could not reach PhotonAppSettings");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(appId))
                {
                    settings.AppSettings.AppIdFusion = appId;
                }

                string effective = settings.AppSettings.AppIdFusion;

                if (string.IsNullOrWhiteSpace(effective))
                {
                    ServerLog.Error(
                        "no App ID: not given as -appid/TN_APPID and none set in " +
                        "PhotonAppSettings.asset. Photon will reject this with " +
                        "InvalidAuthentication.");
                    return;
                }

                ServerLog.Info(
                    $"app id {effective} (from {(string.IsNullOrWhiteSpace(appId) ? "PhotonAppSettings" : "override")}), " +
                    $"AppVersion '{settings.AppSettings.AppVersion}'");
            }
            catch (System.Exception ex)
            {
                ServerLog.Error($"could not set App ID: {ex.Message}");
            }
        }

        public static void Quit(int code)
        {
            if (Instance != null)
            {
                Instance._exitCode = code;
            }

            ServerLog.Info($"shutting down ({code})");
            Application.Quit(code);
        }

        private void OnApplicationQuit()
        {
            // Not async. Awaiting Runner.Shutdown() here resumes on a Unity
            // context that is already being torn down, so the continuation can
            // simply never run. GoodbyeThenQuit owns the shutdown and its
            // timing; reaching here with the runner still up means the quit
            // bypassed the veto.
            if (Runner != null && Runner.IsRunning)
            {
                ServerLog.Warn("quit reached OnApplicationQuit with the runner still up");
                Runner.Shutdown();
            }
        }

        private void OnEnable()
        {
            Application.wantsToQuit += WantsToQuit;
        }

        private void OnDisable()
        {
            Application.wantsToQuit -= WantsToQuit;
        }

        private bool WantsToQuit()
        {
            if (_saidGoodbye)
            {
                return true;
            }

            _saidGoodbye = true;

            // First, before anything that can be skipped. A WM_CLOSE ignores the
            // value returned here and tears the app down anyway, so a watchdog
            // armed later - inside the coroutine, after its first yield - never
            // arms at all.
            StartWatchdog();
            ServerLog.Info("quit requested - holding it open to say goodbye");

            // StartCoroutine throws on an inactive GameObject. Letting that
            // escape would leave _saidGoodbye set with no coroutine running, so
            // the quit proceeds and nothing is ever sent.
            try
            {
                StartCoroutine(GoodbyeThenQuit());
            }
            catch (System.Exception e)
            {
                ServerLog.Warn($"could not start the goodbye: {e.Message}");
                StartWatchdog();
                return true;
            }

            return false;
        }

        private System.Collections.IEnumerator GoodbyeThenQuit()
        {
            ServerHub.Farewell();

            // Fusion queues reliable data and flushes it on a later send, so the
            // runner has to keep running afterwards for the goodbye to leave at
            // all. Measured: 0.5s is too short, 3s is enough.
            yield return new WaitForSecondsRealtime(3f);

            if (Runner != null && Runner.IsRunning)
            {
                Runner.Shutdown();
                yield return new WaitForSecondsRealtime(1f);
            }

            StartWatchdog();
            ServerLog.Info("goodbye sent - quitting");
            Application.Quit(_exitCode);
        }

        /// <summary>
        /// Exits the process if Application.Quit does not.
        ///
        /// Quit is a request, and it has been observed to run the whole teardown
        /// - runner stopped, accounts saved - and leave the process alive. That
        /// turns every stop into a force kill, which skips deregistration and
        /// fails the next start with GameIdAlreadyExists.
        ///
        /// A background thread rather than a coroutine: by the time this matters
        /// the player loop is gone, so nothing on it will fire. Background so it
        /// cannot itself keep a healthy process alive.
        /// </summary>
        private const int WatchdogSeconds = 8;

        private void StartWatchdog()
        {
            if (_watchdogStarted)
            {
                return;
            }

            _watchdogStarted = true;

            var watchdog = new System.Threading.Thread(() =>
            {
                System.Threading.Thread.Sleep(WatchdogSeconds * 1000);
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            })
            {
                IsBackground = true,
                Name = "tn-quit-watchdog",
            };

            watchdog.Start();
        }
    }
}
