using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.AlwaysEncrypted.AzureKeyVaultProvider;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

namespace EfSqlEncrypted
{
    public class MigrationSqlBuilder 
    {
        private readonly string _keyVaultName;
        private readonly string _keyVaultKeyName;
        private readonly string _keyVaultKeyVersion;
        private readonly string _connectionString;
        private const string ApplicationName = "patients";
        private readonly string _clientId;
        private readonly string _clientSecret;

        private const string CreateColumnEncryptionKeyTemplate = @" 
            CREATE COLUMN ENCRYPTION KEY [{0}] 
            WITH VALUES 
            ( 
                COLUMN_MASTER_KEY = [{1}], 
                ALGORITHM = 'RSA_OAEP', 
                ENCRYPTED_VALUE = {2} 
            );";

        public MigrationSqlBuilder()
        {

            var path = Directory.GetCurrentDirectory();

            var configuration = new ConfigurationBuilder()
                .SetBasePath(path)
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true)
                .Build();


            _clientId = configuration["Authentication:ClientId"];
            _clientSecret = configuration["Authentication:ClientSecret"];
            _connectionString = configuration["DbConnectionString"];

            _keyVaultName = configuration["KeyVault:Name"];
            _keyVaultKeyName = configuration["KeyVault:KeyName"];
            _keyVaultKeyVersion = configuration["KeyVault:KeyVersion"];

            _connectionString = configuration["DbConnectionString"];
        }

        public string PatientsEncryptionDrop()
        {
            return @"DELETE FROM Patients;
                    ALTER TABLE Patients
                        DROP
                            COLUMN IF EXISTS FirstName, 
                            COLUMN IF EXISTS LastName,
                            COLUMN IF EXISTS SSN; 

                    ALTER TABLE Patients ADD SSN nvarchar(20) NOT NULL
                    ALTER TABLE Patients ADD FirstName nvarchar(255) NOT NULL
                    ALTER TABLE Patients ADD LastName nvarchar(255) NOT NULL";
        }

        public string PatientsEncryptionAdd()
        {
            return @$"DELETE FROM Patients;
                    ALTER TABLE Patients
                        DROP
                            COLUMN IF EXISTS FirstName, 
                            COLUMN IF EXISTS LastName,
                            COLUMN IF EXISTS SSN; 

                    ALTER TABLE Patients ADD SSN nvarchar(20) ENCRYPTED WITH (ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256', COLUMN_ENCRYPTION_KEY = {ApplicationName}) NOT NULL
                    ALTER TABLE Patients ADD FirstName nvarchar(255) ENCRYPTED WITH (ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256', COLUMN_ENCRYPTION_KEY = {ApplicationName}) NOT NULL
                    ALTER TABLE Patients ADD LastName nvarchar(255) ENCRYPTED WITH (ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256', COLUMN_ENCRYPTION_KEY = {ApplicationName}) NOT NULL";
        }

        public void CreateMasterKey()
        {

            string keyStoreProviderName = SqlColumnEncryptionAzureKeyVaultProvider.ProviderName;
            var keyVaultUrl = $"https://{_keyVaultName}.vault.azure.net/keys/{_keyVaultKeyName}/{_keyVaultKeyVersion}";

            var cmkSign = SqlProviderBuilder.Provider.SignColumnMasterKeyMetadata(keyVaultUrl, true);
            string cmkSignStr = string.Concat("0x", BitConverter.ToString(cmkSign).Replace("-", string.Empty));

            string sql =
                $@"CREATE COLUMN MASTER KEY [{ApplicationName}]
                    WITH (
                        KEY_STORE_PROVIDER_NAME = N'{keyStoreProviderName}',
                        KEY_PATH = N'{keyVaultUrl}'
                    );";

            DropObjects("Patients");
            ExecuteSql(sql);            
        }

        private void DropObjects(string tblName)
        {
            var sql = $@"IF EXISTS (select * from sys.objects where name = '{tblName}') BEGIN DROP TABLE [{tblName}] END";
            ExecuteSql(sql);

            sql = $@"IF EXISTS (select * from sys.column_encryption_keys where name = '{ApplicationName}') BEGIN DROP COLUMN ENCRYPTION KEY [{ApplicationName}] END";
            ExecuteSql(sql);

            sql = $@"IF EXISTS (select * from sys.column_master_keys where name = '{ApplicationName}') BEGIN DROP COLUMN MASTER KEY [{ApplicationName}] END";
            ExecuteSql(sql);
        }

        public void CreateEncryptionKey()
        {
            var keyId = $"https://{_keyVaultName}.vault.azure.net/keys/{_keyVaultKeyName}";
            CreateColumnEncryptionKey(keyId);
        }

        [SuppressMessage("Microsoft.Security", "CA2100", 
            Justification = "The SqlCommand text is issuing a DDL statement that requires to use only literals (no parameterization is possible). The user input is being escaped.", Scope = "method")]
        private void CreateColumnEncryptionKey(string keyId)
        {
            // Generate the raw bytes that will be used as a key by using a CSPRNG 
            var cekRawValue = new byte[32];
            var provider = new RNGCryptoServiceProvider();
            provider.GetBytes(cekRawValue);

            var cekEncryptedValue = SqlProviderBuilder.Provider.EncryptColumnEncryptionKey(keyId, @"RSA_OAEP", cekRawValue);

            // Prevent SQL injections by escaping the user-defined tokens 
            var sql =  string.Format(CreateColumnEncryptionKeyTemplate, ApplicationName, ApplicationName, BytesToHex(cekEncryptedValue));

            ExecuteSql(sql);
        }


        private void ExecuteSql(string sql)
        {
            var connection = new SqlConnection(_connectionString);

            connection.Open();
            var command = connection.CreateCommand();

            command.CommandText = sql;

            command.ExecuteNonQuery();
        }

        private static string BytesToHex(byte[] a)
        {
            var temp = BitConverter.ToString(a);
            var len = a.Length;

            // We need to remove the dashes that come from the BitConverter
            var sb = new StringBuilder((len - 2) / 2); // This should be the final size

            foreach (var t in temp)
                if (t != '-')
                    sb.Append(t);

            return "0x" + sb;
        }
    }
}
