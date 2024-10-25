using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;

namespace TcpServer
{
    public class Room
    {
        public string Name { get; set; }
        public int MaxPlayers { get; set; }
        public string HostId { get; set; }
        public List<string> Players { get; set; }
        public string MapName { get; set; }
        public Dictionary<string, Vector3> PlayerPositions { get; set; } = new Dictionary<string, Vector3>();

    }
    public class Vector3
    {
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
    }

    internal class Program
    {
        readonly static string connectDB = "server=localhost;user=root;password=1234;database=unityproject";
        private static Dictionary<string, Room> rooms = new Dictionary<string, Room>();
        private static Dictionary<string, string> playerRooms = new Dictionary<string, string>();
        private static List<TcpClient> connectedClients = new List<TcpClient>();

        static void Main(string[] args)
        {
            TcpListener server = null;

            try
            {
                int port = 7777;
                IPAddress localAddr = IPAddress.Any;

                server = new TcpListener(localAddr, port);

                server.Start();

                Console.WriteLine("서버 시작");

                while (true)
                {
                    TcpClient client = server.AcceptTcpClient();

                    connectedClients.Add(client);
                    Console.WriteLine($"클라이언트 {client}가 연결되었습니다.");

                    Thread clientThread = new Thread(() => BroadcastClient(client));
                    clientThread.Start();

                }
            }
            catch (SocketException e)
            {
                Console.WriteLine($"SocketException : {e}");
            }
        }

