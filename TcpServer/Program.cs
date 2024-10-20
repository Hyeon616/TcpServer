using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TcpServer
{
    internal class Program
    {
        readonly static string connectDB = "server=localhost;user=root;password=1234;database=unityproject";

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

                    Console.WriteLine("클라이언트가 연결되었습니다.");

                    NetworkStream stream = client.GetStream();

                    byte[] bytes = new byte[1024];
                    string data = null;
                    int i;

                    while ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
                    {
                        data = Encoding.UTF8.GetString(bytes, 0, i);
                        Console.WriteLine($"응답 : {data}");
                        string response = SendQuery(data);
                        byte[] msg = Encoding.UTF8.GetBytes(response);
                        stream.Write(msg, 0, msg.Length);


                    }

                    client.Close();
                }


            }
            catch (SocketException e)
            {
                Console.WriteLine($"SocketException : {e}");
            }
        }

        private static string SendQuery(string data)
        {
            try
            {
                var request = JsonConvert.DeserializeObject<Dictionary<string, object>>(data);

                if (request.TryGetValue("action", out object actionObj) && actionObj is string action)
                {
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
                    string query = "INSERT INTO players (id, password, playername) VALUES (@id, @password, @playername)";
                    using (MySqlCommand command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@id", id);
                        command.Parameters.AddWithValue("@password", passWord);
                        command.Parameters.AddWithValue("@playername", playerName);
                        command.ExecuteNonQuery();
                    }

                    return JsonConvert.SerializeObject(new { status = "success", action = "register", message = $"ID : {id}, playername : {playerName} 회원가입 성공" });

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
                    using (MySqlCommand command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@id", id);
                        command.Parameters.AddWithValue("@password", passWord);
                        using (MySqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return JsonConvert.SerializeObject(new { status = "success", action = "login" , message = "로그인 성공", playername = reader["playername"].ToString() });
                            }
                            else
                            {
                                return JsonConvert.SerializeObject(new { status = "error", message = "데이터 형식이 잘못되었습니다." });
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

    }
}
