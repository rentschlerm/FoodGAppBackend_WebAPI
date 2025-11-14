using System;
using System.Collections.Generic;
using FoodGappBackend_WebAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace FoodGappBackend_WebAPI.Data;

public partial class FoodGappDbContext : DbContext
{
	public FoodGappDbContext()
	{
	}

	public FoodGappDbContext(DbContextOptions<FoodGappDbContext> options)
		: base(options)
	{
	}

	public virtual DbSet<BodyGoal> BodyGoals { get; set; }

	public virtual DbSet<DailyIntake> DailyIntakes { get; set; }

	public virtual DbSet<Food> Foods { get; set; }

	public virtual DbSet<FoodCategory> FoodCategories { get; set; }

	public virtual DbSet<FoodLog> FoodLogs { get; set; }

	public virtual DbSet<MealPlan> MealPlans { get; set; }

	public virtual DbSet<NutrientLog> NutrientLogs { get; set; }

	public virtual DbSet<Role> Roles { get; set; }

	public virtual DbSet<User> Users { get; set; }

	public virtual DbSet<UserRole> UserRoles { get; set; }

	//	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	//	{
	//#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
	//		//=> optionsBuilder.UseSqlServer("Server=LAPTOP-1GERCPHB\\SQLEXPRESS;Database=FoodGAppDB;Trusted_Connection=True;Integrated Security=True;TrustServerCertificate=True");
	//		if (!optionsBuilder.IsConfigured)
	//		{
	//			// leave empty so Program.cs controls the configuration
	//		}
	//	}
	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		if (!optionsBuilder.IsConfigured)
		{
			// Get connection string from environment variable (Railway provides this)
			var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
				?? Environment.GetEnvironmentVariable("DefaultConnection")
				?? "Server=ballast.proxy.rlwy.net;Port=27006;Database=railway;User=root;Password=iGhQhsUlNAZuFEdbikZmeXybbxsTkkHJ;"; // fallback for local dev

			optionsBuilder.UseMySql(
				connectionString,
				new MySqlServerVersion(new Version(8, 0, 36))
			);
		}
	}
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<BodyGoal>(entity =>
		{
			entity.ToTable("BodyGoal");

			entity.Property(e => e.BodyGoalDesc).HasMaxLength(100);
			entity.Property(e => e.BodyGoalName).HasMaxLength(50);
		});

		modelBuilder.Entity<DailyIntake>(entity =>
		{
			entity.ToTable("DailyIntake");

			entity.Property(e => e.UpdatedAt).HasColumnType("datetime");

			entity.HasOne(d => d.User).WithMany(p => p.DailyIntakes)
				.HasForeignKey(d => d.UserId)
				.HasConstraintName("FK_DailyIntake_User");
		});

		modelBuilder.Entity<Food>(entity =>
		{
			entity.ToTable("Food");

			entity.Property(e => e.FoodName).HasMaxLength(100);

			entity.HasOne(d => d.FoodCategory).WithMany(p => p.Foods)
				.HasForeignKey(d => d.FoodCategoryId)
				.HasConstraintName("FK_Food_FoodCategory");
		});

		modelBuilder.Entity<FoodCategory>(entity =>
		{
			entity.ToTable("FoodCategory");

			entity.Property(e => e.FoodCategoryName).HasMaxLength(50);
		});

		modelBuilder.Entity<FoodLog>(entity =>
		{
			entity.ToTable("FoodLog");

			entity.HasOne(d => d.FoodCategory).WithMany(p => p.FoodLogs)
				.HasForeignKey(d => d.FoodCategoryId)
				.HasConstraintName("FK_FoodLog_FoodCategory");

			entity.HasOne(d => d.Food).WithMany(p => p.FoodLogs)
				.HasForeignKey(d => d.FoodId)
				.HasConstraintName("FK_FoodLog_Food");

			entity.HasOne(d => d.User).WithMany(p => p.FoodLogs)
				.HasForeignKey(d => d.UserId)
				.HasConstraintName("FK_FoodLog_User");
		});

		modelBuilder.Entity<MealPlan>(entity =>
		{
			entity.ToTable("MealPlan");

			entity.Property(e => e.Date).HasColumnType("datetime");
			entity.Property(e => e.MealType).HasMaxLength(50);

			entity.HasOne(d => d.User).WithMany(p => p.MealPlans)
				.HasForeignKey(d => d.UserId)
				.HasConstraintName("FK_MealPlan_User");
		});

		modelBuilder.Entity<NutrientLog>(entity =>
		{
			entity.ToTable("NutrientLog");

			entity.Property(e => e.Calories).HasMaxLength(50);
			entity.Property(e => e.Fat).HasMaxLength(50);
			entity.Property(e => e.Protein).HasMaxLength(50);
			entity.Property(e => e.UpdatedAt).HasColumnType("datetime");

			entity.HasOne(d => d.FoodCategory).WithMany(p => p.NutrientLogs)
				.HasForeignKey(d => d.FoodCategoryId)
				.HasConstraintName("FK_NutrientLog_FoodCategory");

			entity.HasOne(d => d.Food).WithMany(p => p.NutrientLogs)
				.HasForeignKey(d => d.FoodId)
				.HasConstraintName("FK_NutrientLog_Food");

			entity.HasOne(d => d.User).WithMany(p => p.NutrientLogs)
				.HasForeignKey(d => d.UserId)
				.HasConstraintName("FK_NutrientLog_User");
		});

		modelBuilder.Entity<Role>(entity =>
		{
			entity.ToTable("Role");

			entity.Property(e => e.RoleName).HasMaxLength(100);
		});

		modelBuilder.Entity<User>(entity =>
		{
			entity.ToTable("User");

			entity.Property(e => e.Email).HasMaxLength(100);
			entity.Property(e => e.FirstName).HasMaxLength(50);
			entity.Property(e => e.IsActive).HasDefaultValue(true);
			entity.Property(e => e.LastName).HasMaxLength(50);
			entity.Property(e => e.Password).HasMaxLength(100);

			entity.HasOne(d => d.BodyGoal).WithMany(p => p.Users)
				.HasForeignKey(d => d.BodyGoalId)
				.HasConstraintName("FK_User_BodyGoal");
		});

		modelBuilder.Entity<UserRole>(entity =>
		{
			entity.HasKey(e => e.UserRole1);

			entity.ToTable("UserRole");

			entity.Property(e => e.UserRole1).HasColumnName("UserRole");

			entity.HasOne(d => d.Role).WithMany(p => p.UserRoles)
				.HasForeignKey(d => d.RoleId)
				.OnDelete(DeleteBehavior.Cascade)
				.HasConstraintName("FK_UserRole_Role");

			entity.HasOne(d => d.User).WithMany(p => p.UserRoles)
				.HasForeignKey(d => d.UserId)
				.OnDelete(DeleteBehavior.Cascade)
				.HasConstraintName("FK_UserRole_User");
		});

		OnModelCreatingPartial(modelBuilder);
	}

	partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}