using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EfSqlEncrypted;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace unit.tests
{
    public class EfSqlEncryptionShould
    {
        private const string Ssn = "SSN-989879311";
        private const string Email = "joe.bloggs@test.com";
        private readonly Patient _patient;
        private readonly string _connectionString;

        public EfSqlEncryptionShould()
        {
            EnvironmentBuilder.Build();
            var configuration = EnvironmentBuilder.InitializeConfig();
            _connectionString = configuration["DbConnectionString"];

            Initialize();
            _patient = new Patient
            {
                Email = Email,
                SSN = Ssn,
                FirstName = "Joe",
                LastName = "Bloggs",
                BirthDate = new DateTime(1970, 01, 01)
            };

            using var db = new EfContext();
            db.Database.Migrate();
        }

        [Fact]
        public void ApplyMigrationWithAndAddData()
        {
            using var db = new EfContext();
            AddDataWithSql();

            var entity = db.Patients.FirstOrDefault(x => x.Email == Email);
             
            Assert.Equal(Email, entity.Email);
            Assert.Equal(Ssn, entity.SSN);

        }

        [Fact]
        public async  Task AddUpdateAndDecryptData()
        {
            await using var db = new EfContext();

            AddDataWithEf();

            var entity = await db.Patients.FirstAsync();
            entity.BirthDate = DateTime.UtcNow.AddYears(-40);
            await db.SaveChangesAsync();

            var entityUpdated = await db.Patients.FirstAsync();

            Assert.True(entityUpdated.BirthDate < DateTime.UtcNow);

            Assert.Equal(Email, entity.Email);
            Assert.Equal(Ssn, entity.SSN);
        }

        private void Initialize()
        {
            using var file = File.OpenText("Properties\\launchSettings.json");
            var reader = new JsonTextReader(file);
            var jObject = JObject.Load(reader);

            var variables = jObject
                .GetValue("profiles")
                //select a proper profile here
                .SelectMany(profiles => profiles.Children())
                .SelectMany(profile => profile.Children<JProperty>())
                .Where(prop => prop.Name == "environmentVariables")
                .SelectMany(prop => prop.Value.Children<JProperty>())
                .ToList();

            foreach (var variable in variables)
            {
                Environment.SetEnvironmentVariable(variable.Name, variable.Value.ToString());
            }
        }

        private void AddDataWithEf()
        {
            using var db = new EfContext();

            var entity = db.Patients.FirstOrDefault(x=>x.Email == Email);

            if (entity != null)
            {
                db.Patients.Remove(entity);
                db.SaveChanges();
            }

            db.Patients.Add(_patient);
            db.SaveChanges();

        }

        private void AddDataWithSql()
        {
            SqlProviderBuilder.InitializeAzureKeyVaultProvider();
            var sqlConnection = new SqlConnection(_connectionString);
            string insertSql = "INSERT INTO [Patients] (Email, SSN, FirstName, LastName, BirthDate) VALUES (@Email, @SSN, @FirstName, @LastName, @BirthDate);";

            sqlConnection.Open();
            using var sqlTransaction = sqlConnection.BeginTransaction();
            using var command = new SqlCommand(insertSql,
                connection: sqlConnection, transaction: sqlTransaction,
                columnEncryptionSetting: SqlCommandColumnEncryptionSetting.Enabled);

            command.Parameters.Add("@Email", SqlDbType.VarChar);
            command.Parameters.Add("@SSN", SqlDbType.VarChar);
            command.Parameters.Add("@FirstName", SqlDbType.VarChar);
            command.Parameters.Add("@LastName", SqlDbType.VarChar);
            command.Parameters.Add("@BirthDate", SqlDbType.DateTime);

            command.Parameters["@Email"].Value = _patient.Email;
            command.Parameters["@SSN"].Value = _patient.SSN;
            command.Parameters["@FirstName"].Value = _patient.FirstName;
            command.Parameters["@LastName"].Value = _patient.LastName;
            command.Parameters["@BirthDate"].Value = DateTime.UtcNow;

            command.ExecuteNonQuery();
            sqlTransaction.Commit();
        }
    }
}
