using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public sealed class ServerCallbacks : INetworkRunnerCallbacks
    {
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) =>
            PlayerDirector.Join(runner, player);

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) =>
            PlayerDirector.Leave(runner, player);

        public void OnConnectRequest(
            NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
            string id = token != null && token.Length > 0
                ? System.Text.Encoding.UTF8.GetString(token)
                : "(no token)";

            ServerLog.Info($"connect request from {id}");
            request.Accept();
        }

        public void OnConnectedToServer(NetworkRunner runner) =>
            ServerLog.Info("connected to server");

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) =>
            ServerLog.Warn($"disconnected: {reason}");

        public void OnConnectFailed(
            NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) =>
            ServerLog.Warn($"connect failed from {remoteAddress}: {reason}");

        public void OnSceneLoadDone(NetworkRunner runner)
        {
            ServerLog.Info("scene load done - world is live");
            WorldPopulator.Populate();

            Services.EventService.WorldIsLive = true;
        }

        public void OnSceneLoadStart(NetworkRunner runner) =>
            ServerLog.Info("scene load started");

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            ServerLog.Warn($"runner shutdown: {shutdownReason}");
            PlayerDirector.Reset();
        }

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
        }

        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
        {
        }

        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
        {
        }

        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
        }

        public void OnCustomAuthenticationResponse(
            NetworkRunner runner, Dictionary<string, object> data)
        {
        }

        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
        {
        }

        public void OnReliableDataReceived(
            NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data)
        {
            ReliableChannel.Receive(runner, player, data);
        }

        public void OnReliableDataProgress(
            NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
        {
        }

        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
        }

        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
        }
    }
}
