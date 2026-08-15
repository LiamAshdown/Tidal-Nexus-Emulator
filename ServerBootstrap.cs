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
            if (IsUp)
            {
                ServerHub.Tick(Time.fixedDeltaTime);
            }
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

        private async void OnApplicationQuit()
        {
            if (Runner != null && Runner.IsRunning)
            {
                await Runner.Shutdown();
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
            StartCoroutine(GoodbyeThenQuit());
            return false;
        }

        private System.Collections.IEnumerator GoodbyeThenQuit()
        {
            ServerHub.Farewell();

            yield return new WaitForSecondsRealtime(3f);

            Application.Quit(_exitCode);
        }
    }
}