        private static void BroadcastClient(TcpClient client)
        {

            NetworkStream stream = client.GetStream();
            byte[] bytes = new byte[8192];
            string data = null;
            int i;
            string playerId = null;

            try
            {
                while ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
                {
                    data = Encoding.UTF8.GetString(bytes, 0, i);
                    Console.WriteLine($"응답 : {data}");
                    string response = SendQuery(data);
                    Console.WriteLine($"응답 데이터: {response}");
                    byte[] msg = Encoding.UTF8.GetBytes(response);

                    var responseData = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                    if (responseData.TryGetValue("action", out object action))
                    {
                        if (action.ToString() == "login" && responseData["status"].ToString() == "success")
                        {
                            playerId = responseData["userId"].ToString();
                        }
                        else if (action.ToString() == "create_room" || action.ToString() == "join_room" || action.ToString() == "leave_room")
                        {
                            if (responseData["status"].ToString() == "success")
                            {

                                BroadcastRoomList(client);

                            }
                            else
                            {

                                stream.Write(msg, 0, msg.Length);
                            }
                        }

                        stream.Write(msg, 0, msg.Length);

                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error handling client: {e.Message}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(playerId))
                {
                    string leaveResponse = LeaveRoom(playerId);
                    if (leaveResponse.Contains("success"))
                    {
                        BroadcastRoomList(client);
                    }
                }

                connectedClients.Remove(client);
                stream.Close();
                client.Close();
            }
        }

        private static void BroadcastRoomList(TcpClient localClient)
        {
            string roomListResponse = GetRoomList();
            byte[] roomListMsg = Encoding.UTF8.GetBytes(roomListResponse);

            // 리스트가 중간에 터졌을 때를 방지하기위해 ToList() 사용
            foreach (TcpClient client in connectedClients.ToList())
            {
                try
                {
                    if (client.Connected && client != localClient)
                    {
                        NetworkStream stream = client.GetStream();
                        stream.Write(roomListMsg, 0, roomListMsg.Length);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"broadcast가 실패했습니다 : {e.Message}");
                    connectedClients.Remove(client);
                }
            }
        }


        private static string SendQuery(string data)
        {
            try
            {
                var request = JsonConvert.DeserializeObject<Dictionary<string, object>>(data);

                if (request.TryGetValue("action", out object requestAction) && requestAction != null)
                {
                    string action = requestAction.ToString();
                    string currentRoomName;

                    switch (action)
                    {
                        case "register":
                            if (request.TryGetValue("id", out object id) &&
                                request.TryGetValue("password", out object password) &&
                                request.TryGetValue("playername", out object playername))
                            {
                                return Register(id.ToString(), password.ToString(), playername.ToString());
                            }
                            break;
                        case "login":
                            if (request.TryGetValue("id", out object loginId) &&
                                request.TryGetValue("password", out object loginPassword))
                            {
                                return Login(loginId.ToString(), loginPassword.ToString());
                            }
                            break;
                        case "save":
                            if (request.TryGetValue("userId", out object userId) &&
                                request.TryGetValue("characterData", out object characterData))
                            {
                                return SavePlayerData(userId.ToString(), characterData.ToString());
                            }
                            break;
                        case "create_room":
                            if (request.TryGetValue("roomName", out object roomName) &&
                                request.TryGetValue("hostId", out object hostId) &&
                                request.TryGetValue("mapName", out object mapName))
                            {
                                return CreateRoom(roomName.ToString(), hostId.ToString(), mapName.ToString());
                            }
                            break;
                        case "join_room":
                            if (request.TryGetValue("roomName", out object joinRoomName) &&
                                request.TryGetValue("playerId", out object joinPlayerId))
                            {
                                return JoinRoom(joinRoomName.ToString(), joinPlayerId.ToString());
                            }
                            break;
                        case "leave_room":
                            if (request.TryGetValue("playerId", out object leavePlayerId))
                            {
                                return LeaveRoom(leavePlayerId.ToString());
                            }
                            break;
                        case "get_room_list":
                            return GetRoomList();
                        case "start_game":
                            if (request.TryGetValue("roomName", out object startRoomName) &&
                                request.TryGetValue("hostId", out object roomHostId) &&
                                request.TryGetValue("sceneName", out object sceneName))
                            {
                                return StartGame(startRoomName.ToString(), roomHostId.ToString(), sceneName.ToString());
                            }
                            break;
                        case "player_spawn":
                            {
                                string playerId = request["playerId"].ToString();
                                var position = JsonConvert.DeserializeObject<Dictionary<string, float>>(request["position"].ToString());
                                int maxHealth = Convert.ToInt32(request["maxHealth"]);
                                int attackPower = Convert.ToInt32(request["attackPower"]);

                                // 플레이어가 속한 방 찾기
                                if (playerRooms.TryGetValue(playerId, out currentRoomName) &&
                                    rooms.TryGetValue(currentRoomName, out Room room))
                                {
                                    // 플레이어 위치 정보 저장
                                    var playerPosition = new Vector3
                                    {
                                        x = position["x"],
                                        y = position["y"],
                                        z = position["z"]
                                    };
                                    room.PlayerPositions[playerId] = playerPosition;

                                    // 새로 들어온 플레이어에게 기존 플레이어들의 정보 전송
                                    foreach (var existingPlayer in room.Players)
                                    {
                                        if (existingPlayer != playerId && room.PlayerPositions.ContainsKey(existingPlayer))
                                        {
                                            var existingPlayerPos = room.PlayerPositions[existingPlayer];
                                            var existingPlayerMessage = JsonConvert.SerializeObject(new
                                            {
                                                action = "player_spawn",
                                                playerId = existingPlayer,
                                                position = existingPlayerPos,
                                                maxHealth = maxHealth,
                                                attackPower = attackPower
                                            });

                                            // 해당 방의 플레이어 중에서 `existingPlayer`에게만 전송
                                            var clientToSend = connectedClients.FirstOrDefault(c =>
                                                c.Connected &&
                                                playerRooms.ContainsKey(existingPlayer) &&
                                                playerRooms[existingPlayer] == currentRoomName);

                                            if (clientToSend != null)
                                            {
                                                try
                                                {
                                                    NetworkStream stream = clientToSend.GetStream();
                                                    byte[] messageBytes = Encoding.UTF8.GetBytes(existingPlayerMessage);
                                                    stream.Write(messageBytes, 0, messageBytes.Length);
                                                }
                                                catch (Exception e)
                                                {
                                                    Console.WriteLine($"스폰 메시지 전송 실패: {e.Message}");
                                                }
                                            }
                                        }
                                    }

                                    // 새 플레이어의 정보를 다른 플레이어들에게 전송
                                    var spawnMessage = JsonConvert.SerializeObject(new
                                    {
                                        action = "player_spawn",
                                        playerId = playerId,
                                        position = playerPosition,
                                        maxHealth = maxHealth,
                                        attackPower = attackPower
                                    });

                                    foreach (var client in connectedClients)
                                    {
                                        try
                                        {
                                            if (client.Connected && room.Players.Contains(playerId))
                                            {
                                                NetworkStream stream = client.GetStream();
                                                byte[] messageBytes = Encoding.UTF8.GetBytes(spawnMessage);
                                                stream.Write(messageBytes, 0, messageBytes.Length);
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            Console.WriteLine($"스폰 메시지 전송 실패: {e.Message}");
                                        }
                                    }
                                }

                                return JsonConvert.SerializeObject(new { status = "success" });
                            }

                        case "player_state":
                            {
                                string playerId = request["playerId"].ToString();

                                // 플레이어의 방을 찾고, 방에 있는 플레이어들에게만 메시지를 전송
                                if (playerRooms.TryGetValue(playerId, out currentRoomName) && rooms.TryGetValue(currentRoomName, out Room room))
                                {
                                    foreach (var targetPlayerId in room.Players)
                                    {
                                        if (targetPlayerId != playerId) // 본인 제외
                                        {
                                            var targetClient = connectedClients.FirstOrDefault(c => playerRooms.ContainsKey(targetPlayerId) && playerRooms[targetPlayerId] == currentRoomName);
                                            if (targetClient != null && targetClient.Connected)
                                            {
                                                try
                                                {
                                                    NetworkStream stream = targetClient.GetStream();
                                                    byte[] messageBytes = Encoding.UTF8.GetBytes(data);
                                                    stream.Write(messageBytes, 0, messageBytes.Length);
                                                }
                                                catch (Exception e)
                                                {
                                                    Console.WriteLine($"상태 메시지 전송 실패: {e.Message}");
                                                }
                                            }
                                        }
                                    }
                                }
                                return JsonConvert.SerializeObject(new { status = "success" });
                            }


                        case "player_action":
                            {
                                string playerId = request["playerId"].ToString();
                                string actionName = request["actionName"].ToString();

                                // 플레이어가 속한 방 찾기
                                if (playerRooms.TryGetValue(playerId, out currentRoomName) && rooms.TryGetValue(currentRoomName, out Room room))
                                {
                                    var actionMessage = JsonConvert.SerializeObject(new
                                    {
                                        action = "player_action",
                                        playerId = playerId,
                                        actionName = actionName
                                    });

                                    byte[] messageBytes = Encoding.UTF8.GetBytes(actionMessage);

                                    // 같은 방에 있는 플레이어들에게만 전송
                                    foreach (var targetPlayerId in room.Players)
                                    {
                                        if (targetPlayerId != playerId) // 본인 제외
                                        {
                                            var targetClient = connectedClients.FirstOrDefault(c => playerRooms.ContainsKey(targetPlayerId) && playerRooms[targetPlayerId] == currentRoomName);
                                            if (targetClient != null && targetClient.Connected)
                                            {
                                                try
                                                {
                                                    NetworkStream stream = targetClient.GetStream();
                                                    stream.Write(messageBytes, 0, messageBytes.Length);
                                                }
                                                catch (Exception e)
                                                {
                                                    Console.WriteLine($"액션 메시지 전송 실패: {e.Message}");
                                                }
                                            }
                                        }
                                    }
                                }
                                return JsonConvert.SerializeObject(new { status = "success" });
                            }
                    }
                }

                return JsonConvert.SerializeObject(new { status = "error", message = "정의되지 않은 유형입니다." });

            }
            catch (Exception ex)
            {

                return JsonConvert.SerializeObject(new { status = "error", message = ex.Message });
            }

        }

        private static string Register(string id, string passWord, string playerName)
        {
            using (MySqlConnection connection = new MySqlConnection(connectDB))
            {
                try
                {
                    connection.Open();

                    using (MySqlTransaction transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            string query = "INSERT INTO players (id, password, playername) VALUES (@id, @password, @playername)";
                            using (MySqlCommand command = new MySqlCommand(query, connection))
                            {
                                command.Parameters.AddWithValue("@id", id);
                                command.Parameters.AddWithValue("@password", passWord);
                                command.Parameters.AddWithValue("@playername", playerName);
                                command.ExecuteNonQuery();
                            }

                            query = "INSERT INTO character_data (player_id, player_name) VALUES (@player_id, @player_name)";
                            using (MySqlCommand command = new MySqlCommand(query, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@player_id", id);
                                command.Parameters.AddWithValue("@player_name", playerName);
                                command.ExecuteNonQuery();
                            }

                            transaction.Commit();

                            return JsonConvert.SerializeObject(new { status = "success", action = "register", message = $"ID : {id}, playername : {playerName} 회원가입 성공" });

                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
                catch (MySqlException ex)
                {
                    if (ex.Number == 1062)
                    {
                        return JsonConvert.SerializeObject(new { status = "error", message = "ID가 이미 존재합니다." });
                    }

                    return JsonConvert.SerializeObject(new { status = "error", message = ex.Message });
                }

            }
        }

        private static string Login(string id, string passWord)
        {
            using (MySqlConnection connection = new MySqlConnection(connectDB))
            {
                try
                {
                    connection.Open();

                    string query = "SELECT * FROM players WHERE id = @id AND password = @password";
                    using (MySqlCommand Command = new MySqlCommand(query, connection))
                    {
                        Command.Parameters.AddWithValue("@id", id);
                        Command.Parameters.AddWithValue("@password", passWord);
                        using (MySqlDataReader reader = Command.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                return JsonConvert.SerializeObject(new { status = "error", message = "아이디 또는 비밀번호가 잘못되었습니다." });
                            }
                        }
                    }

                    query = "SELECT * FROM character_data WHERE player_id = @playerId";
                    using (MySqlCommand Command = new MySqlCommand(query, connection))
                    {
                        Command.Parameters.AddWithValue("@playerId", id);
                        using (MySqlDataReader reader = Command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var characterData = new
                                {
                                    PlayerName = reader["player_name"].ToString(),
                                    PlayerId = reader["player_id"].ToString(),
                                    Gems = Convert.ToInt32(reader["gems"]),
                                    Coins = Convert.ToInt32(reader["coins"]),
                                    MaxHealth = Convert.ToInt32(reader["max_health"]),
                                    HealthEnhancement = Convert.ToInt32(reader["health_enhancement"]),
                                    AttackPower = Convert.ToInt32(reader["attack_power"]),
                                    AttackEnhancement = Convert.ToInt32(reader["attack_enhancement"]),
                                    WeaponEnhancement = Convert.ToInt32(reader["weapon_enhancement"]),
                                    ArmorEnhancement = Convert.ToInt32(reader["armor_enhancement"])
                                };

                                return JsonConvert.SerializeObject(new
                                {
                                    status = "success",
                                    action = "login",
                                    message = "로그인 성공",
                                    userId = id,
                                    character = characterData
                                });
                            }
                            else
                            {
                                return JsonConvert.SerializeObject(new { status = "error", message = "캐릭터 데이터를 찾을 수 없습니다." });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { status = "error", message = ex.Message });
                }
            }
        }

        private static string SavePlayerData(string userId, string characterDataJson)
        {
            using (MySqlConnection connection = new MySqlConnection(connectDB))
            {
                try
                {
                    connection.Open();
                    var characterData = JsonConvert.DeserializeObject<Dictionary<string, object>>(characterDataJson);

                    string query = @"
                        UPDATE character_data 
                        SET player_name = @playerName, 
                            gems = @gems, 
                            coins = @coins, 
                            max_health = @maxHealth, 
                            health_enhancement = @healthEnhancement, 
                            attack_power = @attackPower, 
                            attack_enhancement = @attackEnhancement, 
                            weapon_enhancement = @weaponEnhancement, 
                            armor_enhancement = @armorEnhancement 
                        WHERE player_id = @playerId";

                    using (MySqlCommand command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@playerId", userId);
                        command.Parameters.AddWithValue("@playerName", characterData["PlayerName"]);
                        command.Parameters.AddWithValue("@gems", characterData["Gems"]);
                        command.Parameters.AddWithValue("@coins", characterData["Coins"]);
                        command.Parameters.AddWithValue("@maxHealth", characterData["MaxHealth"]);
                        command.Parameters.AddWithValue("@healthEnhancement", characterData["HealthEnhancement"]);
                        command.Parameters.AddWithValue("@attackPower", characterData["AttackPower"]);
                        command.Parameters.AddWithValue("@attackEnhancement", characterData["AttackEnhancement"]);
                        command.Parameters.AddWithValue("@weaponEnhancement", characterData["WeaponEnhancement"]);
                        command.Parameters.AddWithValue("@armorEnhancement", characterData["ArmorEnhancement"]);

                        int rowsAffected = command.ExecuteNonQuery();

                        if (rowsAffected > 0)
                        {
                            return JsonConvert.SerializeObject(new { status = "success", message = "Player data saved successfully" });
                        }
                        else
                        {
                            return JsonConvert.SerializeObject(new { status = "error", message = "No data was updated" });
                        }
                    }
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { status = "error", message = ex.Message });
                }
            }
        }

        private static string CreateRoom(string roomName, string hostId, string mapName)
        {
            if (rooms.ContainsKey(roomName))
            {
                return JsonConvert.SerializeObject(new { status = "error", message = "이미 존재하는 방 이름입니다." });
            }

            if (playerRooms.ContainsKey(hostId))
            {
                return JsonConvert.SerializeObject(new { status = "error", message = "플레이어가 이미 방에 있습니다." });
            }

            var newRoom = new Room
            {
                Name = roomName,
                MaxPlayers = 4,
                HostId = hostId,
                Players = new List<string> { hostId },
                MapName = mapName
            };

            rooms[roomName] = newRoom;
            playerRooms[hostId] = roomName;

            return JsonConvert.SerializeObject(new { status = "success", action = "create_room", message = "방 생성 성공", room = newRoom });
        }

        private static string JoinRoom(string roomName, string playerId)
        {
            if (!rooms.TryGetValue(roomName, out Room room))
            {
                return JsonConvert.SerializeObject(new { status = "error", message = "방을 찾을 수 없습니다." });
            }

            if (room.Players.Count >= room.MaxPlayers)
            {
                return JsonConvert.SerializeObject(new { status = "error", message = "방이 가득 찼습니다." });
            }

            if (playerRooms.ContainsKey(playerId))
            {
                return JsonConvert.SerializeObject(new { status = "error", message = "플레이어가 이미 방에 있습니다." });
            }

            room.Players.Add(playerId);
            playerRooms[playerId] = roomName;

            return JsonConvert.SerializeObject(new { status = "success", action = "join_room", message = "방 참가 성공", room = room });
        }

        private static string LeaveRoom(string playerId)
        {
            if (!playerRooms.TryGetValue(playerId, out string roomName))
            {
                return JsonConvert.SerializeObject(new { status = "error", message = "플레이어가 어떤 방에도 속해있지 않습니다." });
            }

            if (!rooms.TryGetValue(roomName, out Room room))
            {
                return JsonConvert.SerializeObject(new { status = "error", message = "방을 찾을 수 없습니다." });
            }

            room.Players.Remove(playerId);
            if (room.PlayerPositions.ContainsKey(playerId))
            {
                room.PlayerPositions.Remove(playerId);
            }
            playerRooms.Remove(playerId);

            if (room.Players.Count == 0)
            {
                rooms.Remove(roomName);
                return JsonConvert.SerializeObject(new { status = "success", action = "leave_room", message = "마지막 플레이어가 방을 나가 방이 삭제되었습니다." });
            }

            if (room.HostId == playerId)
            {
                room.HostId = room.Players[0];
            }

            return JsonConvert.SerializeObject(new { status = "success", action = "leave_room", message = "방 퇴장 성공", room = room });
        }

        private static string StartGame(string roomName, string hostId, string sceneName)
        {
            if (!rooms.TryGetValue(roomName, out Room room))
            {
                return JsonConvert.SerializeObject(new { status = "error", message = "방을 찾을 수 없습니다." });
            }

            if (room.HostId != hostId)
            {
                return JsonConvert.SerializeObject(new { status = "error", message = "방장만 게임을 시작할 수 있습니다." });
            }

            var startGameMessage = JsonConvert.SerializeObject(new
            {
                status = "success",
                action = "start_game",
                message = "게임을 시작합니다.",
                sceneName = sceneName
            });

            // 방의 모든 클라이언트에게 게임 시작 메시지 브로드캐스트
            foreach (TcpClient client in connectedClients.ToList())
            {
                try
                {
                    if (client.Connected)
                    {
                        NetworkStream stream = client.GetStream();
                        byte[] messageBytes = Encoding.UTF8.GetBytes(startGameMessage);
                        stream.Write(messageBytes, 0, messageBytes.Length);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"게임 시작 메시지 전송 실패: {e.Message}");
                    connectedClients.Remove(client);
                }
            }

            return startGameMessage;
        }
        private static void BroadcastToRoom(Room room, string message)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            foreach (var playerId in room.Players)
            {
                var client = connectedClients.FirstOrDefault(c => playerRooms.ContainsKey(playerId) && playerRooms[playerId] == room.Name);
                if (client != null && client.Connected)
                {
                    try
                    {
                        NetworkStream stream = client.GetStream();
                        stream.Write(messageBytes, 0, messageBytes.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"스폰 메시지 전송 실패: {ex.Message}");
                    }
                }
            }
        }

        private static void SendToClient(TcpClient client, string message)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            try
            {
                NetworkStream stream = client.GetStream();
                stream.Write(messageBytes, 0, messageBytes.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"클라이언트 메시지 전송 실패: {ex.Message}");
            }
        }




        private static string GetRoomList()
        {
            return JsonConvert.SerializeObject(new { status = "success", action = "get_room_list", rooms = rooms.Values });
        }

    }
}
