using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EfSqlEncrypted
{
    public class EfContext : DbContext
    {
        private readonly string _connectionString;
        public DbSet<Patient> Patients { get; set; }
        public EfContext() 
        {
            var configuration = EnvironmentBuilder.InitializeConfig();

            _connectionString = configuration["DbConnectionString"];

            SqlProviderBuilder.InitializeAzureKeyVaultProvider();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var connection = new SqlConnection(_connectionString);

            optionsBuilder.UseSqlServer(connection);
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Patient>().ToTable("Patients");
        }
    }
}
