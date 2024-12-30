using Microsoft.EntityFrameworkCore;
using PollSystem.Models;

namespace PollSystem.Data
{
    public class PollDbContext : DbContext
    {
        public PollDbContext(DbContextOptions<PollDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Poll> Polls { get; set; }
        public DbSet<PollOption> PollOptions { get; set; }
        public DbSet<Vote> Votes { get; set; }
        public DbSet<Comment> Comments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure indexes
            modelBuilder.Entity<User>()
                .HasIndex(u => u.IpAddress)
                .HasDatabaseName("IX_Users_IpAddress");

            modelBuilder.Entity<Poll>()
                .HasIndex(p => p.CreatedBy)
                .HasDatabaseName("IX_Polls_CreatedBy");

            modelBuilder.Entity<Poll>()
                .HasIndex(p => p.StartDate)
                .HasDatabaseName("IX_Polls_StartDate");

            modelBuilder.Entity<Poll>()
                .HasIndex(p => p.EndDate)
                .HasDatabaseName("IX_Polls_EndDate")
                .IncludeProperties(p => p.IsActive);

            modelBuilder.Entity<PollOption>()
                .HasIndex(po => po.PollId)
                .HasDatabaseName("IX_PollOptions_PollId")
                .IncludeProperties(po => new { po.OptionText, po.DisplayOrder });

            // Configure unique constraint for votes
            modelBuilder.Entity<Vote>()
                .HasIndex(v => new { v.PollId, v.UserId })
                .IsUnique()
                .HasFilter("[UserId] IS NOT NULL")
                .HasDatabaseName("UX_Votes_UserPoll");

            // Configure cascade delete
            modelBuilder.Entity<Poll>()
                .HasMany(p => p.Options)
                .WithOne(o => o.Poll)
                .HasForeignKey(o => o.PollId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Poll>()
                .HasMany(p => p.Comments)
                .WithOne(c => c.Poll)
                .HasForeignKey(c => c.PollId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure default values
            modelBuilder.Entity<User>()
                .Property(u => u.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            modelBuilder.Entity<Poll>()
                .Property(p => p.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            modelBuilder.Entity<Poll>()
                .Property(p => p.UpdatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            modelBuilder.Entity<PollOption>()
                .Property(po => po.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            modelBuilder.Entity<Vote>()
                .Property(v => v.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
        }
    }
} 