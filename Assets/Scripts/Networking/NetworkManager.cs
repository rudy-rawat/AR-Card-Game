using UnityEngine;
using System;
using System.Collections.Generic;
using NativeWebSocket;

public class NetworkManager : MonoBehaviour
{
    [Header("Server Settings")]
    [SerializeField] private string serverUrl = "ws://localhost:8080";
    
    [Header("Card Settings")]
    [SerializeField] private string cardID = "CARD001";  // Your card ID
    
    [Header("Player Settings")]
    [SerializeField] private int maxHealth = 100;
    
    // Events for UI to subscribe to
    public event Action<int, string, string> OnConnected; // playerId, cardID, assignedSlot
    public event Action<string> OnError;
    public event Action<GameState> OnGameStateUpdated;
    public event Action<int, string, string, int> OnAttackSuccess; // targetId, targetCardID, targetSlot, newHealth
    public event Action<int, string, string, int> OnAttacked; // attackerId, attackerCardID, attackerSlot, damage
    public event Action<string, string> OnVictory; // message, defeatedCardID
    public event Action<string, string> OnDefeat; // message, winnerCardID
    public event Action<string, string> OnSlotAssigned; // cardID, slot
    
    // WebSocket connection
    private WebSocket websocket;
    
    // Player data
    private int myPlayerId = -1;
    private string myCardID = "";
    private string myAssignedSlot = "";
    private Dictionary<int, PlayerData> players = new Dictionary<int, PlayerData>();
    private Dictionary<string, PlayerData> playersByCardID = new Dictionary<string, PlayerData>();
    
    // Connection state
    private bool isConnected = false;
    private float heartbeatTimer = 0f;
    private const float HEARTBEAT_INTERVAL = 5f;
    
    private void Start()
    {
        myCardID = cardID;
        ConnectToServer();
    }
    
    private async void ConnectToServer()
    {
        Debug.Log($"Connecting to server: {serverUrl}");
        Debug.Log($"Using Card ID: {myCardID}");
        
        websocket = new WebSocket(serverUrl);
        
        websocket.OnOpen += () =>
        {
            Debug.Log("WebSocket connection opened!");
            // Send connection request with cardID
            SendConnectMessage();
        };
        
        websocket.OnMessage += (bytes) =>
        {
            string message = System.Text.Encoding.UTF8.GetString(bytes);
            HandleServerMessage(message);
        };
        
        websocket.OnError += (e) =>
        {
            Debug.LogError($"WebSocket Error: {e}");
            OnError?.Invoke($"Connection error: {e}");
        };
        
        websocket.OnClose += (e) =>
        {
            Debug.Log($"WebSocket connection closed: {e}");
            isConnected = false;
        };
        
        await websocket.Connect();
    }
    
    private void SendConnectMessage()
    {
        var connectMsg = new
        {
            type = "CONNECT",
            cardID = myCardID
        };
        SendMessage(connectMsg);
        Debug.Log($"Sent connection request with Card ID: {myCardID}");
    }
    
    private void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
        if (websocket != null)
        {
            websocket.DispatchMessageQueue();
        }
        #endif
        
