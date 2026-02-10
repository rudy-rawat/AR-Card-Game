// using System.Collections.Generic;
// using UnityEngine;

// public static class ServerMock
// {
//     static Queue<string> availableSlots = new Queue<string>(
//         new[] { "A1", "A2", "B1", "B2" });

//     public static string AssignSlot(string cardID)
//     {
//         return availableSlots.Dequeue();
//     }
// }
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SimpleGameServer
{
    public class Player
    {
        public int Id { get; set; }
        public IPEndPoint EndPoint { get; set; }
        public int Health { get; set; } = 100;
        public DateTime LastHeartbeat { get; set; }
        public bool IsAlive => Health > 0;

        public Player(int id, IPEndPoint endPoint)
        {
            Id = id;
            EndPoint = endPoint;
            LastHeartbeat = DateTime.UtcNow;
        }
    }

    public class GameServer
    {
        private UdpClient udpServer;
        private Dictionary<int, Player> players = new Dictionary<int, Player>();
        private int nextPlayerId = 1;
        private readonly object lockObj = new object();
        private bool isRunning = false;
        private const int PORT = 7777;
        private const int ATTACK_DAMAGE = 10;

        public void Start()
        {
            udpServer = new UdpClient(PORT);
            isRunning = true;

            Console.WriteLine($"Game Server started on port {PORT}");
            Console.WriteLine("Waiting for 2 players to connect...\n");

            // Start listening thread
            Thread listenThread = new Thread(Listen);
            listenThread.Start();

            // Start game update thread
            Thread updateThread = new Thread(GameUpdate);
            updateThread.Start();
        }

        private void Listen()
        {
            while (isRunning)
            {
                try
                {
                    IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = udpServer.Receive(ref clientEndPoint);
                    string message = Encoding.UTF8.GetString(data);

                    ProcessMessage(message, clientEndPoint);
                }
                catch (SocketException)
                {
                    // Socket closed
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        private void ProcessMessage(string message, IPEndPoint clientEndPoint)
        {
            lock (lockObj)
            {
                string[] parts = message.Split('|');
                string command = parts[0];

                switch (command)
                {
                    case "CONNECT":
                        HandleConnect(clientEndPoint);
                        break;

                    case "ATTACK":
                        if (parts.Length > 1 && int.TryParse(parts[1], out int playerId))
                        {
                            HandleAttack(playerId);
                        }
                        break;

                    case "HEARTBEAT":
                        if (parts.Length > 1 && int.TryParse(parts[1], out int hbPlayerId))
                        {
                            HandleHeartbeat(hbPlayerId);
                        }
                        break;

                    case "DISCONNECT":
                        if (parts.Length > 1 && int.TryParse(parts[1], out int dcPlayerId))
                        {
                            HandleDisconnect(dcPlayerId);
                        }
                        break;
                }
            }
        }

        private void HandleConnect(IPEndPoint endPoint)
        {
            if (players.Count >= 2)
            {
                SendMessage("FULL", endPoint);
                Console.WriteLine($"Connection refused from {endPoint} - Server full");
                return;
            }

            Player newPlayer = new Player(nextPlayerId, endPoint);
            players[nextPlayerId] = newPlayer;

            string response = $"CONNECTED|{nextPlayerId}";
            SendMessage(response, endPoint);

            Console.WriteLine($"Player {nextPlayerId} connected from {endPoint}");
            Console.WriteLine($"Players online: {players.Count}/2\n");

            nextPlayerId++;

            // Notify all players of game state
            BroadcastGameState();
        }

        private void HandleAttack(int attackerId)
        {
            if (!players.ContainsKey(attackerId))
                return;

            Player attacker = players[attackerId];

            if (!attacker.IsAlive)
            {
                SendMessage("DEAD|You are dead!", attacker.EndPoint);
                return;
            }

            // Find opponent
            Player opponent = null;
            foreach (var player in players.Values)
            {
                if (player.Id != attackerId)
                {
                    opponent = player;
                    break;
                }
            }

            if (opponent == null)
            {
                SendMessage("ERROR|No opponent found", attacker.EndPoint);
                return;
            }

            // Apply damage
            opponent.Health = Math.Max(0, opponent.Health - ATTACK_DAMAGE);

            Console.WriteLine($"Player {attackerId} attacked Player {opponent.Id}!");
            Console.WriteLine($"Player {opponent.Id} health: {opponent.Health}/100\n");

            // Check for game over
            if (opponent.Health <= 0)
            {
                SendMessage($"VICTORY|You defeated Player {opponent.Id}!", attacker.EndPoint);
                SendMessage($"DEFEAT|You were defeated by Player {attackerId}!", opponent.EndPoint);
                Console.WriteLine($"Game Over! Player {attackerId} wins!\n");
            }

            // Broadcast updated game state
            BroadcastGameState();
        }

        private void HandleHeartbeat(int playerId)
        {
            if (players.ContainsKey(playerId))
            {
                players[playerId].LastHeartbeat = DateTime.UtcNow;
            }
        }

        private void HandleDisconnect(int playerId)
        {
            if (players.ContainsKey(playerId))
            {
                Console.WriteLine($"Player {playerId} disconnected");
                players.Remove(playerId);
                BroadcastGameState();
            }
        }

        private void GameUpdate()
        {
            while (isRunning)
            {
                Thread.Sleep(1000); // Update every second

                lock (lockObj)
                {
                    List<int> toRemove = new List<int>();
                    foreach (var player in players.Values)
                    {
                        if ((DateTime.UtcNow - player.LastHeartbeat).TotalSeconds > 10)
                        {
                            toRemove.Add(player.Id);
                        }
                    }

                    foreach (int id in toRemove)
                    {
                        Console.WriteLine($"Player {id} timed out");
                        players.Remove(id);
                    }

                    if (toRemove.Count > 0)
                    {
                        BroadcastGameState();
                    }
                }
            }
        }

        private void BroadcastGameState()
        {
            foreach (var player in players.Values)
            {
                string state = BuildGameState(player.Id);
                SendMessage(state, player.EndPoint);
            }
        }

        private string BuildGameState(int forPlayerId)
        {
            StringBuilder sb = new StringBuilder("STATE");

            foreach (var player in players.Values)
            {
                sb.Append($"|{player.Id},{player.Health},{(player.IsAlive ? "1" : "0")}");
            }

            return sb.ToString();
        }

        private void SendMessage(string message, IPEndPoint endPoint)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            udpServer.Send(data, data.Length, endPoint);
        }

        public void Stop()
        {
            isRunning = false;
            udpServer?.Close();
            Console.WriteLine("Server stopped");
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            GameServer server = new GameServer();
            server.Start();

            Console.WriteLine("Press any key to stop the server...");
            Console.ReadKey();

            server.Stop();
        }
    }
}