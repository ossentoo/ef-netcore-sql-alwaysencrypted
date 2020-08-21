using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using EfSqlEncrypted;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.AlwaysEncrypted.AzureKeyVaultProvider;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

namespace EfTestConsole
{
    /// <summary>
    /// Original code from 
    /// https://docs.microsoft.com/en-us/sql/connect/ado-net/sql/azure-key-vault-enclave-example?view=sql-server-ver15
    /// </summary>
    class Program
    {
        static readonly string s_algorithm = "RSA_OAEP";

        private static IConfiguration _configuration;

        private static string _keyVaultUrl;
        // ******************************************

        static void Main(string[] args)
        {
            EnvironmentBuilder.Build();

            _configuration = EnvironmentBuilder.InitializeConfig();

            var keyVaultName = _configuration["KeyVault:Name"];
            var keyVaultKeyName = _configuration["KeyVault:KeyName"];
            var keyVaultKeyVersion = _configuration["KeyVault:KeyVersion"];
            _keyVaultUrl = $"https://{keyVaultName}.vault.azure.net/keys/{keyVaultKeyName}/{keyVaultKeyVersion}";

            // Initialize AKV provider
            var provider = new SqlColumnEncryptionAzureKeyVaultProvider(AzureActiveDirectoryAuthenticationCallback);

            // Register AKV provider
            SqlConnection.RegisterColumnEncryptionKeyStoreProviders(customProviders: new Dictionary<string, SqlColumnEncryptionKeyStoreProvider>(capacity: 1, comparer: StringComparer.OrdinalIgnoreCase)
                {
                    { SqlColumnEncryptionAzureKeyVaultProvider.ProviderName, provider}
                });
            Console.WriteLine("AKV provider Registered");

            // Create connection to database
            var connectionString = _configuration["DbConnectionString"];

            using SqlConnection sqlConnection = new SqlConnection(connectionString);
            string cmkName = "CMK_WITH_AKV";
            string cekName = "CEK_WITH_AKV";
            string tblName = "AKV_TEST_TABLE";

            var customer = new CustomerRecord(1, @"Microsoft", @"Corporation");

            try
            {
                sqlConnection.Open();

                // Drop Objects if exists
                DropObjects(sqlConnection, cmkName, cekName, tblName);

                // Create Column Master Key with AKV Url
                CreateCmk(sqlConnection, cmkName, provider);
                Console.WriteLine("Column Master Key created.");

                // Create Column Encryption Key
                CreateCek(sqlConnection, cmkName, cekName, provider);
                Console.WriteLine("Column Encryption Key created.");

                // Create Table with Encrypted Columns
                CreateTbl(sqlConnection, cekName, tblName);
                Console.WriteLine("Table created with Encrypted columns.");

                // Insert Customer Record in table
                InsertData(sqlConnection, tblName, customer);
                Console.WriteLine("Encryted data inserted.");

                // Read data from table
                VerifyData(sqlConnection, tblName);
                Console.WriteLine("Data validated successfully.");
            }
            finally
            {
                // Drop table and keys
                DropObjects(sqlConnection, cmkName, cekName, tblName);
                Console.WriteLine("Dropped Table, CEK and CMK");
            }

            Console.WriteLine("Completed AKV provider Sample.");

            Console.ReadKey();
        }

        public static async Task<string> AzureActiveDirectoryAuthenticationCallback(string authority, string resource, string scope)
        {
            var clientId = _configuration["Authentication:ClientId"];
            var clientSecret = _configuration["Authentication:ClientSecret"];

            var authContext = new AuthenticationContext(authority);
            var clientCred = new ClientCredential(clientId, clientSecret);
            var result = await authContext.AcquireTokenAsync(resource, clientCred);
            if (result == null)
            {
                throw new InvalidOperationException($"Failed to retrieve an access token for {resource}");
            }

            return result.AccessToken;
        }

