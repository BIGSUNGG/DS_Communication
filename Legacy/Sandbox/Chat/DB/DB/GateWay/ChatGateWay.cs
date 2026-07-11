using DB.Configuration;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Diagnostics;
using System.Linq;

namespace DB.GateWay;

public class ChatGateWay
{
    public int Id { get; private set; }
    public DateTime DateTime { get; set; }
    public AccountGateWay? Sender { get; set; }
    public string Text { get; set; } = string.Empty;

    public bool Insert()
    {
        // DB 연결
        using (SqliteConnection connection = new SqliteConnection(ConnectionString.GetSqliteConnectionString()))
        {
            connection.Open();

            // DB 연결 실패 시 false 반환
            if (connection.State != ConnectionState.Open)
            {
                Trace.WriteLine($"Failed to connect Sql Lite Server, Connection String : {ConnectionString.GetSqliteConnectionString()}");
                return false;
            }

            if (Sender == null)
            {
                Trace.WriteLine("Sender is null, cannot insert chat");
                return false;
            }

            // 중복되지 않는 PK 구하기
            {
                // 가장 큰 Id를 가진 채팅 데이터 구하기
                ChatGateWay? biggestIdChat = SelectAll().OrderByDescending(c => c.Id).FirstOrDefault();
                Id = biggestIdChat == null ? 0 : biggestIdChat.Id + 1;
            }

            SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Chat(Id, DateTime, Text, SenderId) VALUES(@Id, @DateTime, @Text, @SenderId)";
            command.Parameters.Add(new SqliteParameter("@Id", Id));
            command.Parameters.Add(new SqliteParameter("@DateTime", DateTime.ToString("yyyy-MM-dd HH:mm:ss")));
            command.Parameters.Add(new SqliteParameter("@Text", Text));
            command.Parameters.Add(new SqliteParameter("@SenderId", Sender.Id));
            command.ExecuteNonQuery();

            connection.Close();
            return true;
        }
    }

    public static List<ChatGateWay> SelectAll(AccountGateWay? filterAccount = null, int filterCount = 0)
    {
        // DB 연결
        using (SqliteConnection connection = new SqliteConnection(ConnectionString.GetSqliteConnectionString()))
        {
            connection.Open();

            // DB 연결 실패 시 빈 리스트 반환
            if (connection.State != ConnectionState.Open)
            {
                Trace.WriteLine($"Failed to connect Sql Lite Server, Connection String : {ConnectionString.GetSqliteConnectionString()}");
                return new List<ChatGateWay>();
            }

            List<ChatGateWay> result = new List<ChatGateWay>();

            SqliteCommand command = connection.CreateCommand();
            command.CommandText = $@"
SELECT Id, DateTime, Text, SenderId 
FROM Chat 
{(filterAccount == null ? "" : $"WHERE SenderId == {filterAccount.Id}")} 
ORDER BY DateTime DESC
{(filterCount == 0 ? "" : $"LIMIT {filterCount}")}
";
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    int index = 0;

                    int id = reader.GetInt32(index++);
                    DateTime dateTime = DateTime.Parse(reader.GetString(index++));
                    string text = reader.GetString(index++);
                    int senderId = reader.GetInt32(index++);

                    ChatGateWay item = new ChatGateWay();
                    item.Id = id;
                    item.DateTime = dateTime;
                    item.Text = text;
                    item.Sender = AccountGateWay.Select(senderId);

                    if (item.Sender == null)
                        Trace.WriteLine($"Account id {senderId} is not exist in db");

                    result.Add(item);
                }
            }

            connection.Close();

            return result;
        }
    }
}

