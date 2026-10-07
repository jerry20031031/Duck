using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lives on the persistent NetworkRunner. It guarantees that the local duck
/// exists after a network scene switch, including when Unity has unloaded the
/// dynamic lobby player with BigHall.
/// </summary>
[DisallowMultipleComponent]
public sealed class FusionPlayerSceneTransfer : MonoBehaviour, INetworkRunnerCallbacks
{
    private const string LobbySceneName = "BigHall";
    private const string GameSceneName = "UNIT1";
    private const int PlayerSpawnCount = 6;
    private const float SpawnGroundRayHeight = 8f;
    private const float SpawnGroundRayDistance = 16f;
    private const float SpawnGroundClearance = 0.02f;
    private const float MinimumGroundArea = 100f;
    private const float MaximumGroundThickness = 5f;
    // Used only while the named first-level spawn point becomes available.
    // Root height that places this prefab's capsule feet on UNIT1's existing
    // ground BoxCollider. It is only visible for the frame before the named
    // spawn point is resolved.
    private static readonly Vector3 FirstLevelFallbackSpawn = new(0.58f, -0.334f, 15.29f);

    private NetworkRunner runner;
    private NetworkObject playerPrefab;
    private string playerName;
    private RPGCameraFollow.ViewState savedCameraView;
    private bool hasSavedCameraView;
    private Quaternion savedPlayerRotation = Quaternion.identity;
    private bool hasSavedPlayerRotation;
    private bool unit1PlayerPlaced;
    private bool unit1CameraAttached;

    public void Configure(NetworkRunner networkRunner, NetworkObject prefab, string localPlayerName)
    {
        if (runner != null)
        {
            runner.RemoveCallbacks(this);
        }

        runner = networkRunner;
        playerPrefab = prefab;
        playerName = localPlayerName;
        runner?.AddCallbacks(this);
    }

    private void OnDestroy()
    {
        runner?.RemoveCallbacks(this);
    }

    private void LateUpdate()
    {
        if (SceneManager.GetActiveScene().name == LobbySceneName)
        {
            RememberLobbyView();
        }
    }

    private void Update()
    {
        if (SceneManager.GetActiveScene().name != GameSceneName)
        {
            return;
        }

        if (!unit1PlayerPlaced)
        {
            unit1PlayerPlaced = PlaceLocalPlayerInUnit1();
        }

        if (unit1PlayerPlaced && !unit1CameraAttached)
        {
            AttachCameraToPlacedPlayer();
        }
    }