        // Send periodic heartbeat
        if (isConnected)
        {
            heartbeatTimer += Time.deltaTime;
            if (heartbeatTimer >= HEARTBEAT_INTERVAL)
            {
                SendHeartbeat();
                heartbeatTimer = 0f;
            }
        }
    }
    
    private void HandleServerMessage(string json)
    {
        try {
            ServerMessage msg = JsonUtility.FromJson<ServerMessage>(json);
            
            switch (msg.type)
            {
                case "CONNECTED":
                    ConnectedMessage connMsg = JsonUtility.FromJson<ConnectedMessage>(json);
                    myPlayerId = connMsg.playerId;
                    myCardID = connMsg.cardID;
                    myAssignedSlot = connMsg.assignedSlot;
                    isConnected = true;
                    Debug.Log($"✅ Connected as Player {myPlayerId}");
                    Debug.Log($"   Card ID: {myCardID}");
                    Debug.Log($"   Assigned Slot: {myAssignedSlot}");
                    OnConnected?.Invoke(myPlayerId, myCardID, myAssignedSlot);
                    OnSlotAssigned?.Invoke(myCardID, myAssignedSlot);
                    break;
                    
                case "FULL":
                    Debug.LogWarning("Server is full!");
                    OnError?.Invoke("Server is full (2/2 players)");
                    break;
                    
                case "GAME_STATE":
                    HandleGameState(json);
                    break;
                    
                case "ATTACK_SUCCESS":
                    AttackSuccessMessage attackMsg = JsonUtility.FromJson<AttackSuccessMessage>(json);
                    Debug.Log($"⚔️ Attack successful! Dealt {attackMsg.damage} damage to Card {attackMsg.targetCardID} in Slot {attackMsg.targetSlot}");
                    OnAttackSuccess?.Invoke(attackMsg.targetId, attackMsg.targetCardID, attackMsg.targetSlot, attackMsg.targetHealth);
                    break;
                    
                case "ATTACKED":
                    AttackedMessage attackedMsg = JsonUtility.FromJson<AttackedMessage>(json);
                    Debug.Log($"💥 Attacked by Card {attackedMsg.attackerCardID} from Slot {attackedMsg.attackerSlot}! Took {attackedMsg.damage} damage");
                    OnAttacked?.Invoke(attackedMsg.attackerId, attackedMsg.attackerCardID, attackedMsg.attackerSlot, attackedMsg.damage);
                    break;
                    
                case "VICTORY":
                    VictoryMessage victoryMsg = JsonUtility.FromJson<VictoryMessage>(json);
                    Debug.Log($"🏆 VICTORY! {victoryMsg.message}");
                    OnVictory?.Invoke(victoryMsg.message, victoryMsg.defeatedCardID);
                    break;
                    
                case "DEFEAT":
                    DefeatMessage defeatMsg = JsonUtility.FromJson<DefeatMessage>(json);
                    Debug.Log($"💀 DEFEAT! {defeatMsg.message}");
                    OnDefeat?.Invoke(defeatMsg.message, defeatMsg.winnerCardID);
                    break;
                    
                case "SLOT_INFO":
                    SlotInfoMessage slotMsg = JsonUtility.FromJson<SlotInfoMessage>(json);
                    Debug.Log($"🅿️ Slot Info - Card {slotMsg.cardID}: {slotMsg.slot}");
                    OnSlotAssigned?.Invoke(slotMsg.cardID, slotMsg.slot);
                    break;
                    
                case "ERROR":
                    Debug.LogWarning($"Server error: {msg.message}");
                    OnError?.Invoke(msg.message);
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing server message: {e.Message}\nJSON: {json}");
        }
    }
    
    private void HandleGameState(string json)
    {
        try
        {
            GameStateMessage stateMsg = JsonUtility.FromJson<GameStateMessage>(json);
            
            // Update players dictionaries
            players.Clear();
            playersByCardID.Clear();
            
            foreach (var playerData in stateMsg.players)
            {
                players[playerData.id] = playerData;
                playersByCardID[playerData.cardID] = playerData;
            }
            
            // Create GameState object for the event
            GameState gameState = new GameState
            {
                players = stateMsg.players,
                myPlayerId = myPlayerId,
                myCardID = myCardID,
                mySlot = myAssignedSlot
            };
            
            OnGameStateUpdated?.Invoke(gameState);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error handling game state: {e.Message}");
        }
    }
    
    /// <summary>
    /// Attack using your card ID (attacks any available opponent)
    /// </summary>
    public void Attack()
    {
        if (!isConnected)
        {
            Debug.LogWarning("Cannot attack - not connected to server");
            return;
        }
        
        SendMessage(new { type = "ATTACK", cardID = myCardID });
        Debug.Log($"Card {myCardID} sent attack command");
    }
    
    /// <summary>
    /// Attack a specific opponent by their card ID
    /// </summary>
    public void AttackByCardID(string targetCardID)
    {
        if (!isConnected)
        {
            Debug.LogWarning("Cannot attack - not connected to server");
            return;
        }
        
        SendMessage(new { type = "ATTACK_BY_CARD", cardID = myCardID, targetCardID = targetCardID });
        Debug.Log($"Card {myCardID} attacking Card {targetCardID}");
    }
    
   //for later (experimental )
    public void RequestSlotInfo()
    {
        if (!isConnected)
        {
            Debug.LogWarning("Cannot request slot - not connected to server");
            return;
        }
        
        SendMessage(new { type = "GET_SLOT" });
    }
    
    private void SendHeartbeat()
    {
        SendMessage(new { type = "HEARTBEAT" });
    }
    
    public void RequestGameState()
    {
        SendMessage(new { type = "GET_STATE" });
    }
    
    private async void SendMessage(object message)
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            string json = JsonUtility.ToJson(message);
            await websocket.SendText(json);
        }
    }
    
    // Public getters
    public int GetMyPlayerId() => myPlayerId;
    public string GetMyCardID() => myCardID;
    public string GetMyAssignedSlot() => myAssignedSlot;
    public bool IsConnected() => isConnected;
    
    public PlayerData GetPlayerData(int playerId)
    {
        return players.ContainsKey(playerId) ? players[playerId] : null;
    }
    
    public PlayerData GetPlayerDataByCardID(string cardID)
    {
        return playersByCardID.ContainsKey(cardID) ? playersByCardID[cardID] : null;
    }
    
    public PlayerData GetMyPlayerData()
    {
        return GetPlayerData(myPlayerId);
    }
    
    public List<PlayerData> GetAllPlayers()
    {
        return new List<PlayerData>(players.Values);
    }
    
    /// <summary>
    /// Set card ID at runtime (useful for dynamic assignment)
    /// Must be called before connecting
    /// </summary>
    public void SetCardID(string newCardID)
    {
        if (isConnected)
        {
            Debug.LogWarning("Cannot change Card ID while connected!");
            return;
        }
        cardID = newCardID;
        myCardID = newCardID;
        Debug.Log($"Card ID set to: {cardID}");
    }
    
    private async void OnApplicationQuit()
    {
        if (websocket != null)
        {
            await websocket.Close();
        }
    }
}

