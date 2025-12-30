using DB.Configuration;
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace DB.EF_Core;

public class SQLiteDbContext : DbContext
{
    public DbSet<AccountDb> Accounts { get; set; }
    public DbSet<ChatDb> Chats { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(ConnectionString.GetSqliteConnectionString());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AccountDb>(ea =>
        {
            // Account의 이름으로 인덱스 추가 및 이름 중복 안 되도록
            ea.HasIndex(a => a.Name)
                .IsUnique();

            ea.Property(a => a.Name)
                .IsRequired();

            ea.Property(a => a.Password)
                .IsRequired();
        });

        modelBuilder.Entity<ChatDb>(ec =>
        {
            // Account와 Chat의 일대다 관계 설정
            ec.HasOne(c => c.Sender)
                .WithMany(a => a.Chats)
                .HasForeignKey(c => c.SenderId)
                .IsRequired();

            // 채팅의 텍스트를 256자로 제한
            ec.Property(c => c.Text)
                .IsRequired()
                .HasMaxLength(256);
        });
    }
}

[Table("Account")]
public class AccountDb
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public List<ChatDb> Chats { get; set; } = new List<ChatDb>();

    public override string ToString() { return Name; }
}

[Table("Chat")]
public class ChatDb
{
    [Key]
    public int Id { get; set; }
    public DateTime DateTime { get; set; }
    public string Text { get; set; } = string.Empty;

    [ForeignKey("Account")]
    public int SenderId { get; set; }
    public AccountDb Sender { get; set; } = null!;
}