    void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner networkRunner)
    {
        if (networkRunner != runner || SceneManager.GetActiveScene().name != GameSceneName)
        {
            return;
        }

        unit1PlayerPlaced = false;
        unit1CameraAttached = false;

        // UNIT1's authoring cameras are inactive. Enable one before the
        // player is restored so DuckMover and the follow camera use the same
        // view immediately after the scene switch.
        RPGCameraFollow cameraFollow = RPGCameraFollow.GetOrActivateMainCamera();
        if (cameraFollow != null && hasSavedCameraView)
        {
            cameraFollow.RestoreViewState(savedCameraView);
        }

        EnsureLocalPlayerInGameScene();
        if (PlaceLocalPlayerInUnit1())
        {
            unit1PlayerPlaced = true;
            AttachCameraToPlacedPlayer();
        }
    }

    private bool PlaceLocalPlayerInUnit1()
    {
        if (runner == null
            || !runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject playerObject)
            || playerObject == null)
        {
            return false;
        }

        FusionDuckPlayer player = playerObject.GetComponent<FusionDuckPlayer>();
        if (player == null)
        {
            return false;
        }

        // TryGetPlayerObject(LocalPlayer) is the authoritative local mapping.
        // In Shared mode this must request StateAuthority; InputAuthority is
        // not a reliable local-player test and NetworkTransform makes a
        // non-state-authority Rigidbody kinematic.
        player.EnableLocalControlFromSceneTransfer();

        int spawnNumber = Mathf.Abs(runner.LocalPlayer.AsIndex) % PlayerSpawnCount + 1;
        GameObject spawnObject = GameObject.Find("Player Spawn " + spawnNumber);
        if (spawnObject == null)
        {
            return false;
        }

        Vector3 spawnPosition = GetGroundedSpawnPosition(
            spawnObject.transform.position,
            player.GetComponent<CapsuleCollider>());
        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = spawnPosition;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
        else
        {
            player.transform.position = spawnPosition;
        }

        return true;
    }

    private void AttachCameraToPlacedPlayer()
    {
        if (runner == null
            || !runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject playerObject)
            || playerObject == null)
        {
            return;
        }

        RPGCameraFollow cameraFollow = RPGCameraFollow.GetOrActivateMainCamera();
        if (cameraFollow == null)
        {
            return;
        }

        cameraFollow.SetTarget(playerObject.transform);
        unit1CameraAttached = true;
    }

    private static Vector3 GetGroundedSpawnPosition(Vector3 spawnPosition, CapsuleCollider capsule)
    {
        // The authored map already has one large BoxCollider below the
        // playable area. Prefer its real top surface over a generic raycast,
        // so a roof, wall or tree cannot become the spawn surface.
        BoxCollider groundCollider = FindMapGroundCollider(spawnPosition);
        if (groundCollider != null)
        {
            return PositionFeetOnSurface(spawnPosition, groundCollider.bounds.max.y, capsule);
        }

        Vector3 rayOrigin = spawnPosition + Vector3.up * SpawnGroundRayHeight;
        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, SpawnGroundRayDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            return spawnPosition;
        }

        return PositionFeetOnSurface(spawnPosition, hit.point.y, capsule);
    }

    private static BoxCollider FindMapGroundCollider(Vector3 position)
    {
        Scene scene = SceneManager.GetActiveScene();
        BoxCollider bestGround = null;
        float bestArea = 0f;
        BoxCollider[] colliders = FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < colliders.Length; i++)
        {
            BoxCollider candidate = colliders[i];
            if (candidate == null
                || !candidate.enabled
                || candidate.isTrigger
                || candidate.gameObject.scene != scene)
            {
                continue;
            }

            Bounds bounds = candidate.bounds;
            float area = bounds.size.x * bounds.size.z;
            bool containsSpawn = position.x >= bounds.min.x
                && position.x <= bounds.max.x
                && position.z >= bounds.min.z
                && position.z <= bounds.max.z;
            if (!containsSpawn
                || bounds.size.y > MaximumGroundThickness
                || area < MinimumGroundArea
                || area <= bestArea)
            {
                continue;
            }

            bestGround = candidate;
            bestArea = area;
        }

        return bestGround;
    }

    private static Vector3 PositionFeetOnSurface(Vector3 spawnPosition, float surfaceHeight, CapsuleCollider capsule)
    {
        float feetOffset = capsule != null
            ? capsule.center.y - capsule.height * 0.5f
            : 0f;
        return new Vector3(spawnPosition.x, surfaceHeight - feetOffset + SpawnGroundClearance, spawnPosition.z);
    }

    private void RememberLobbyView()
    {
        RPGCameraFollow cameraFollow = Camera.main != null
            ? Camera.main.GetComponent<RPGCameraFollow>()
            : null;
        if (cameraFollow != null)
        {
            savedCameraView = cameraFollow.CaptureViewState();
            hasSavedCameraView = true;
        }

        if (runner != null
            && runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject playerObject)
            && playerObject != null)
        {
            savedPlayerRotation = playerObject.transform.rotation;
            hasSavedPlayerRotation = true;
        }
    }

    private NetworkObject EnsureLocalPlayerInGameScene()
    {
        if (runner == null || playerPrefab == null)
        {
            return null;
        }

        if (runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject existingPlayer) && existingPlayer != null)
        {
            return existingPlayer;
        }

        NetworkObject playerObject = runner.Spawn(
            playerPrefab,
            FirstLevelFallbackSpawn,
            hasSavedPlayerRotation ? savedPlayerRotation : Quaternion.identity,
            runner.LocalPlayer);
        if (playerObject == null)
        {
            return null;
        }

        runner.SetPlayerObject(runner.LocalPlayer, playerObject);
        playerObject.GetComponent<FusionDuckPlayer>()?.SetPlayerName(playerName);
        return playerObject;
    }

    void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input) { }
    void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner) { }
    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner)
    {
        if (runner == this.runner && SceneManager.GetActiveScene().name == LobbySceneName)
        {
            RememberLobbyView();
        }
    }
    void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
}
