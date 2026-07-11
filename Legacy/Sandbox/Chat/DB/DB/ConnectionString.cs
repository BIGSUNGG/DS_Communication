using System;
using System.IO;

namespace DB.Configuration;

public class ConnectionString
{
    public static string DBPath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

    static string GetConnectionString(string dbName)
    {
        // DB 디렉토리가 없으면 생성
        if (!Directory.Exists(DBPath))
        {
            Directory.CreateDirectory(DBPath);
        }

        return $@"Data Source={Path.Combine(DBPath, dbName)}";
    }

    static public string GetSqliteConnectionString()
    {
        return GetConnectionString(@"Sqlite.db");
    }
}

