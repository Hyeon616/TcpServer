using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

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
                string requestJson = JsonSerializer.Serialize(new { action = "leave_room", playerId = connection.PlayerId });
                var request = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(requestJson);
                await LeaveRoom(request);
            }

            connections.Remove(connection);
            connection.Close();
        }

        private async Task<string> ActionResponse(string message, TcpServerConnection connection)
        {
            try
            {
                var request = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(message);
                if (!request.TryGetValue("action", out var actionElement))
                {
                    return ErrorResponse("Action not specified");
                }

                string action = actionElement.GetString();
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
        private async Task<string> Register(Dictionary<string, JsonElement> request)
        {
            string id = request["id"].GetString();
            string password = request["password"].GetString();
            string playerName = request["playername"].GetString();

            var (success, message) = await database.Register(id, password, playerName);
            return Response("register", success, message);
        }

        private async Task<string> Login(Dictionary<string, JsonElement> request, TcpServerConnection connection)
        {
            string id = request["id"].GetString();
            string password = request["password"].GetString();

            var (success, message, characterData) = await database.Login(id, password);
            if (success)
            {
                connection.PlayerId = id;
                return JsonSerializer.Serialize(new
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

        private async Task<string> SaveData(Dictionary<string, JsonElement> request)
        {
            try
            {
                string userId = request["userId"].GetString();
                string characterDataJson = request["characterData"].GetRawText();
                var characterData = new PlayerCharacterData
                {
                    PlayerName = request["characterData"].GetProperty("PlayerName").GetString(),
                    PlayerId = request["characterData"].GetProperty("PlayerId").GetString(),
                    Gems = request["characterData"].GetProperty("Gems").GetInt32(),
                    Coins = request["characterData"].GetProperty("Coins").GetInt32(),
                    MaxHealth = request["characterData"].GetProperty("MaxHealth").GetInt32(),
                    HealthEnhancement = request["characterData"].GetProperty("HealthEnhancement").GetInt32(),
                    AttackPower = request["characterData"].GetProperty("AttackPower").GetInt32(),
                    AttackEnhancement = request["characterData"].GetProperty("AttackEnhancement").GetInt32(),
                    WeaponEnhancement = request["characterData"].GetProperty("WeaponEnhancement").GetInt32(),
                    ArmorEnhancement = request["characterData"].GetProperty("ArmorEnhancement").GetInt32()
                };

                var (success, message) = await database.SavePlayerData(userId, characterData);
                return Response("save", success, message);
            }
            catch (Exception ex)
            {
                return ErrorResponse($"Failed to save player data: {ex.Message}");
            }
        }

        private async Task<string> CreateRoom(Dictionary<string, JsonElement> request)
        {
            string roomName = request["roomName"].GetString();
            string hostId = request["hostId"].GetString();
            string mapName = request["mapName"].GetString();

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

            rooms[roomName] = room;
            playerRooms[hostId] = roomName;

            await BroadcastRoomList();
            return Response("create_room", true, "방 생성 성공", new { room });
        }

        private async Task<string> JoinRoom(Dictionary<string, JsonElement> request)
        {
            string roomName = request["roomName"].GetString();
            string playerId = request["playerId"].GetString();

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

            room.Players.Add(playerId);
            playerRooms[playerId] = roomName;

            await BroadcastRoomList();
            return Response("join_room", true, "방 참가 성공", new { room });
        }

        private async Task<string> LeaveRoom(Dictionary<string, JsonElement> request)
        {
            string playerId = request["playerId"].GetString();

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


        private async Task<string> StartGame(Dictionary<string, JsonElement> request)
        {
            string roomName = request["roomName"].GetString();
            string hostId = request["hostId"].GetString();
            string sceneName = request["sceneName"].GetString();

            if (!rooms.TryGetValue(roomName, out Room room))
            {
                return ErrorResponse("방을 찾을 수 없습니다.");
            }

            if (room.HostId != hostId)
            {
                return ErrorResponse("방장만 게임을 시작할 수 있습니다.");
            }

            var gameData = new Dictionary<string, object>
        {
            { "sceneName", sceneName }
        };

            var response = Response("start_game", true, "게임을 시작합니다.", gameData);
            await BroadcastToRoom(roomName, response);
            return response;
        }

        private async Task<string> GetRoomList()
        {
            return Response("get_room_list", true, "", rooms.Values);
        }

        private async Task<string> PlayerSpawn(Dictionary<string, JsonElement> request)
        {
            string playerId = request["playerId"].GetString();
            var position = request["position"].GetRawText();
            int maxHealth = request["maxHealth"].GetInt32();
            int attackPower = request["attackPower"].GetInt32();

            if (playerRooms.TryGetValue(playerId, out string roomName))
            {
                var spawnMessage = JsonSerializer.Serialize(new
                {
                    action = "player_spawn",
                    playerId,
                    position = JsonSerializer.Deserialize<object>(position),
                    maxHealth,
                    attackPower
                });

                await BroadcastToRoom(roomName, spawnMessage);
            }

            return Response("player_spawn", true, "");
        }

        private async Task<string> PlayerState(Dictionary<string, JsonElement> request)
        {
            string playerId = request["playerId"].GetString();
            if (playerRooms.TryGetValue(playerId, out string roomName))
            {
                await BroadcastToRoom(roomName, JsonSerializer.Serialize(request));
            }
            return Response("player_state", true, "");
        }

        private async Task<string> PlayerAction(Dictionary<string, JsonElement> request)
        {
            string playerId = request["playerId"].GetString();
            if (playerRooms.TryGetValue(playerId, out string roomName))
            {
                await BroadcastToRoom(roomName, JsonSerializer.Serialize(request));
            }
            return Response("player_action", true, "");
        }
        #endregion

        #region Broadcasting
        private async Task BroadcastToRoom(string roomName, string message)
        {
            if (!rooms.TryGetValue(roomName, out Room room)) return;

            var messageBytes = Encoding.UTF8.GetBytes(message);
            var tasks = new List<Task>();

            foreach (var connection in connections.ToList())
            {
                if (connection.IsConnected && room.Players.Contains(connection.PlayerId))
                {
                    tasks.Add(connection.Send(messageBytes));
                }
            }

            await Task.WhenAll(tasks);
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
                return JsonSerializer.Serialize(response);
            }

            // 게임 시작인 경우 특별 처리
            if (action == "start_game" && data != null)
            {
                var gameData = data as Dictionary<string, object>;
                if (gameData != null && gameData.ContainsKey("sceneName"))
                {
                    response["sceneName"] = gameData["sceneName"];
                }
                return JsonSerializer.Serialize(response);
            }

            // 그 외 데이터가 있는 경우
            if (data != null)
            {
                foreach (var prop in data.GetType().GetProperties())
                {
                    response[prop.Name.ToLower()] = prop.GetValue(data);
                }
            }

            return JsonSerializer.Serialize(response);
        }

        private string ErrorResponse(string message)
        {
            return JsonSerializer.Serialize(new
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


