using System;
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
                SSN = "SSN-989879311",
                FirstName = "Joe",
                LastName = "Bloggs",
                BirthDate = new DateTime(1970, 01, 01)
            };
        }

        [Fact]
        public void ApplyDataMigration()
        {
            using var db = new EfContext();
            db.Database.Migrate();
            AddDataWithEf();
        }

        [Fact]
        public async  Task DecryptData()
        {
            await using var db = new EfContext();

            AddDataWithEf();

            var results = await db.Patients.ToListAsync();

            foreach (var r in results)
            {
                Debug.WriteLine(r.SSN);
            }

            Assert.True(results.Any());
        }

        private void Initialize()
        {

            using (var file = File.OpenText("Properties\\launchSettings.json"))
            {
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
        }

        private void AddDataWithEf()
        {
            using var db = new EfContext();

            var entity = db.Find<Patient>(1);

            if (entity == null)
            {
                AddDataWithSql();
                // db.Patients.Add(_patient);
                // db.SaveChanges();
            }
        }
        
        private void AddDataWithSql()
        {
            SqlProviderBuilder.InitializeAzureKeyVaultProvider();
            var sqlConnection = new SqlConnection(_connectionString);
            string insertSql = "INSERT INTO [Patients] (SSN, FirstName, LastName, BirthDate) VALUES (@SSN, @FirstName, @LastName, @BirthDate);";

            sqlConnection.Open();
            using var sqlTransaction = sqlConnection.BeginTransaction();
            using var sqlCommand = new SqlCommand(insertSql,
                connection: sqlConnection, transaction: sqlTransaction,
                columnEncryptionSetting: SqlCommandColumnEncryptionSetting.Enabled);

            sqlCommand.Parameters.AddWithValue(@"SSN", _patient.SSN);
            sqlCommand.Parameters.AddWithValue(@"FirstName", _patient.FirstName);
            sqlCommand.Parameters.AddWithValue(@"LastName", _patient.LastName);
            sqlCommand.Parameters.AddWithValue(@"BirthDate", DateTime.UtcNow);

            sqlCommand.ExecuteNonQuery();
            sqlTransaction.Commit();
        }
    }
}
