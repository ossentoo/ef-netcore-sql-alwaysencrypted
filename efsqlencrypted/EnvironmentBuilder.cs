using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EfSqlEncrypted
{
    public static class EnvironmentBuilder
    {
        public static void Build()
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
        public static IConfiguration InitializeConfig()
        {

            var path = Directory.GetCurrentDirectory();

            var environmentVariable = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

            // If this is fill, the secret hasn't been set correctly
            if (string.IsNullOrEmpty(environmentVariable) && environmentVariable.Equals("FILL"))
            {
                throw new ArgumentException("Environment in configuration has not been set");
            }

            var configuration = new ConfigurationBuilder()
                .SetBasePath(path)
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{environmentVariable}.json", optional: true)
                .Build();

            return configuration;
        }
    }
}
