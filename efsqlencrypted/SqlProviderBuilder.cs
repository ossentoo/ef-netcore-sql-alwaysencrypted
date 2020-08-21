using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.AlwaysEncrypted.AzureKeyVaultProvider;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

namespace EfSqlEncrypted
{
    public static class SqlProviderBuilder
    {
        public static SqlColumnEncryptionAzureKeyVaultProvider Provider { get; private set; }
        private static bool _isInitialized;
        private static string _clientId;
        private static string _clientSecret;
        private static string _keyVaultName;
        private static string _keyVaultKeyName;
        private static object _keyVaultKeyVersion;

        public static void InitializeAzureKeyVaultProvider()
        {
            if (_isInitialized)
            {
                return;
            }

            Provider = new SqlColumnEncryptionAzureKeyVaultProvider(GetToken);

            var providers = new Dictionary<string, SqlColumnEncryptionKeyStoreProvider>
            {
                {SqlColumnEncryptionAzureKeyVaultProvider.ProviderName, Provider}
            };

            SqlConnection.RegisterColumnEncryptionKeyStoreProviders(providers);

            var configuration = EnvironmentBuilder.InitializeConfig();

            _clientId = configuration["Authentication:ClientId"];
            _clientSecret = configuration["Authentication:ClientSecret"];
            _keyVaultName = configuration["KeyVault:Name"];
            _keyVaultKeyName = configuration["KeyVault:KeyName"];
            _keyVaultKeyVersion = configuration["KeyVault:KeyVersion"];


            // If this is fill, the secret hasn't been set correctly
            if (_clientId.Equals("FILL") || _clientSecret.Equals("FILL"))
            {
                throw new ArgumentException("Secrets in configuration have not been set");
            }


            var keyVaultUrl = $"https://{_keyVaultName}.vault.azure.net/keys/{_keyVaultKeyName}/{_keyVaultKeyVersion}";

            var cmkSign = Provider.SignColumnMasterKeyMetadata(keyVaultUrl, true);


            _isInitialized = true;
        }

        private static async Task<string> GetToken(string authority, string resource, string scope)
        {
            var appCredentials = new ClientCredential(_clientId, _clientSecret);
            var context = new AuthenticationContext(authority, TokenCache.DefaultShared);

            var result = await context.AcquireTokenAsync(resource, appCredentials);

            return result.AccessToken;
        }

    }
}