        private static void CreateCmk(SqlConnection sqlConnection, string cmkName, SqlColumnEncryptionAzureKeyVaultProvider provider)
        {
            string KeyStoreProviderName = SqlColumnEncryptionAzureKeyVaultProvider.ProviderName;

            byte[] cmkSign = provider.SignColumnMasterKeyMetadata(_keyVaultUrl, true);
            string cmkSignStr = string.Concat("0x", BitConverter.ToString(cmkSign).Replace("-", string.Empty));

            string sql =
                $@"CREATE COLUMN MASTER KEY [{cmkName}]
                    WITH (
                        KEY_STORE_PROVIDER_NAME = N'{KeyStoreProviderName}',
                        KEY_PATH = N'{_keyVaultUrl}',
                        ENCLAVE_COMPUTATIONS (SIGNATURE = {cmkSignStr})
                    );";

            using SqlCommand command = sqlConnection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static void CreateCek(SqlConnection sqlConnection, string cmkName, string cekName, SqlColumnEncryptionAzureKeyVaultProvider sqlColumnEncryptionAzureKeyVaultProvider)
        {
            string sql =
                $@"CREATE COLUMN ENCRYPTION KEY [{cekName}] 
                    WITH VALUES (
                        COLUMN_MASTER_KEY = [{cmkName}],
                        ALGORITHM = '{s_algorithm}', 
                        ENCRYPTED_VALUE = {GetEncryptedValue(sqlColumnEncryptionAzureKeyVaultProvider)}
                    )";

            using SqlCommand command = sqlConnection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static string GetEncryptedValue(SqlColumnEncryptionAzureKeyVaultProvider sqlColumnEncryptionAzureKeyVaultProvider)
        {
            byte[] plainTextColumnEncryptionKey = new byte[32];
            RNGCryptoServiceProvider rngCsp = new RNGCryptoServiceProvider();
            rngCsp.GetBytes(plainTextColumnEncryptionKey);

            byte[] encryptedColumnEncryptionKey = sqlColumnEncryptionAzureKeyVaultProvider.EncryptColumnEncryptionKey(_keyVaultUrl, s_algorithm, plainTextColumnEncryptionKey);
            string encryptedValue = string.Concat("0x", BitConverter.ToString(encryptedColumnEncryptionKey).Replace("-", string.Empty));
            return encryptedValue;
        }

        private static void CreateTbl(SqlConnection sqlConnection, string cekName, string tblName)
        {
            string ColumnEncryptionAlgorithmName = @"AEAD_AES_256_CBC_HMAC_SHA_256";

            string sql =
                    $@"CREATE TABLE [dbo].[{tblName}]
                (
                    [CustomerId] [int] ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = [{cekName}], ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = '{ColumnEncryptionAlgorithmName}'),
                    [FirstName] [nvarchar](50) COLLATE Latin1_General_BIN2 ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = [{cekName}], ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = '{ColumnEncryptionAlgorithmName}'),
                    [LastName] [nvarchar](50) COLLATE Latin1_General_BIN2 ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = [{cekName}], ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = '{ColumnEncryptionAlgorithmName}')
                )";

            using var command = sqlConnection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static void InsertData(SqlConnection sqlConnection, string tblName, CustomerRecord customer)
        {
            string insertSql = $"INSERT INTO [{tblName}] (CustomerId, FirstName, LastName) VALUES (@CustomerId, @FirstName, @LastName);";

            using var sqlTransaction = sqlConnection.BeginTransaction();
            using var sqlCommand = new SqlCommand(insertSql,
                connection: sqlConnection, transaction: sqlTransaction,
                columnEncryptionSetting: SqlCommandColumnEncryptionSetting.Enabled);

            sqlCommand.Parameters.AddWithValue(@"CustomerId", customer.Id);
            sqlCommand.Parameters.AddWithValue(@"FirstName", customer.FirstName);
            sqlCommand.Parameters.AddWithValue(@"LastName", customer.LastName);

            sqlCommand.ExecuteNonQuery();
            sqlTransaction.Commit();
        }

        private static void VerifyData(SqlConnection sqlConnection, string tblName)
        {
            // Test INPUT parameter on an encrypted parameter
            using var sqlCommand = new SqlCommand($"SELECT CustomerId, FirstName, LastName FROM [{tblName}] WHERE FirstName = @firstName",
                sqlConnection);
            var customerFirstParam = sqlCommand.Parameters.AddWithValue(@"firstName", @"Microsoft");
            customerFirstParam.Direction = System.Data.ParameterDirection.Input;
            customerFirstParam.ForceColumnEncryption = true;

            using var sqlDataReader = sqlCommand.ExecuteReader();
            ValidateResultSet(sqlDataReader);
        }

        private static void ValidateResultSet(SqlDataReader sqlDataReader)
        {
            Console.WriteLine(" * Row available: " + sqlDataReader.HasRows);

            while (sqlDataReader.Read())
            {
                if (sqlDataReader.GetInt32(0) == 1)
                {
                    Console.WriteLine(" * Employee Id received as sent: " + sqlDataReader.GetInt32(0));
                }
                else
                {
                    Console.WriteLine("Employee Id didn't match");
                }

                if (sqlDataReader.GetString(1) == @"Microsoft")
                {
                    Console.WriteLine(" * Employee Firstname received as sent: " + sqlDataReader.GetString(1));
                }
                else
                {
                    Console.WriteLine("Employee FirstName didn't match.");
                }

                if (sqlDataReader.GetString(2) == @"Corporation")
                {
                    Console.WriteLine(" * Employee LastName received as sent: " + sqlDataReader.GetString(2));
                }
                else
                {
                    Console.WriteLine("Employee LastName didn't match.");
                }
            }
        }

        private static void DropObjects(SqlConnection sqlConnection, string cmkName, string cekName, string tblName)
        {
            using var cmd = sqlConnection.CreateCommand();
            cmd.CommandText = $@"IF EXISTS (select * from sys.objects where name = '{tblName}') BEGIN DROP TABLE [{tblName}] END";
            cmd.ExecuteNonQuery();
            cmd.CommandText = $@"IF EXISTS (select * from sys.column_encryption_keys where name = '{cekName}') BEGIN DROP COLUMN ENCRYPTION KEY [{cekName}] END";
            cmd.ExecuteNonQuery();
            cmd.CommandText = $@"IF EXISTS (select * from sys.column_master_keys where name = '{cmkName}') BEGIN DROP COLUMN MASTER KEY [{cmkName}] END";
            cmd.ExecuteNonQuery();
        }

        private class CustomerRecord
        {
            internal int Id { get; set; }
            internal string FirstName { get; set; }
            internal string LastName { get; set; }

            public CustomerRecord(int id, string fName, string lName)
            {
                Id = id;
                FirstName = fName;
                LastName = lName;
            }
        }
    }
}