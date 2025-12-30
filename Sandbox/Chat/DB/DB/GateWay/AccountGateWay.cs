using DB.Configuration;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Diagnostics;
using System.Linq;

namespace DB.GateWay;

public class AccountGateWay
{
    public int Id { get; private set; }
    public string Name { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public override string ToString()
    {
        return Name;
    }

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

            // 중복되지 않는 PK 구하기
            {
                // 가장 큰 Id를 가진 계정 데이터 구하기
                AccountGateWay? biggestIdAccount = SelectAll().OrderByDescending(a => a.Id).FirstOrDefault();
                Id = biggestIdAccount == null ? 0 : biggestIdAccount.Id + 1;
            }

            SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Account(Id, Name, Password) VALUES(@Id, @Name, @Password)";
            command.Parameters.Add(new SqliteParameter("@Id", Id));
            command.Parameters.Add(new SqliteParameter("@Name", Name));
            command.Parameters.Add(new SqliteParameter("@Password", Password));
            command.ExecuteNonQuery();

            connection.Close();
            return true;
        }
    }

    public static List<AccountGateWay> SelectAll()
    {
        // DB 연결
        using (SqliteConnection connection = new SqliteConnection(ConnectionString.GetSqliteConnectionString()))
        {
            connection.Open();

            // DB 연결 실패 시 빈 리스트 반환
            if (connection.State != ConnectionState.Open)
            {
                Trace.WriteLine($"Failed to connect Sql Lite Server, Connection String : {ConnectionString.GetSqliteConnectionString()}");
                return new List<AccountGateWay>();
            }

            List<AccountGateWay> result = new List<AccountGateWay>();

            SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Name, Password FROM Account";
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    int index = 0;

                    int id = reader.GetInt32(index++);
                    string name = reader.GetString(index++);
                    string password = reader.GetString(index++);

                    AccountGateWay item = new AccountGateWay();
                    item.Id = id;
                    item.Name = name;
                    item.Password = password;

                    result.Add(item);
                }
            }

            connection.Close();

            return result;
        }
    }

    public static AccountGateWay? Select(int selectId)
    {
        // DB 연결
        using (SqliteConnection connection = new SqliteConnection(ConnectionString.GetSqliteConnectionString()))
        {
            connection.Open();

            // DB 연결 실패 시 null 반환
            if (connection.State != ConnectionState.Open)
            {
                Trace.WriteLine($"Failed to connect Sql Lite Server, Connection String : {ConnectionString.GetSqliteConnectionString()}");
                return null;
            }

            AccountGateWay? result = null;

            SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Name, Password FROM Account WHERE Id=@Id";
            command.Parameters.Add(new SqliteParameter("@Id", selectId));

            using (SqliteDataReader reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    int index = 0;

                    int id = reader.GetInt32(index++);
                    string name = reader.GetString(index++);
                    string password = reader.GetString(index++);

                    result = new AccountGateWay();
                    result.Id = id;
                    result.Name = name;
                    result.Password = password;
                }
            }

            connection.Close();

            return result;
        }
    }

    public static AccountGateWay? Select(string selectName)
    {
        // DB 연결
        using (SqliteConnection connection = new SqliteConnection(ConnectionString.GetSqliteConnectionString()))
        {
            connection.Open();

            // DB 연결 실패 시 null 반환
            if (connection.State != ConnectionState.Open)
            {
                Trace.WriteLine($"Failed to connect Sql Lite Server, Connection String : {ConnectionString.GetSqliteConnectionString()}");
                return null;
            }

            AccountGateWay? result = null;

            SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Name, Password FROM Account WHERE Name=@Name";
            command.Parameters.Add(new SqliteParameter("@Name", selectName));

            using (SqliteDataReader reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    int index = 0;

                    int id = reader.GetInt32(index++);
                    string name = reader.GetString(index++);
                    string password = reader.GetString(index++);

                    result = new AccountGateWay();
                    result.Id = id;
                    result.Name = name;
                    result.Password = password;
                }
            }

            connection.Close();

            return result;
        }
    }
}

