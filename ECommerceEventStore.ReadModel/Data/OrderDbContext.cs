using Microsoft.EntityFrameworkCore;
using ECommerceEventStore.ReadModel.Models;

namespace ECommerceEventStore.ReadModel.Data
{
    public class OrderDbContext : DbContext
    {
        public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }
        
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<OrderStatusHistory> OrderStatusHistory { get; set; }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Order configuration
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.CustomerId);
                entity.Property(e => e.TotalAmount).HasColumnType("decimal(18,2)");
                entity.Property(e => e.Status).IsRequired(false);
                entity.Property(e => e.CreatedAt).IsRequired(false);
                
                // Indexes for frequent queries
                entity.HasIndex(e => e.CustomerId);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.CreatedAt);
            });
            
            // OrderItem configuration
            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OrderId);
                entity.Property(e => e.ProductId).IsRequired(false);
                entity.Property(e => e.ProductName).IsRequired(false).HasMaxLength(200);
                entity.Property(e => e.Quantity).IsRequired(false);
                entity.Property(e => e.UnitPrice).IsRequired(false).HasColumnType("decimal(18,2)");
                
                // Relationship with Order
                entity.HasOne<Order>()
                    .WithMany(o => o.Items)
                    .HasForeignKey(e => e.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            
            // OrderStatusHistory configuration
            modelBuilder.Entity<OrderStatusHistory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OrderId).IsRequired(false);
                entity.Property(e => e.Status).IsRequired(false);
                entity.Property(e => e.Timestamp).IsRequired(false);
                entity.Property(e => e.Reason).IsRequired(false).HasMaxLength(500);
                
                // Relationship with Order
                entity.HasOne<Order>()
                    .WithMany(o => o.StatusHistory)
                    .HasForeignKey(e => e.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
                
                // Index for querying history
                entity.HasIndex(e => new { e.OrderId, e.Timestamp });
            });
        }
    }
}