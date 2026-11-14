using MarksApi.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MarksApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Student> Students => Set<Student>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Student>(b =>
        {
            b.HasKey(s => s.Id);
            b.HasIndex(s => s.RegistrationNo).IsUnique();
            b.HasIndex(s => s.Email).IsUnique();
            b.Property(s => s.RegistrationNo).HasMaxLength(20).IsRequired();
            b.Property(s => s.FullName).HasMaxLength(100).IsRequired();
            b.Property(s => s.Email).HasMaxLength(150).IsRequired();
        });

        modelBuilder.Entity<Course>(b =>
        {
            b.HasKey(c => c.Id);
            b.HasIndex(c => c.Code).IsUnique();
            b.Property(c => c.Code).HasMaxLength(10).IsRequired();
            b.Property(c => c.Title).HasMaxLength(150).IsRequired();
            b.ToTable(t => t.HasCheckConstraint("CK_Course_CreditHours", "credit_hours BETWEEN 1 AND 4"));
        });

        modelBuilder.Entity<Enrollment>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.StudentId, e.CourseId }).IsUnique();
            b.HasOne(e => e.Student).WithMany(s => s.Enrollments).HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(e => e.Course).WithMany(c => c.Enrollments).HasForeignKey(e => e.CourseId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_Enrollment_Mark", "mark IS NULL OR (mark BETWEEN 0 AND 100)"));
        });

        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);
            b.HasIndex(u => u.Username).IsUnique();
            b.Property(u => u.Username).HasMaxLength(50).IsRequired();
            b.Property(u => u.Role).HasMaxLength(20).IsRequired();
            b.HasOne(u => u.Student).WithMany().HasForeignKey(u => u.StudentId).OnDelete(DeleteBehavior.SetNull);
        });

        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Student>().HasData(
            new Student { Id = 1, RegistrationNo = "CS-2022-001", FullName = "Ali Raza", Email = "ali.raza@example.com", CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Student { Id = 2, RegistrationNo = "CS-2022-002", FullName = "Sara Khan", Email = "sara.khan@example.com", CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );

        modelBuilder.Entity<Course>().HasData(
            new Course { Id = 1, Code = "CS401", Title = "Web API Development", CreditHours = 3, CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Course { Id = 2, Code = "MA201", Title = "Linear Algebra", CreditHours = 2, CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Course { Id = 3, Code = "CS101", Title = "Intro to Programming", CreditHours = 4, CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );

        modelBuilder.Entity<Enrollment>().HasData(
            new Enrollment { Id = 1, StudentId = 1, CourseId = 1, Mark = 80, EnrolledAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Enrollment { Id = 2, StudentId = 1, CourseId = 2, Mark = 60, EnrolledAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Enrollment { Id = 3, StudentId = 2, CourseId = 1, Mark = 70, EnrolledAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Enrollment { Id = 4, StudentId = 2, CourseId = 2, Mark = 90, EnrolledAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Enrollment { Id = 5, StudentId = 1, CourseId = 3, Mark = null, EnrolledAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Enrollment { Id = 6, StudentId = 2, CourseId = 3, Mark = null, EnrolledAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );

        modelBuilder.Entity<User>().HasData(
            new User { Id = 1, Username = "teacher1", PasswordHash = "AQAAAAIAAYagAAAAEL8/Zo8+V36ayp8ipt+tTokS3zg51oNLZ4baaOEnGg2XiLJkeKouiTiD38D8OyxSQA==", Role = "Teacher" },
            new User { Id = 2, Username = "student1", PasswordHash = "AQAAAAIAAYagAAAAEPBTrrK6I2UdQkon63SZRuLxh97sz/22H8TWunIdRvhlZMDAm7/KLaQTj9baum1ToA==", Role = "Student", StudentId = 1 },
            new User { Id = 3, Username = "student2", PasswordHash = "AQAAAAIAAYagAAAAEPBTrrK6I2UdQkon63SZRuLxh97sz/22H8TWunIdRvhlZMDAm7/KLaQTj9baum1ToA==", Role = "Student", StudentId = 2 }
        );
    }
}

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(
                Environment.GetEnvironmentVariable("ConnectionStrings__Database")
                ?? throw new InvalidOperationException("ConnectionStrings__Database is required for EF design-time operations."))
                      .UseSnakeCaseNamingConvention();
        return new AppDbContext(optionsBuilder.Options);
    }
}