// Data classes for JSON serialization
[Serializable]
public class ServerMessage
{
    public string type;
    public string message;
}

[Serializable]
public class ConnectedMessage
{
    public string type;
    public int playerId;
    public string cardID;
    public string assignedSlot;
    public string message;
}

[Serializable]
public class GameStateMessage
{
    public string type;
    public PlayerData[] players;
    public int playerCount;
}

[Serializable]
public class AttackSuccessMessage
{
    public string type;
    public int damage;
    public int targetId;
    public string targetCardID;
    public string targetSlot;
    public int targetHealth;
}

[Serializable]
public class AttackedMessage
{
    public string type;
    public int attackerId;
    public string attackerCardID;
    public string attackerSlot;
    public int damage;
    public int newHealth;
}

[Serializable]
public class VictoryMessage
{
    public string type;
    public string message;
    public string defeatedCardID;
}

[Serializable]
public class DefeatMessage
{
    public string type;
    public string message;
    public string winnerCardID;
}

[Serializable]
public class SlotInfoMessage
{
    public string type;
    public string cardID;
    public string slot;
}

[Serializable]
public class PlayerData
{
    public int id;
    public string cardID;
    public string assignedSlot;
    public int health;
    public bool isAlive;
}

[Serializable]
public class GameState
{
    public PlayerData[] players;
    public int myPlayerId;
    public string myCardID;
    public string mySlot;
}