using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TcpServer
{
    #region Models
    public class Room
    {
        public string Name { get; set; }
        public int MaxPlayers { get; set; } = 4;
        public string HostId { get; set; }
        public List<string> Players { get; set; } = new();
        public string MapName { get; set; }
        public Dictionary<string, int> SpawnIndexes { get; set; } = new();

        public int GetNextSpawnIndex()
        {
            var usedIndexes = SpawnIndexes.Values.ToHashSet();
            for (int i = 0; i < MaxPlayers; i++)
            {
                if (!usedIndexes.Contains(i))
                    return i;
            }
            return 0;
        }
    }

    public class PlayerCharacterData
    {
        public string PlayerName { get; set; }
        public string PlayerId { get; set; }
        public int Gems { get; set; }
        public int Coins { get; set; }
        public int MaxHealth { get; set; }
        public int HealthEnhancement { get; set; }
        public int AttackPower { get; set; }
        public int AttackEnhancement { get; set; }
        public int WeaponEnhancement { get; set; }
        public int ArmorEnhancement { get; set; }
    }
    #endregion

    #region Network
    public class NetworkBuffer
    {
        private const int BufferSize = 8192;
        private byte[] buffer;

        public NetworkBuffer()
        {
            buffer = new byte[BufferSize];
        }

        public byte[] Buffer => buffer;
        public int Size => BufferSize;
    }

    public class TcpServerConnection
    {
        private TcpClient client;
        private NetworkBuffer buffer;
        private NetworkStream stream;
        private string playerId;

        public TcpServerConnection(TcpClient client)
        {
            this.client = client;
            this.buffer = new NetworkBuffer();
            this.stream = client.GetStream();
        }

        public string PlayerId
        {
            get => playerId;
            set => playerId = value;
        }

        public bool IsConnected => client?.Connected ?? false;

        public async Task Send(byte[] data)
        {
            if (IsConnected)
            {
                await stream.WriteAsync(data, 0, data.Length);
            }
        }

        public async Task<(int bytesRead, byte[] data)> Receive()
        {
            int bytesRead = await stream.ReadAsync(buffer.Buffer, 0, buffer.Size);
            return (bytesRead, buffer.Buffer);
        }

        public void Close()
        {
            stream?.Close();
            client?.Close();
        }
    }
    #endregion

    #region Database
    public class DatabaseManager
    {
        private readonly string connectionString;

        public DatabaseManager(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public async Task<(bool success, string message, PlayerCharacterData data)> Login(string id, string password)
        {
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            // Verify login credentials
            var loginCommand = new MySqlCommand(
                "SELECT * FROM players WHERE id = @id AND password = @password",
                connection);
            loginCommand.Parameters.AddWithValue("@id", id);
            loginCommand.Parameters.AddWithValue("@password", password);

            using var reader = await loginCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return (false, "아이디 또는 비밀번호가 잘못되었습니다.", null);
            }
            reader.Close();

            // Get character data
            var characterCommand = new MySqlCommand(
                "SELECT * FROM character_data WHERE player_id = @playerId",
                connection);
            characterCommand.Parameters.AddWithValue("@playerId", id);

            using var characterReader = await characterCommand.ExecuteReaderAsync();
            if (await characterReader.ReadAsync())
            {
                var characterData = new PlayerCharacterData
                {
                    PlayerName = characterReader["player_name"].ToString(),
                    PlayerId = characterReader["player_id"].ToString(),
                    Gems = Convert.ToInt32(characterReader["gems"]),
                    Coins = Convert.ToInt32(characterReader["coins"]),
                    MaxHealth = Convert.ToInt32(characterReader["max_health"]),
                    HealthEnhancement = Convert.ToInt32(characterReader["health_enhancement"]),
                    AttackPower = Convert.ToInt32(characterReader["attack_power"]),
                    AttackEnhancement = Convert.ToInt32(characterReader["attack_enhancement"]),
                    WeaponEnhancement = Convert.ToInt32(characterReader["weapon_enhancement"]),
                    ArmorEnhancement = Convert.ToInt32(characterReader["armor_enhancement"])
                };

                return (true, "로그인 성공", characterData);
            }

            return (false, "캐릭터 데이터를 찾을 수 없습니다.", null);
        }

        public async Task<(bool success, string message)> Register(string id, string password, string playerName)
        {
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Insert player account
                var playerCommand = new MySqlCommand(
                    "INSERT INTO players (id, password, playername) VALUES (@id, @password, @playername)",
                    connection, transaction);
                playerCommand.Parameters.AddWithValue("@id", id);
                playerCommand.Parameters.AddWithValue("@password", password);
                playerCommand.Parameters.AddWithValue("@playername", playerName);
                await playerCommand.ExecuteNonQueryAsync();

                // Insert initial character data
                var characterCommand = new MySqlCommand(
                    "INSERT INTO character_data (player_id, player_name) VALUES (@player_id, @player_name)",
                    connection, transaction);
                characterCommand.Parameters.AddWithValue("@player_id", id);
                characterCommand.Parameters.AddWithValue("@player_name", playerName);
                await characterCommand.ExecuteNonQueryAsync();

                await transaction.CommitAsync();
                return (true, $"ID : {id}, playername : {playerName} 회원가입 성공");
            }
            catch (MySqlException ex)
            {
                await transaction.RollbackAsync();
                if (ex.Number == 1062)
                {
                    return (false, "ID가 이미 존재합니다.");
                }
                return (false, ex.Message);
            }
        }

        public async Task<(bool success, string message)> SavePlayerData(string userId, PlayerCharacterData data)
        {
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            var command = new MySqlCommand(@"
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
                WHERE player_id = @playerId", connection);

            command.Parameters.AddWithValue("@playerId", userId);
            command.Parameters.AddWithValue("@playerName", data.PlayerName);
            command.Parameters.AddWithValue("@gems", data.Gems);
            command.Parameters.AddWithValue("@coins", data.Coins);
            command.Parameters.AddWithValue("@maxHealth", data.MaxHealth);
            command.Parameters.AddWithValue("@healthEnhancement", data.HealthEnhancement);
            command.Parameters.AddWithValue("@attackPower", data.AttackPower);
            command.Parameters.AddWithValue("@attackEnhancement", data.AttackEnhancement);
            command.Parameters.AddWithValue("@weaponEnhancement", data.WeaponEnhancement);
            command.Parameters.AddWithValue("@armorEnhancement", data.ArmorEnhancement);

            int rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0
                ? (true, "Player data saved successfully")
                : (false, "No data was updated");
        }


        public async Task<PlayerCharacterData> GetCharacterData(string playerId)
        {
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            var characterCommand = new MySqlCommand(
                "SELECT * FROM character_data WHERE player_id = @playerId",
                connection);
            characterCommand.Parameters.AddWithValue("@playerId", playerId);

            using var characterReader = await characterCommand.ExecuteReaderAsync();
            if (await characterReader.ReadAsync())
            {
                return new PlayerCharacterData
                {
                    PlayerName = characterReader["player_name"].ToString(),
                    PlayerId = characterReader["player_id"].ToString(),
                    Gems = Convert.ToInt32(characterReader["gems"]),
                    Coins = Convert.ToInt32(characterReader["coins"]),
                    MaxHealth = Convert.ToInt32(characterReader["max_health"]),
                    HealthEnhancement = Convert.ToInt32(characterReader["health_enhancement"]),
                    AttackPower = Convert.ToInt32(characterReader["attack_power"]),
                    AttackEnhancement = Convert.ToInt32(characterReader["attack_enhancement"]),
                    WeaponEnhancement = Convert.ToInt32(characterReader["weapon_enhancement"]),
                    ArmorEnhancement = Convert.ToInt32(characterReader["armor_enhancement"])
                };
            }
            return null;
        }
    }


    #endregion

    #region Game Server
    public class TcpGameServer
    {
        private readonly TcpListener listener;
        private readonly List<TcpServerConnection> connections;
        private readonly Dictionary<string, Room> rooms;
        private readonly Dictionary<string, string> playerRooms;
        private readonly DatabaseManager database;
        private readonly int port;
        private bool isRunning;
        private DateTime lastBroadcastTime = DateTime.UtcNow;
        private readonly TimeSpan broadcastInterval = TimeSpan.FromMilliseconds(250);


        public TcpGameServer(string dbConnectionString, int port = 7777)
        {
            this.port = port;
            listener = new TcpListener(IPAddress.Any, port);
            connections = new List<TcpServerConnection>();
            rooms = new Dictionary<string, Room>();
            playerRooms = new Dictionary<string, string>();
            database = new DatabaseManager(dbConnectionString);
        }

        public async Task Start()
        {
            try
            {
                // Bind & Listen
                listener.Start();
                isRunning = true;
                Console.WriteLine($"서버가 포트 {port}에서 시작되었습니다.");

                // Accept Loop
                while (isRunning)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    var connection = new TcpServerConnection(client);
                    connections.Add(connection);
                    Console.WriteLine($"새로운 클라이언트가 연결되었습니다.");
                    _ = ClientConnect(connection);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"서버 에러: {ex.Message}");
                Stop();
            }
        }

        public void Stop()
        {
            isRunning = false;
            listener?.Stop();
            foreach (var connection in connections)
            {
                connection.Close();
            }
            connections.Clear();
        }

        private async Task ClientConnect(TcpServerConnection connection)
        {
            try
            {
                while (connection.IsConnected)
                {
                    var (bytesRead, buffer) = await connection.Receive();
                    if (bytesRead == 0) break;

                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var response = await ActionResponse(message, connection);
                    var responseBytes = Encoding.UTF8.GetBytes(response);
                    await connection.Send(responseBytes);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"클라이언트 처리 중 에러: {ex.Message}");
            }
            finally
            {
                await Disconnect(connection);
            }
        }

        private async Task Disconnect(TcpServerConnection connection)
        {
            if (connection.PlayerId != null && playerRooms.TryGetValue(connection.PlayerId, out _))
            {
                string requestJson = JsonConvert.SerializeObject(new { action = "leave_room", playerId = connection.PlayerId });
                var request = JsonConvert.DeserializeObject<Dictionary<string, object>>(requestJson);
                await LeaveRoom(request);
            }

            connections.Remove(connection);
            connection.Close();
        }

        private async Task<string> ActionResponse(string message, TcpServerConnection connection)
        {
            try
            {
                var request = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
                if (!request.TryGetValue("action", out var actionElement))
                {
                    return ErrorResponse("Action not specified");
                }

                string action = actionElement.ToString();
                return action switch
                {
                    "register" => await Register(request),
                    "login" => await Login(request, connection),
                    "save" => await SaveData(request),
                    "create_room" => await CreateRoom(request),
                    "join_room" => await JoinRoom(request),
                    "leave_room" => await LeaveRoom(request),
                    "start_game" => await StartGame(request),
                    "get_room_list" => await GetRoomList(),
                    "player_spawn" => await PlayerSpawn(request),
                    "player_state" => await PlayerState(request),
                    "player_action" => await PlayerAction(request),
                    _ => ErrorResponse("Unknown action")
                };
            }
            catch (Exception ex)
            {
                return ErrorResponse(ex.Message);
            }
        }

        #region Message Handlers
        private async Task<string> Register(Dictionary<string, object> request)
        {
            string id = request["id"].ToString();
            string password = request["password"].ToString();
            string playerName = request["playername"].ToString();

            var (success, message) = await database.Register(id, password, playerName);
            return Response("register", success, message);
        }

        private async Task<string> Login(Dictionary<string, object> request, TcpServerConnection connection)
        {
            string id = request["id"].ToString();
            string password = request["password"].ToString();

            var (success, message, characterData) = await database.Login(id, password);
            if (success)
            {
                connection.PlayerId = id;
                return JsonConvert.SerializeObject(new
                {
                    status = "success",
                    action = "login",
                    message,
                    userId = id,
                    character = characterData
                });
            }
            return ErrorResponse(message);
        }

        //private async Task<string> SaveData(Dictionary<string, object> request)
        //{
        //    try
        //    {
        //        string userId = request["userId"].ToString();
        //        string characterDataJson = request["characterData"].GetRawText();
        //        var characterData = new PlayerCharacterData
        //        {
        //            PlayerName = request["characterData"].GetProperty("PlayerName").GetString(),
        //            PlayerId = request["characterData"].GetProperty("PlayerId").GetString(),
        //            Gems = request["characterData"].GetProperty("Gems").GetInt32(),
        //            Coins = request["characterData"].GetProperty("Coins").GetInt32(),
        //            MaxHealth = request["characterData"].GetProperty("MaxHealth").GetInt32(),
        //            HealthEnhancement = request["characterData"].GetProperty("HealthEnhancement").GetInt32(),
        //            AttackPower = request["characterData"].GetProperty("AttackPower").GetInt32(),
        //            AttackEnhancement = request["characterData"].GetProperty("AttackEnhancement").GetInt32(),
        //            WeaponEnhancement = request["characterData"].GetProperty("WeaponEnhancement").GetInt32(),
        //            ArmorEnhancement = request["characterData"].GetProperty("ArmorEnhancement").GetInt32()
        //        };

        //        var (success, message) = await database.SavePlayerData(userId, characterData);
        //        return Response("save", success, message);
        //    }
        //    catch (Exception ex)
        //    {
        //        return ErrorResponse($"Failed to save player data: {ex.Message}");
        //    }
        //}

        private async Task<string> SaveData(Dictionary<string, object> request)
        {
            try
            {
                string userId = request["userId"].ToString();
                var characterData = JsonConvert.DeserializeObject<PlayerCharacterData>(JsonConvert.SerializeObject(request["characterData"]));
                var (success, message) = await database.SavePlayerData(userId, characterData);
                return Response("save", success, message);
            }
            catch (Exception ex)
            {
                return ErrorResponse($"Failed to save player data: {ex.Message}");
            }
        }


        private async Task<string> CreateRoom(Dictionary<string, object> request)
        {
            string roomName = request["roomName"].ToString();
            string hostId = request["hostId"].ToString();
            string mapName = request["mapName"].ToString();

            if (rooms.ContainsKey(roomName))
            {
                return ErrorResponse("이미 존재하는 방 이름입니다.");
            }

            if (playerRooms.ContainsKey(hostId))
            {
                return ErrorResponse("플레이어가 이미 방에 있습니다.");
            }

            var room = new Room
            {
                Name = roomName,
                HostId = hostId,
                Players = new List<string> { hostId },
                MapName = mapName
            };

            room.SpawnIndexes[hostId] = room.GetNextSpawnIndex();

            rooms[roomName] = room;
            playerRooms[hostId] = roomName;

            await BroadcastRoomList();
            return Response("create_room", true, "방 생성 성공", new { room });
        }

        private async Task<string> JoinRoom(Dictionary<string, object> request)
        {
            string roomName = request["roomName"].ToString();
            string playerId = request["playerId"].ToString();

            if (!rooms.TryGetValue(roomName, out Room room))
            {
                return ErrorResponse("방을 찾을 수 없습니다.");
            }

            if (room.Players.Count >= room.MaxPlayers)
            {
                return ErrorResponse("방이 가득 찼습니다.");
            }

            if (playerRooms.ContainsKey(playerId))
            {
                return ErrorResponse("플레이어가 이미 방에 있습니다.");
            }

            room.SpawnIndexes[playerId] = room.GetNextSpawnIndex();
            room.Players.Add(playerId);
            playerRooms[playerId] = roomName;

            await BroadcastRoomList();
            return Response("join_room", true, "방 참가 성공", new { room });
        }

        private async Task<string> LeaveRoom(Dictionary<string, object> request)
        {
            string playerId = request["playerId"].ToString();

            if (!playerRooms.TryGetValue(playerId, out string roomName))
            {
                return ErrorResponse("플레이어가 어떤 방에도 속해있지 않습니다.");
            }

            if (!rooms.TryGetValue(roomName, out Room room))
            {
                return ErrorResponse("방을 찾을 수 없습니다.");
            }

            room.Players.Remove(playerId);
            playerRooms.Remove(playerId);

            if (room.Players.Count == 0)
            {
                rooms.Remove(roomName);
                return Response("leave_room", true, "마지막 플레이어가 방을 나가 방이 삭제되었습니다.");
            }

            if (room.HostId == playerId)
            {
                room.HostId = room.Players[0];
            }

            await BroadcastRoomList();
            return Response("leave_room", true, "방 퇴장 성공", new { room });
        }


        private async Task<string> StartGame(Dictionary<string, object> request)
        {
            string roomName = request["roomName"].ToString();
            string hostId = request["hostId"].ToString();
            string sceneName = request["sceneName"].ToString();

            if (!rooms.TryGetValue(roomName, out Room room))
            {
                return ErrorResponse("방을 찾을 수 없습니다.");
            }

            if (room.HostId != hostId)
            {
                return ErrorResponse("방장만 게임을 시작할 수 있습니다.");
            }

            // 방의 모든 플레이어에게 한 번만 메시지 전송
            var startGameMessage = JsonConvert.SerializeObject(new
            {
                status = "success",
                action = "start_game",
                message = "게임을 시작합니다.",
                sceneName = sceneName,
                players = room.Players // 참여 중인 플레이어 목록 포함
            });

            // 방에 있는 모든 플레이어에게 메시지를 한 번만 전송
            await BroadcastToRoom(roomName, startGameMessage);

            return startGameMessage;
        }

        private async Task<string> GetRoomList()
        {
            return Response("get_room_list", true, "", rooms.Values);
        }

        private async Task<string> PlayerSpawn(Dictionary<string, object> request)
        {
            try
            {
                string playerId = request["playerId"].ToString();
                Console.WriteLine($"Received spawn request from: {playerId}");

                if (!playerRooms.TryGetValue(playerId, out string roomName))
                {
                    Console.WriteLine("방에 사람이 없습니다.");
                    return ErrorResponse("Room not found for player");
                }

                if (!rooms.TryGetValue(roomName, out Room room))
                {
                    Console.WriteLine("방이 없습니다.");
                    return ErrorResponse("Room not found for player");
                }

                // 해당 플레이어의 스폰 인덱스 가져오기
                int spawnIndex = room.SpawnIndexes[playerId];

                // 현재 플레이어의 능력치를 DB에서 조회 (Login 대신 GetCharacterData 사용)
                var playerCharacterData = await database.GetCharacterData(playerId);

                if (playerCharacterData == null)
                {
                    Console.WriteLine($"Cannot find character data for player: {playerId}");
                    return ErrorResponse("Cannot find player character data");
                }

                // 새로 접속한 플레이어의 스폰 정보
                var spawnData = new Dictionary<string, object>
        {
            { "status", "success" },
            { "action", "player_spawn" },
            { "playerId", playerId },
            { "spawnIndex", spawnIndex },
            { "maxHealth", playerCharacterData.MaxHealth },
            { "attackPower", playerCharacterData.AttackPower }
        };

                // 모든 플레이어에게 새로운 플레이어의 스폰을 알림
                string spawnMessage = JsonConvert.SerializeObject(spawnData);
                await BroadcastToRoom(roomName, spawnMessage);
                Console.WriteLine($"Broadcasting new player spawn: {playerId}");

                // 새로운 플레이어에게 기존 플레이어들의 정보 전송
                foreach (var existingPlayerId in room.Players)
                {
                    if (existingPlayerId != playerId)
                    {
                        var existingPlayerData = await database.GetCharacterData(existingPlayerId);
                        if (existingPlayerData == null) continue;

                        var existingPlayerSpawnData = new Dictionary<string, object>
                {
                    { "status", "success" },
                    { "action", "player_spawn" },
                    { "playerId", existingPlayerId },
                    { "spawnIndex", room.SpawnIndexes[existingPlayerId] },
                    { "maxHealth", existingPlayerData.MaxHealth },
                    { "attackPower", existingPlayerData.AttackPower }
                };

                        string existingPlayerMessage = JsonConvert.SerializeObject(existingPlayerSpawnData);
                        var newPlayerConnection = connections.FirstOrDefault(c => c.PlayerId == playerId);
                        if (newPlayerConnection != null)
                        {
                            await newPlayerConnection.Send(Encoding.UTF8.GetBytes(existingPlayerMessage));
                        }
                    }
                }

                Console.WriteLine($"{playerId}가 성공적으로 생성되었습니다.");
                return spawnMessage;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in PlayerSpawn: {ex.Message}");
                return ErrorResponse($"Spawn failed: {ex.Message}");
            }
        }

        private async Task<string> PlayerState(Dictionary<string, object> request)
        {
            string playerId = request["playerId"].ToString();
            if (playerRooms.TryGetValue(playerId, out string roomName))
            {
                // Response를 통한 응답 구조 통일
                var stateResponse = new Dictionary<string, object>
                {
                    { "status", "success" },
                    { "action", "player_state" },
                    { "playerId", playerId },
                    { "position", request["position"] },
                    { "rotation", request["rotation"] },
                    { "isRunning", request["isRunning"] },
                    { "isAction", request["isAction"] },
                    { "currentHealth", request["currentHealth"] },
                    { "maxHealth", request["maxHealth"] },
                    { "attackPower", request["attackPower"] }
                };

                string response = JsonConvert.SerializeObject(stateResponse);
                await BroadcastToRoom(roomName, response);
            }
            return Response("player_state", true, "");
        }

        private async Task<string> PlayerAction(Dictionary<string, object> request)
        {
            string playerId = request["playerId"].ToString();
            if (playerRooms.TryGetValue(playerId, out string roomName))
            {
                await BroadcastToRoom(roomName, JsonConvert.SerializeObject(request));
            }
            return Response("player_action", true, "");
        }
        #endregion

        #region Broadcasting
        private async Task BroadcastToRoom(string roomName, string message)
        {
            if (!rooms.TryGetValue(roomName, out Room room)) return;

            // player_state 메시지일 경우에만 쓰로틀링 적용
            if (message.Contains("\"action\":\"player_state\""))
            {
                var now = DateTime.UtcNow;
                if (now - lastBroadcastTime < broadcastInterval)
                {
                    return; // 너무 빠른 브로드캐스트는 스킵
                }
                lastBroadcastTime = now;
            }

            var messageBytes = Encoding.UTF8.GetBytes(message);
            Console.WriteLine($"Broadcasting to room {roomName} - Message: {message}");
            Console.WriteLine($"Players in room: {string.Join(", ", room.Players)}");

            foreach (var connection in connections.ToList())
            {
                if (!connection.IsConnected) continue;

                Console.WriteLine($"Checking connection for player {connection.PlayerId}");
                if (room.Players.Contains(connection.PlayerId))
                {
                    Console.WriteLine($"Sending message to player {connection.PlayerId}");
                    try
                    {
                        await connection.Send(messageBytes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error sending message to {connection.PlayerId}: {ex.Message}");
                    }
                }
            }
        }

        private async Task BroadcastRoomList()
        {
            var roomList = Response("get_room_list", true, "", rooms.Values);
            var messageBytes = Encoding.UTF8.GetBytes(roomList);

            foreach (var connection in connections.ToList())
            {
                if (connection.IsConnected)
                {
                    await connection.Send(messageBytes);
                }
            }
        }
        #endregion

        #region Helper Methods
        private string Response(string action, bool success, string message, object data = null)
        {
            // 기본 응답 구조 생성
            var response = new Dictionary<string, object>
        {
            { "status", success ? "success" : "error" },
            { "action", action },
            { "message", message }
        };

            // rooms 목록인 경우 특별 처리
            if (action == "get_room_list" && data != null)
            {
                response["rooms"] = data;
                return JsonConvert.SerializeObject(response);
            }

            // 게임 시작인 경우 특별 처리
            if (action == "start_game" && data != null)
            {
                var gameData = data as Dictionary<string, object>;
                if (gameData != null && gameData.ContainsKey("sceneName"))
                {
                    response["sceneName"] = gameData["sceneName"];
                }
                return JsonConvert.SerializeObject(response);
            }

            //if (action == "player_spawn")
            //{
            //    var spawnData = data as Dictionary<string, object>;
            //    if (spawnData != null)
            //    {
            //        foreach (var kvp in spawnData)
            //        {
            //            response[kvp.Key] = kvp.Value;
            //        }
            //    }
            //}

            // 그 외 데이터가 있는 경우
            if (data != null)
            {
                foreach (var prop in data.GetType().GetProperties())
                {
                    response[prop.Name.ToLower()] = prop.GetValue(data);
                }
            }

            return JsonConvert.SerializeObject(response);
        }

        private string ErrorResponse(string message)
        {
            return JsonConvert.SerializeObject(new
            {
                status = "error",
                message
            });
        }
        #endregion
    }
    #endregion

    #region Program Entry
    public class Program
    {
        private static async Task Main(string[] args)
        {
            const string connectionString = "server=localhost;user=root;password=1234;database=unityproject";
            var server = new TcpGameServer(connectionString);

            try
            {
                await server.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Server error: {ex.Message}");
            }
        }
    }
    #endregion


}


